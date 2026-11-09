using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class TaskOperatorActionTests
{
    private string root = null!;
    private RelayService service = null!;
    private string source = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("a", "manual", "甲"); service.RegisterManual("b", "manual", "乙");
        source = Path.Combine(root, "source.md"); File.WriteAllText(source, "Work item");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public void DeletionRequiresConfirmationAndKeepsFileOwnershipForLaterCleanup()
    {
        var task = service.CreateFileHandoff(new("a", "b", source)).Task;
        var receiptPath = service.ManualReceiptPath(task);
        File.WriteAllText(receiptPath, RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId, "b", 0, "working", false)));
        service.ImportManualReceipts();
        Assert.ThrowsExactly<RelayException>(() => service.DeleteTask(task.TaskId, false));
        Assert.AreEqual(TaskState.Running, service.Status(task.TaskId).Task.State);
        service.DeleteTask(task.TaskId, true);
        Assert.IsEmpty(service.Tasks()); Assert.IsEmpty(service.Audit(task.TaskId));
        Assert.HasCount(1, service.Artifacts(task.TaskId)); Assert.IsEmpty(service.Continuations(task.TaskId));
        var retained = service.WorkspaceFiles().Single(file => file.Path == task.RequestPath);
        Assert.AreEqual(WorkspaceFileOwner.Relay, retained.Owner);
        Assert.AreEqual(task.TaskId, retained.OwnerTaskId);
        Assert.IsEmpty(retained.References);
        Assert.IsEmpty(service.Inbox()); Assert.IsTrue(File.Exists(task.RequestPath)); Assert.IsTrue(File.Exists(source));
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
        var store = new RelayStore(service.Home);
        foreach (var table in new[] { "tasks", "callbacks", "events", "outbox", "notifications", "executions", "continuations" })
            Assert.AreEqual(0L, store.Read(s => s.Query($"SELECT COUNT(*) FROM {table}", r => r.GetInt64(0)).Single()));
    }

    [TestMethod]
    public void ForcedCompletionHasOperatorEvidenceAndIgnoresLateAgentReceipts()
    {
        var task = service.CreateFileHandoff(new("a", "b", source)).Task;
        Assert.ThrowsExactly<RelayException>(() => service.ForceCompleteTask(task.TaskId, "决定结束", false));
        var completeView = service.ForceCompleteTask(task.TaskId, "无需继续", true);
        Assert.AreEqual("inspect", completeView.NextAction);
        var completed = completeView.Task;
        Assert.AreEqual(TaskState.Succeeded, completed.State); Assert.AreEqual("operator_completed", completed.ResumeStatus);
        Assert.IsNull(completed.ManualReceiptJson);
        Assert.IsTrue(service.Audit(task.TaskId).Any(e => e.Event == "task.operator_completed" && e.SenderId == "operator" && e.Detail == "无需继续"));
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId, "b", 0, "failed", false)));
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        Assert.IsEmpty(service.Continuations(task.TaskId));
    }

    [TestMethod]
    public void InvalidJsonHasActionableMessageWithoutRuntimeExceptionText()
    {
        var error = Assert.ThrowsExactly<RelayException>(() => ManualTransfers.Parse("{\"from\":\"a\",", service.Agents()));
        Assert.AreEqual("manual_json_invalid", error.Code);
        StringAssert.Contains(error.Message, "重新复制完整 JSON");
        Assert.DoesNotContain("BytePositionInLine", error.Message, StringComparison.Ordinal);
        Assert.IsNotNull(error.Data["technical_detail"]);
    }

    [TestMethod]
    public async Task LegacyTaskDeletionClearsCallbacksAndPendingContinuations()
    {
        var task = (await service.DispatchAsync(new("a", "b", "Review", "manual", "manual", "legacy"))).Task;
        service.Acknowledge(task.TaskId, task.AttemptId, "b", "ack");
        File.WriteAllText(task.ResultPath, "Result");
        service.Complete(task.TaskId, task.AttemptId, "b", "done", task.ResultPath);
        Assert.HasCount(1, service.Continuations(task.TaskId));
        service.DeleteTask(task.TaskId, true);
        var store = new RelayStore(service.Home);
        Assert.AreEqual(0L, store.Read(s => s.Query("SELECT COUNT(*) FROM callbacks", r => r.GetInt64(0)).Single()));
        Assert.IsEmpty(service.Continuations(task.TaskId)); Assert.IsEmpty(service.Audit(task.TaskId));
        Assert.IsTrue(File.Exists(task.ResultPath));
    }

    [TestMethod]
    public async Task ForcedCompletionStopsPendingLegacyContinuation()
    {
        var task = (await service.DispatchAsync(new("a", "b", "Review", "manual", "manual", "legacy"))).Task;
        File.WriteAllText(task.ResultPath, "Failure evidence");
        service.Complete(task.TaskId, task.AttemptId, "b", "fail", task.ResultPath, true, "fixture_failure", "fixture");
        var done = service.ForceCompleteTask(task.TaskId, "Accept the outcome", true);
        Assert.AreEqual(TaskState.Succeeded, done.Task.State);
        var continuation = service.Continuations(task.TaskId).Single();
        Assert.AreEqual("superseded", continuation.State); Assert.AreEqual("not_required", continuation.DesktopState);
        Assert.IsNull(done.Task.ErrorCode); Assert.IsNull(done.Task.ErrorMessage);
    }
}
