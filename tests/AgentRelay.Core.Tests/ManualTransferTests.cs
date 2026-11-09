using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class ManualTransferTests
{
    private string root = null!;
    private RelayService service = null!;
    private string source = null!;
    private readonly ManualClock clock = new();

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay"), clock); service.Initialize();
        service.RegisterManual("agent-a", "manual", "甲"); service.RegisterManual("agent-b", "manual", "乙");
        source = Path.Combine(root, "source.zip"); File.WriteAllBytes(source, [0x50, 0x4b, 0x03, 0x04, 0, 255]);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private TaskRecord Create() => service.CreateFileHandoff(new("甲", "乙", source, "请检查文件")).Task;
    private void Receipt(TaskRecord task, string sender, string status, bool needsReply = false,
        string? resultFile = null, string? nextAction = null, int? turn = null) =>
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId,
            sender, turn ?? task.ManualTurn, status, needsReply, resultFile, nextAction)));

    [TestMethod]
    public void FriendlyNameAndIdResolveWithoutInvokingAgents()
    {
        var parsed = ManualTransfers.Parse(RelayJson.Write(new { from = "甲", to = "agent-b", file = source }), service.Agents());
        Assert.AreEqual("agent-a", parsed.From); Assert.AreEqual("agent-b", parsed.To);
        service.RegisterManual("other", "manual", "乙");
        Assert.ThrowsExactly<RelayException>(() => ManualTransfers.Parse(RelayJson.Write(new { from = "甲", to = "乙", file = source }), service.Agents()));
        Assert.ThrowsExactly<RelayException>(() => ManualTransfers.Parse(RelayJson.Write(new { from = "甲", to = "甲", file = source }), service.Agents()));
        Assert.ThrowsExactly<RelayException>(() => ManualTransfers.Parse(RelayJson.Write(new { from = "甲", to = "agent-b", file = "missing" }), service.Agents()));
    }

    [TestMethod]
    public void BinaryHandoffHasOnlyOneManagedCopyAndDuplicatePasteReusesIt()
    {
        var task = Create();
        CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(task.RequestPath));
        Assert.HasCount(1, Directory.GetFiles(Path.GetDirectoryName(task.RequestPath)!));
        Assert.HasCount(1, service.Artifacts(task.TaskId));
        Assert.AreEqual(task.TaskId, Create().TaskId);
        Assert.HasCount(1, service.Tasks());
        Assert.IsFalse(File.Exists(service.ManualReceiptPath(task)));
        Assert.AreEqual(TaskState.AwaitingManual, task.State);
    }

    [TestMethod]
    public void ChangedInstructionsCreateANewHandoffAndKeepTheOriginal()
    {
        var original = Create();
        var updated = service.CreateFileHandoff(new("agent-a", "agent-b", source, "请修正并重新验收")).Task;
        Assert.AreNotEqual(original.TaskId, updated.TaskId);
        Assert.AreEqual("请修正并重新验收", updated.ManualNextAction);
        Assert.AreEqual("请检查文件", service.Status(original.TaskId).Task.ManualNextAction);
        Assert.HasCount(2, service.Tasks());
    }

    [TestMethod]
    public void ChangedSourceContentCreatesANewSnapshot()
    {
        var original = Create();
        var originalBytes = File.ReadAllBytes(original.RequestPath);
        File.WriteAllBytes(source, [0x50, 0x4b, 0x03, 0x04, 1, 254]);
        var updated = Create();
        Assert.AreNotEqual(original.TaskId, updated.TaskId);
        CollectionAssert.AreEqual(File.ReadAllBytes(source), File.ReadAllBytes(updated.RequestPath));
        CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(original.RequestPath));
        Assert.HasCount(2, service.Tasks());
    }

    [TestMethod]
    public void ExplicitCreationKeepsIdenticalFilesAsSeparateTasksWithTheirOwnOptions()
    {
        var original = Create();
        var created = service.CreateFileHandoff(new("agent-a", "agent-b", source, "请检查文件"), reuseExisting:false).Task;
        service.ConfigureFileHandoff(created.TaskId, new(ManualContinuous:true));
        Assert.AreNotEqual(original.TaskId, created.TaskId);
        Assert.IsTrue(service.Status(created.TaskId).Task.ManualContinuous);
        Assert.IsFalse(service.Status(original.TaskId).Task.ManualContinuous);
        Assert.HasCount(2, service.Tasks());
    }

    [TestMethod]
    public void DuplicateImportUsesOriginalInstructionsAfterTheTaskAdvances()
    {
        var original = Create();
        var reply = Path.Combine(root, "reply.md"); File.WriteAllText(reply, "Please decide option 2.");
        Receipt(original, "agent-b", "done", true, reply, "选择选项并更新计划");
        Assert.HasCount(1, service.ImportManualReceipts().UpdatedTaskIds);
        var repeated = Create();
        Assert.AreEqual(original.TaskId, repeated.TaskId);
        Assert.AreEqual(1, repeated.ManualTurn);
        Assert.AreEqual("选择选项并更新计划", repeated.ManualNextAction);
        Assert.HasCount(1, service.Tasks());
    }

    [TestMethod]
    public void AlreadyManagedUnregisteredFileDoesNotGetCopied()
    {
        var path = Path.Combine(service.Home, "workspace", "outgoing", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "Report");
        var task = service.CreateFileHandoff(new("agent-a", "agent-b", path)).Task;
        Assert.AreEqual(path, task.RequestPath); Assert.HasCount(1, service.Artifacts(task.TaskId));
    }

    [TestMethod]
    public void ExplicitReceiptEndsTaskWithoutImportingAnotherSnapshot()
    {
        var task = Create();
        File.WriteAllText(task.ResultPath, "Finished");
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
        Receipt(task, "agent-b", "done", resultFile: task.ResultPath);
        var result = service.ImportManualReceipts();
        Assert.HasCount(1, result.UpdatedTaskIds); Assert.IsEmpty(result.Errors);
        var done = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Succeeded, done.State); Assert.AreEqual(task.ResultPath, done.ResultPath);
        Assert.HasCount(2, service.Artifacts(task.TaskId));
        Assert.HasCount(2, Directory.GetFiles(Path.GetDirectoryName(task.RequestPath)!));
        Assert.IsEmpty(service.Continuations(task.TaskId));
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
    }

    [TestMethod]
    public void FollowupWaitsForRequesterAndOldTurnCannotOverwriteIt()
    {
        var task = Create(); var reply = Path.Combine(root, "reply.md"); File.WriteAllText(reply, "Please decide option 2.");
        Receipt(task, "agent-b", "done", true, reply, "选择选项并更新计划");
        Assert.HasCount(1, service.ImportManualReceipts().UpdatedTaskIds);
        var next = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.AwaitingReply, next.State); Assert.AreEqual("agent-a", next.AwaitingAgentId);
        Assert.AreEqual(1, next.ManualTurn); Assert.AreNotEqual(reply, next.ManualCurrentFile);
        Assert.IsTrue(File.Exists(reply));
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
        Receipt(next, "agent-b", "done", turn: 0);
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
        Receipt(next, "agent-a", "done");
        Assert.HasCount(1, service.ImportManualReceipts().UpdatedTaskIds);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void WrongActorAndIncompleteReceiptDoNotChangeStateOrBlockOtherTasks()
    {
        var task = Create(); Receipt(task, "agent-a", "done");
        Assert.HasCount(1, service.ImportManualReceipts().Errors);
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
        File.WriteAllText(service.ManualReceiptPath(task), "{\"task_id\":");
        var other = Path.Combine(root, "other.md"); File.WriteAllText(other, "Second task");
        var good = service.CreateFileHandoff(new("agent-b", "agent-a", other)).Task; Receipt(good, "agent-a", "done");
        var result = service.ImportManualReceipts();
        Assert.HasCount(1, result.Errors); Assert.HasCount(1, result.UpdatedTaskIds);
        Assert.AreEqual(TaskState.Succeeded, service.Status(good.TaskId).Task.State);
    }

    [TestMethod]
    public void ReceivedAndWorkingReceiptsAreIdempotentAndCannotRegress()
    {
        var task = Create(); Receipt(task, "agent-b", "received"); service.ImportManualReceipts();
        Assert.AreEqual(TaskState.Acknowledged, service.Status(task.TaskId).Task.State);
        var count = service.Audit(task.TaskId).Count;
        var path = service.ManualReceiptPath(task); File.AppendAllText(path, "\n");
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds); Assert.HasCount(count, service.Audit(task.TaskId));
        Receipt(task, "agent-b", "working"); service.ImportManualReceipts();
        Assert.AreEqual(TaskState.Running, service.Status(task.TaskId).Task.State);
        Receipt(task, "agent-b", "received"); Assert.HasCount(1, service.ImportManualReceipts().Errors);
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
        var progress = service.Status(task.TaskId);
        Assert.AreEqual(TaskState.Running, progress.Task.State);
        Assert.AreEqual("working", progress.Progress!.Status!.Agent);
    }

    [TestMethod]
    public void FollowupRequiresConcreteWorkAndPausesAfterFourTurnsUntilTheUserAddsRounds()
    {
        var task = Create(); Receipt(task, "agent-b", "done", true);
        Assert.HasCount(1, service.ImportManualReceipts().Errors);
        void Reply()
        {
            task = service.Status(task.TaskId).Task;
            var reply = service.TurnReplyPath(task);
            File.WriteAllText(reply, "Pending decision " + task.ManualTurn);
            Receipt(task, task.AwaitingAgentId!, "done", true, reply, "需要确认尚未决定的选项");
            Assert.HasCount(1, service.ImportManualReceipts().UpdatedTaskIds);
        }
        for (var turn = 0; turn < RelayService.MaxManualTurns; turn++) Reply();
        var halted = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Paused, halted.State); Assert.AreEqual("round_limit", halted.PauseReason);
        Assert.IsFalse(service.CanSendFileHandoff(halted));
        Assert.AreEqual("user", service.FileHandoffProgress(halted).WaitingOn);
        Assert.ThrowsExactly<RelayException>(() => service.ContinueFileHandoff(task.TaskId));
        var resumed = service.ContinueFileHandoff(task.TaskId, "再确认一次即可", 1).Task;
        Assert.AreEqual(TaskState.AwaitingReply, resumed.State); Assert.AreEqual(5, resumed.MaxTurns);
        StringAssert.Contains(resumed.ManualNextAction, "再确认一次即可");
        Reply();
        Assert.AreEqual("round_limit", service.Status(task.TaskId).Task.PauseReason);
    }

    [TestMethod]
    public void CleanupExpiresOwnedFilesWithoutDeletingExternalOriginals()
    {
        var task = Create(); Receipt(task, "agent-b", "done"); service.ImportManualReceipts();
        clock.Advance(TimeSpan.FromDays(4)); service.Cleanup(true);
        Assert.IsFalse(File.Exists(task.RequestPath)); Assert.IsTrue(File.Exists(source));
    }

    [TestMethod]
    public void ReceiptWrittenAfterTheTurnDeadlineIsStillTheAgentsResult()
    {
        var task = Create();
        Receipt(task, "agent-b", "done");
        clock.Advance(TimeSpan.FromMinutes(31));
        var result = service.ImportManualReceipts();
        Assert.HasCount(1, result.UpdatedTaskIds); Assert.IsEmpty(result.Errors);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        Assert.IsEmpty(service.Continuations(task.TaskId));
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
    }

    [TestMethod]
    public void OverdueTurnPausesRecoverablyAndALateReceiptResumesIt()
    {
        var task = Create();
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.HasCount(1, service.ImportManualReceipts().UpdatedTaskIds);
        var paused = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Paused, paused.State); Assert.AreEqual("turn_timeout", paused.PauseReason);
        Assert.IsEmpty(service.Continuations(task.TaskId));
        Assert.IsEmpty(service.ImportManualReceipts().UpdatedTaskIds);
        Receipt(task, "agent-b", "working");
        Assert.HasCount(1, service.ImportManualReceipts().UpdatedTaskIds);
        var running = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Running, running.State); Assert.IsNull(running.PauseReason);
        Assert.IsTrue(running.DeadlineAt > clock.GetUtcNow());
    }

    [TestMethod]
    public void UserCanKeepWaitingAfterATurnTimeout()
    {
        var task = Create();
        clock.Advance(TimeSpan.FromMinutes(31));
        service.ImportManualReceipts();
        var resumed = service.ContinueFileHandoff(task.TaskId).Task;
        Assert.AreEqual(TaskState.AwaitingManual, resumed.State);
        Assert.IsTrue(resumed.DeadlineAt > clock.GetUtcNow());
        Assert.AreEqual("handoff.continued", service.Audit(task.TaskId).Last().Event);
    }

    [TestMethod]
    public void CleanupKeepsExpiredFilesReferencedByAnUnfinishedHandoff()
    {
        var other = Path.Combine(root, "other.zip"); File.WriteAllBytes(other, [1, 2, 3]);
        var second = service.CreateFileHandoff(new("agent-a", "agent-b", other)).Task;
        clock.Advance(TimeSpan.FromDays(4));
        Assert.AreEqual(0, service.Cleanup(true).Deleted);
        Assert.IsTrue(File.Exists(second.RequestPath));
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(second.TaskId).Task.State);
        Assert.IsEmpty(service.Continuations(second.TaskId));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }
}
