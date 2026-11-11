using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class CollaborationTests
{
    private string root = null!;
    private RelayService service = null!;
    private TaskRecord task = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "manual", "甲"); service.RegisterManual("agent-b", "manual", "乙");
        var file = Path.Combine(root, "work.md"); File.WriteAllText(file, "实现登录页并通过验收。");
        task = service.CreateFileHandoff(new("agent-a", "agent-b", file, "实现并提交")).Task;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private void Loop(string author = "agent-b", int rounds = 5) => task =
        service.ConfigureFileHandoff(task.TaskId, new(new CollaborationOptions(author, "页面可用且无控制台错误", rounds))).Task;

    private ManualReceiptImportResult Send(string outcome, string[]? items = null, string? content = null, string? deliverable = null,
        string status = "done", bool? needsReply = null, string? sender = null)
    {
        task = service.Status(task.TaskId).Task;
        string? result = null;
        if (content is not null)
        {
            result = service.TurnReplyPath(task);
            File.WriteAllText(result, content);
        }
        var receipt = new ManualReceipt(task.TaskId, task.AttemptId, sender ?? task.AwaitingAgentId!, task.ManualTurn, status,
            needsReply, result, null, outcome, items, deliverable);
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(receipt));
        var imported = service.ImportManualReceipts();
        task = service.Status(task.TaskId).Task;
        return imported;
    }

    private void Accepted(ManualReceiptImportResult result) => Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(e => e.Message)));
    private void Rejected(ManualReceiptImportResult result, string message)
    {
        Assert.HasCount(1, result.Errors); Assert.IsEmpty(result.UpdatedTaskIds);
        StringAssert.Contains(result.Errors[0].Message, message);
    }

    private void Cycle(int round)
    {
        Accepted(Send("submitted", content: "版本 " + round));
        Assert.AreEqual("review", task.Collaboration!.Phase); Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual(round, task.Collaboration.ReviewRound);
    }

    [TestMethod]
    public void AgentGeneratedCriteriaMustBeReceivedBeforeUnboundedReviewerFinalLoop()
    {
        task=service.ConfigureFileHandoff(task.TaskId,new(new CollaborationOptions("agent-b",MaxReviewRounds:0,GenerateCriteria:true,ReviewerFinal:true))).Task;
        Assert.AreEqual("criteria",task.Collaboration!.Phase);Assert.AreEqual("agent-a",task.AwaitingAgentId);
        Assert.IsFalse(service.CanSendFileHandoff(task));
        service.FileHandoffPrompt(task);
        var context=File.ReadAllText(Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!,"context.json"));
        StringAssert.Contains(context,"acceptance_criteria");StringAssert.Contains(context,"criteria_ready");
        using(var initial=JsonDocument.Parse(context))Assert.AreEqual("initiator",initial.RootElement.GetProperty("collaboration").GetProperty("role").GetString());
        Rejected(Send("criteria_ready"),"acceptance_criteria");
        Assert.AreEqual("criteria",task.Collaboration!.Phase);
        var receipt=new ManualReceipt(task.TaskId,task.AttemptId,"agent-a",task.ManualTurn,"done",Outcome:"criteria_ready",AcceptanceCriteria:"Tests pass; documentation is complete.");
        File.WriteAllText(service.ManualReceiptPath(task),RelayJson.Write(receipt));
        Accepted(service.ImportManualReceipts());task=service.Status(task.TaskId).Task;
        Assert.AreEqual("implement",task.Collaboration!.Phase);Assert.AreEqual("agent-b",task.AwaitingAgentId);
        Assert.AreEqual("Tests pass; documentation is complete.",task.Collaboration.Criteria);
        for(var round=1;round<=6;round++)
        {
            Cycle(round);
            if(round==1)
            {
                service.FileHandoffPrompt(task);
                using var review=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!,"context.json")));
                Assert.AreEqual("Tests pass; documentation is complete.",review.RootElement.GetProperty("collaboration").GetProperty("acceptance_criteria").GetString());
            }
            if(round<6)Accepted(Send("changes_requested",["Issue "+round]));
        }
        Rejected(Send("approved",["Still unresolved"]),"changes_requested");
        Assert.AreEqual(TaskState.AwaitingReply,task.State);
        Accepted(Send("approved"));Assert.AreEqual(TaskState.Succeeded,task.State);
        Assert.AreEqual("agent-a",task.Collaboration!.History.Last().AgentId);
    }

    [TestMethod]
    public void RecipientAsFinalReviewerCanApproveAnExistingSubmission()
    {
        task=service.ConfigureFileHandoff(task.TaskId,new(new CollaborationOptions("agent-a","Work is complete",0,ReviewerFinal:true))).Task;
        Assert.AreEqual("review",task.Collaboration!.Phase);Assert.AreEqual("agent-b",task.AwaitingAgentId);
        Accepted(Send("approved"));Assert.AreEqual(TaskState.Succeeded,task.State);
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(5)]
    public void ReviewLoopRunsAsManyRoundsAsNeededAndEndsOnlyAfterAcceptance(int rounds)
    {
        Loop();
        for (var round = 1; round < rounds; round++)
        {
            Cycle(round);
            Accepted(Send("changes_requested", ["第 " + round + " 轮的问题"]));
            Assert.AreEqual("revise", task.Collaboration!.Phase); Assert.AreEqual("agent-b", task.AwaitingAgentId);
            Assert.AreEqual(TaskState.AwaitingReply, task.State);
        }
        Cycle(rounds);
        Accepted(Send("approved"));
        Assert.AreEqual("accept", task.Collaboration!.Phase); Assert.AreEqual("agent-b", task.AwaitingAgentId);
        Assert.AreEqual(TaskState.AwaitingReply, task.State);
        Assert.AreEqual(task.Collaboration.SubmissionPath, task.Collaboration.ApprovedSubmissionPath);
        Accepted(Send("accepted", deliverable: "src/login 已合并"));
        Assert.AreEqual(TaskState.Succeeded, task.State);
        var progress = service.FileHandoffProgress(task);
        Assert.AreEqual("delivered", progress.Completion); Assert.IsTrue(progress.Finished);
        Assert.HasCount(rounds * 2 + 1, task.Collaboration.History);
        Assert.AreEqual("collaboration.delivered", service.Audit(task.TaskId).Single(e => e.Event == "collaboration.delivered").Event);
    }

    [TestMethod]
    public void AuthorChangingTheApprovedVersionSendsItBackToReview()
    {
        Loop();
        Cycle(1);
        Accepted(Send("approved"));
        Accepted(Send("submitted", content: "通过后又修改了一处"));
        Assert.AreEqual("review", task.Collaboration!.Phase); Assert.AreEqual(2, task.Collaboration.ReviewRound);
        Assert.IsNull(task.Collaboration.ApprovedSubmissionPath);
        Accepted(Send("approved"));
        Accepted(Send("accepted", deliverable: "最终版本已提交"));
        Assert.AreEqual(TaskState.Succeeded, task.State);
    }

    [TestMethod]
    public void EachPhaseAcceptsOnlyItsOwnDecisions()
    {
        Loop();
        Rejected(Send("approved"), "submitted");
        Rejected(Send(null!, content: "x"), "submitted");
        Rejected(Send("submitted"), "result_file");
        Cycle(1);
        Rejected(Send("accepted", deliverable: "x"), "changes_requested / approved");
        Rejected(Send("changes_requested"), "open_items");
        Rejected(Send("approved", ["仍有问题"]), "changes_requested");
        Accepted(Send("approved", needsReply: true));
        Rejected(Send("accepted"), "deliverable");
        Assert.AreEqual(TaskState.AwaitingReply, task.State);
    }

    [TestMethod]
    public void SubmissionWithAStrayNeedsReplyFalseStillGoesToReview()
    {
        Loop();
        Accepted(Send("submitted", content: "版本 1", needsReply: false, deliverable: "E:/project/out.md"));
        Assert.AreEqual("review", task.Collaboration!.Phase); Assert.AreEqual("agent-a", task.AwaitingAgentId);
    }

    [TestMethod]
    public void SubmissionWithRemainingItemsAdvancesToReviewAndPreservesTheItems()
    {
        Loop();
        Accepted(Send("submitted", ["Inspect the next handoff", "Confirm the final revision"], content: "First deliverable", needsReply: true));
        Assert.AreEqual(TaskState.AwaitingReply, task.State);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual(1, task.ManualTurn);
        Assert.AreEqual("review", task.Collaboration!.Phase);
        CollectionAssert.AreEqual(new[] { "Inspect the next handoff", "Confirm the final revision" }, task.Collaboration.OpenItems);
        CollectionAssert.AreEqual(task.Collaboration.OpenItems, task.Collaboration.History.Single().OpenItems!);
        service.FileHandoffPrompt(task);
        using var context = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!, "context.json")));
        Assert.AreEqual("Inspect the next handoff", context.RootElement.GetProperty("collaboration").GetProperty("open_items")[0].GetString());
        Accepted(Send("approved"));
        Rejected(Send("accepted", ["Still pending"], deliverable: "docs/result.md"), "未解决");
        Assert.AreEqual(TaskState.AwaitingReply, task.State);
    }

    [TestMethod]
    public void ProgressReceiptsAndGreetingsDoNotAdvanceTheLoop()
    {
        Loop();
        Accepted(Send(null!, status: "working"));
        Assert.AreEqual(TaskState.Running, task.State); Assert.AreEqual(0, task.ManualTurn);
        Rejected(Send("submitted", status: "working"), "完成当前工作");
        Rejected(Send(null!, status: "done", needsReply: false), "outcome");
        Assert.AreEqual(0, task.ManualTurn);
    }

    [TestMethod]
    public void RoundBudgetPausesBeforeAnotherReviewAndCanBeExtended()
    {
        Loop(rounds: 2);
        Cycle(1); Accepted(Send("changes_requested", ["A"]));
        Cycle(2); Accepted(Send("changes_requested", ["B"]));
        Accepted(Send("submitted", content: "第三版"));
        Assert.AreEqual(TaskState.Paused, task.State); Assert.AreEqual("round_limit", task.PauseReason);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual("user", service.FileHandoffProgress(task).WaitingOn);
        Rejected(Send("approved"), "暂停");
        task = service.ContinueFileHandoff(task.TaskId, "最后复验一次", 1).Task;
        Assert.AreEqual(TaskState.AwaitingReply, task.State); Assert.AreEqual(3, task.Collaboration!.MaxReviewRounds);
        Accepted(Send("approved"));
        Accepted(Send("accepted", deliverable: "已交付"));
        Assert.AreEqual(TaskState.Succeeded, task.State);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FinalAllowedReviewEitherFinishesOrPausesBeforeSendingAnotherRevision(bool approved)
    {
        task = service.ConfigureFileHandoff(task.TaskId,
            new(new CollaborationOptions("agent-b", "Build passes", 1, ReviewerFinal: true))).Task;
        Cycle(1);
        Assert.IsNull(service.FileHandoffProgress(task).NextIfPassed);
        var reviewTurn = task.ManualTurn;
        if (approved)
        {
            Accepted(Send("approved"));
            Assert.AreEqual(TaskState.Succeeded, task.State);
            Assert.AreEqual(reviewTurn, task.ManualTurn);
            Assert.AreEqual(task.Collaboration!.SubmissionPath, task.Collaboration.ApprovedSubmissionPath);
            return;
        }

        Accepted(Send("changes_requested", ["Build is still failing"]));
        Assert.AreEqual(TaskState.Paused, task.State);
        Assert.AreEqual("round_limit", task.PauseReason);
        StringAssert.Contains(task.PauseMessage!, "仍有未解决问题");
        Assert.AreEqual("revise", task.Collaboration!.Phase);
        CollectionAssert.AreEqual(new[] { "Build is still failing" }, task.Collaboration.OpenItems);
        Assert.AreEqual(1, task.Collaboration.ReviewRound);
        Assert.IsFalse(service.CanSendFileHandoff(task));
        Rejected(Send("submitted", content: "Unrequested revision"), "暂停");
        task = service.ContinueFileHandoff(task.TaskId, extraRounds: 1).Task;
        Assert.AreEqual("agent-b", task.AwaitingAgentId);
        Cycle(2);
        Accepted(Send("approved"));
        Assert.AreEqual(TaskState.Succeeded, task.State);
    }

    [TestMethod]
    public void IdenticalResubmissionPausesForTheUser()
    {
        Loop();
        Cycle(1); Accepted(Send("changes_requested", ["标题错误"]));
        Accepted(Send("submitted", content: "版本 1"));
        Assert.AreEqual(TaskState.Paused, task.State); Assert.AreEqual("no_progress", task.PauseReason);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        task = service.ContinueFileHandoff(task.TaskId).Task;
        Assert.AreEqual(TaskState.AwaitingReply, task.State); Assert.AreEqual("agent-a", task.AwaitingAgentId);
    }

    [TestMethod]
    public void RepeatedUnresolvedItemsPauseInsteadOfLoopingForever()
    {
        Loop();
        Cycle(1); Accepted(Send("changes_requested", ["标题错误", "缺少测试"]));
        Cycle(2); Accepted(Send("changes_requested", [" 标题错误", "缺少测试 "]));
        Assert.AreEqual(TaskState.Paused, task.State); Assert.AreEqual("no_progress", task.PauseReason);
        Assert.AreEqual("agent-b", task.AwaitingAgentId); Assert.AreEqual("revise", task.Collaboration!.Phase);
    }

    [TestMethod]
    public void FailedTurnPausesAndARetryGetsANewTurnForTheSameAgent()
    {
        Loop();
        Accepted(Send(null!, status: "failed", needsReply: false));
        Assert.AreEqual(TaskState.Paused, task.State); Assert.AreEqual("agent_failed", task.PauseReason);
        var failedTurn = task.ManualTurn;
        task = service.ContinueFileHandoff(task.TaskId, "依赖已安装，请重试").Task;
        Assert.AreEqual(failedTurn + 1, task.ManualTurn); Assert.AreEqual("agent-b", task.AwaitingAgentId);
        Assert.AreEqual("implement", task.Collaboration!.Phase);
        Cycle(1);
    }

    [TestMethod]
    public void SenderThatAlreadyHoldsTheWorkStartsWithReview()
    {
        Loop(author: "agent-a");
        Assert.AreEqual("review", task.Collaboration!.Phase); Assert.AreEqual(1, task.Collaboration.ReviewRound);
        Assert.AreEqual(task.RequestPath, task.Collaboration.SubmissionPath);
        Accepted(Send("changes_requested", ["补充错误处理"]));
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Accepted(Send("submitted", content: "已补充"));
        Assert.AreEqual("agent-b", task.AwaitingAgentId);
        Accepted(Send("approved"));
        Accepted(Send("accepted", deliverable: "已合并"));
        Assert.AreEqual(TaskState.Succeeded, task.State);
    }

    [TestMethod]
    public void ContextTellsEachParticipantItsRolePhaseAndFreshReplyFile()
    {
        Loop();
        JsonElement Context()
        {
            task = service.Status(task.TaskId).Task;
            service.FileHandoffPrompt(task);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!, "context.json")));
            return document.RootElement.Clone();
        }
        var first = Context();
        Assert.AreEqual("author", first.GetProperty("collaboration").GetProperty("role").GetString());
        Assert.AreEqual("页面可用且无控制台错误", first.GetProperty("collaboration").GetProperty("acceptance_criteria").GetString());
        Cycle(1);
        var review = Context();
        Assert.AreEqual("reviewer", review.GetProperty("collaboration").GetProperty("role").GetString());
        Assert.AreEqual(task.Collaboration!.SubmissionPath, review.GetProperty("collaboration").GetProperty("submission_file").GetString());
        Assert.AreEqual("changes_requested", review.GetProperty("receipt_template").GetProperty("outcome").GetString());
        Assert.AreNotEqual(first.GetProperty("reply_file").GetString(), review.GetProperty("reply_file").GetString());
        Accepted(Send("changes_requested", ["按钮颜色"]));
        var revise = Context().GetProperty("collaboration");
        Assert.AreEqual("revise", revise.GetProperty("phase").GetString());
        Assert.AreEqual("按钮颜色", revise.GetProperty("open_items")[0].GetString());
    }

    [TestMethod]
    public void OptionsCannotChangeOnceTheFirstTurnStarted()
    {
        Assert.ThrowsExactly<RelayException>(() => service.ConfigureFileHandoff(task.TaskId, new(new CollaborationOptions("other"))));
        Assert.ThrowsExactly<RelayException>(() => service.ConfigureFileHandoff(task.TaskId, new(new CollaborationOptions("agent-b", null, -1))));
        Loop();
        Cycle(1);
        Assert.ThrowsExactly<RelayException>(() => Loop());
    }

    [TestMethod]
    public void ReconfiguringAnUnstartedLoopAsSingleHandoffRemovesItsReviewStages()
    {
        Loop();
        task = service.ConfigureFileHandoff(task.TaskId, new FileHandoffOptions()).Task;
        Assert.IsNull(task.Collaboration);
        Accepted(Send(null!, needsReply: false));
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.AreEqual("completed", service.FileHandoffProgress(task).Completion);
    }

    [TestMethod]
    public void ReformattedWorkingReceiptWithOpenItemsArrayDoesNotRepeatItsEvents()
    {
        Loop(author: "agent-a");
        Accepted(Send(null!, items: [], status: "working"));
        var events = service.Audit(task.TaskId).Count;
        var notifications = service.Inbox().Count;
        File.AppendAllText(service.ManualReceiptPath(task), "\n");
        var repeated = service.ImportManualReceipts();
        Assert.IsEmpty(repeated.UpdatedTaskIds);
        Assert.IsEmpty(repeated.Errors);
        Assert.HasCount(events, service.Audit(task.TaskId));
        Assert.HasCount(notifications, service.Inbox());
        Assert.AreEqual(TaskState.Running, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void SingleHandoffIgnoresReviewLoopFieldsAnAgentAddsToItsReceipt()
    {
        Accepted(Send("completed", ["none"], "已完成", "E:/project/out.md", needsReply: false));
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.IsNull(task.Collaboration);
    }
}
