using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class HandoffPlanTests
{
    private string root = null!;
    private RelayService service = null!;
    private TaskRecord task = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "manual", "甲"); service.RegisterManual("agent-b", "manual", "乙");
        var file = Path.Combine(root, "work.md"); File.WriteAllText(file, "B handles, A reviews, B applies.");
        task = service.CreateFileHandoff(new("agent-a", "agent-b", file, "完成明确的交接计划")).Task;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    // Fixed plans can no longer be created; tasks saved by earlier versions still carry them in the ledger.
    private void LegacyPlan(params string[] plan) => new RelayStore(service.Home).Write(s =>
    {
        var stored = s.Task(task.TaskId); stored.HandoffPlan = plan; s.Save(stored); return true;
    });

    private ManualReceiptImportResult Complete(bool needsReply = false, bool includeFollowup = false)
    {
        task = service.Status(task.TaskId).Task;
        string? result = null;
        if (includeFollowup)
        {
            result = Path.Combine(root, "reply-" + task.ManualTurn + ".md");
            File.WriteAllText(result, "Work completed for step " + task.ManualTurn);
        }
        var receipt = new ManualReceipt(task.TaskId, task.AttemptId, task.AwaitingAgentId!, task.ManualTurn,
            "done", needsReply, result, includeFollowup ? "按回稿完成下一步实际工作" : null);
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(receipt));
        return service.ImportManualReceipts();
    }

    [TestMethod]
    public void LegacyThreeStepPlanCannotFinishAfterTheReviewStep()
    {
        LegacyPlan("agent-b", "agent-a", "agent-b");
        Assert.IsEmpty(Complete(includeFollowup: true).Errors);
        var review = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.AwaitingReply, review.State); Assert.AreEqual("agent-a", review.AwaitingAgentId);
        Assert.AreEqual(1, review.ManualTurn);
        Assert.IsEmpty(Complete(includeFollowup: true).Errors);
        var apply = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.AwaitingReply, apply.State); Assert.AreEqual("agent-b", apply.AwaitingAgentId);
        Assert.AreEqual(2, apply.ManualTurn);
        Assert.IsEmpty(Complete().Errors);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void PrematureFinalReceiptWithoutARealFollowupCannotEndOrAdvanceThePlan()
    {
        LegacyPlan("agent-b", "agent-a", "agent-b");
        Assert.IsEmpty(Complete(needsReply: true, includeFollowup: true).Errors);
        var before = service.Status(task.TaskId).Task;
        var result = Complete();
        Assert.HasCount(1, result.Errors); Assert.IsEmpty(result.UpdatedTaskIds);
        Assert.Contains("计划仍有后续步骤", result.Errors[0].Message);
        var after = service.Status(task.TaskId).Task;
        Assert.AreEqual(before.State, after.State); Assert.AreEqual(before.ManualTurn, after.ManualTurn);
        Assert.AreEqual("agent-a", after.AwaitingAgentId);
        Assert.IsEmpty(Complete(includeFollowup: true).Errors);
        Assert.AreEqual("agent-b", service.Status(task.TaskId).Task.AwaitingAgentId);
    }

    [TestMethod]
    public void FinalClaimWithAnOutstandingNextActionIsRejectedWithoutAPlan()
    {
        var result = Complete(includeFollowup: true);
        Assert.HasCount(1, result.Errors); Assert.IsEmpty(result.UpdatedTaskIds);
        Assert.Contains("同时给出了下一步工作", result.Errors[0].Message);
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
        Assert.IsEmpty(Complete(needsReply: true, includeFollowup: true).Errors);
        Assert.AreEqual(TaskState.AwaitingReply, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void SimpleUnplannedHandoffStillFinishesAfterOneCompletion()
    {
        Assert.IsEmpty(Complete().Errors);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        Assert.IsNull(service.Status(task.TaskId).Task.HandoffPlan);
    }

    [TestMethod]
    public void FourStepPlanCanFinishAtItsLastStepWithoutHittingTheFollowupLimit()
    {
        LegacyPlan("agent-b", "agent-a", "agent-b", "agent-a");
        for (var index = 0; index < 3; index++) Assert.IsEmpty(Complete(includeFollowup: true).Errors);
        Assert.IsEmpty(Complete().Errors);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        Assert.AreEqual(3, service.Status(task.TaskId).Task.ManualTurn);
    }

    [TestMethod]
    public void NecessaryWorkCanContinueAfterTheMinimumPlanWithoutInfiniteReplies()
    {
        LegacyPlan("agent-b");
        Assert.IsEmpty(Complete(needsReply: true, includeFollowup: true).Errors);
        Assert.AreEqual("agent-a", service.Status(task.TaskId).Task.AwaitingAgentId);
        Assert.IsEmpty(Complete().Errors);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
    }
}
