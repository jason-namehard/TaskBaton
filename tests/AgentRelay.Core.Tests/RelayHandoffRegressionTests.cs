using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

/// <summary>
/// Regression tests for the 2026-11-07 handoff round: exact conversations, per-turn status records, late results,
/// image scope and the lightweight fixed-count relay. Every fixture is fictional and lives in its own temp site.
/// </summary>
[TestClass]
public sealed class RelayHandoffRegressionTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    private string root = null!;
    private RelayService service = null!;
    private string work = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "fixture", "甲");
        service.RegisterManual("agent-b", "fixture", "乙");
        work = Path.Combine(root, "work.md"); File.WriteAllText(work, "Fix the build and report what changed.");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private TaskRecord Manual() => service.CreateFileHandoff(new("agent-a", "agent-b", work, "按文件要求处理")).Task;
    private TaskRecord Current(string id) => service.Status(id).Task;
    private TaskRecord Reload(TaskRecord task) => Current(task.TaskId);

    private string ResultFile(string name)
    {
        var path = Path.Combine(root, name); File.WriteAllText(path, "result " + name); return path;
    }

    private int receiptSequence;

    private void WriteReceipt(TaskRecord task, string sender, string status, bool? needsReply = null,
        string? resultFile = null, string? nextAction = null, string fileName = "receipt.json", int? turn = null)
    {
        var receipt = new ManualReceipt(task.TaskId, task.AttemptId, sender, turn ?? task.ManualTurn, status,
            needsReply, resultFile, nextAction);
        var path = Path.Combine(service.TurnControlDirectory(task), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RelayJson.Write(receipt));
        // Status files are applied in the order they were written; keep that order explicit in the fixture.
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc).AddSeconds(++receiptSequence));
    }

    private ManualReceiptImportResult Import() => service.ImportManualReceipts();

    // ---------- A. exact conversations and the fixed-count relay ----------

    [TestMethod]
    public void RelayStepsSeparateHandoffCountFromTestAndModifyWork()
    {
        var task = Manual();
        service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions(
        [
            new("agent-b", RelayStepRole.Work, "修好构建"),
            new("agent-a", RelayStepRole.Test, "跑回归并写报告"),
            new("agent-b", RelayStepRole.Modify, "按测试报告修改")
        ]));
        task = Reload(task);
        var progress = service.FileHandoffProgress(task);
        // Three steps are two handoffs; "one test-and-modify round" is not one handoff.
        Assert.AreEqual(2, progress.Status!.HandoffsTotal);
        Assert.AreEqual(0, progress.Status.HandoffsDone);
        Assert.AreEqual(2, RelayService.HandoffCount(3));
        Assert.AreEqual(1, RelayService.HandoffCount(2));
        Assert.AreEqual("修好构建", task.ManualNextAction);

        var context = Context(task);
        Assert.AreEqual(1, context.GetProperty("relay_plan").GetProperty("current_step").GetInt32());
        Assert.IsFalse(context.GetProperty("relay_plan").GetProperty("last_step").GetBoolean());
        Assert.AreEqual(2, context.GetProperty("relay_plan").GetProperty("handoff_count").GetInt32());
        StringAssert.Contains(context.GetProperty("instructions").GetString()!, "修好构建");

        WriteReceipt(task, "agent-b", "done", true, ResultFile("step1.md"), "看构建日志");
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual("agent-a", task.AwaitingAgentId); Assert.AreEqual(1, task.ManualTurn);
        StringAssert.Contains(task.ManualNextAction!, "跑回归并写报告");

        WriteReceipt(task, "agent-a", "done", true, ResultFile("report.md"), "按报告修改");
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual("agent-b", task.AwaitingAgentId); Assert.AreEqual(2, task.ManualTurn);

        WriteReceipt(task, "agent-b", "done", false, ResultFile("final.md"));
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.IsNull(task.Collaboration, "The last step must not become an acceptance role.");
        // A modify step that ends the plan after a test step was never re-tested.
        Assert.AreEqual("modified_not_retested", task.CompletionNote);
        StringAssert.Contains(service.Status(task.TaskId).Prompt, "修改后未复测");
        Assert.AreEqual(2, service.FileHandoffProgress(task).Status!.HandoffsDone);
    }

    [TestMethod]
    public void RelayPlansEndExactlyAtTheConfiguredStep()
    {
        foreach (var (agents, handoffs) in new[]
                 {
                     (new[] { "agent-b", "agent-a" }, 1),
                     (new[] { "agent-b", "agent-a", "agent-b" }, 2),
                     (new[] { "agent-b", "agent-a", "agent-b", "agent-a", "agent-b" }, 4)
                 })
        {
            var task = Manual();
            service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions(
                agents.Select(id => new RelayStep(id, RelayStepRole.Work)).ToArray()));
            task = Reload(task);
            Assert.AreEqual(handoffs, service.FileHandoffProgress(task).Status!.HandoffsTotal);
            for (var index = 0; index < agents.Length; index++)
            {
                task = Reload(task);
                Assert.AreEqual(agents[index], task.AwaitingAgentId);
                var last = index == agents.Length - 1;
                WriteReceipt(task, agents[index], "done", !last, last ? null : ResultFile("step" + index + ".md"),
                    last ? null : "下一步");
                var result = Import();
                if (last) Assert.IsEmpty(result.Errors);
                else
                {
                    Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Message)));
                    Assert.AreEqual(index + 1, Reload(task).ManualTurn);
                }
            }
            task = Reload(task);
            Assert.AreEqual(TaskState.Succeeded, task.State);
            Assert.AreEqual(agents.Length - 1, service.FileHandoffProgress(task).Status!.HandoffsDone);
        }
    }

    [TestMethod]
    public void RelayCanStartWithTheInitiatingAgentsOwnWork()
    {
        var task = Manual();
        service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions(
        [
            new("agent-a", RelayStepRole.Work, "先按原始要求实现"),
            new("agent-b", RelayStepRole.Test, "跑回归"),
            new("agent-a", RelayStepRole.Modify, "按报告修改")
        ]));
        task = Reload(task);
        Assert.AreEqual("agent-a", task.AwaitingAgentId, "the initiating Agent's own work is step 1, not a criteria draft");
        var context = Context(task);
        Assert.AreEqual("user", context.GetProperty("sender_name").GetString());
        Assert.AreEqual("先按原始要求实现", context.GetProperty("request").GetString());
        Assert.AreEqual(2, service.FileHandoffProgress(task).Status!.HandoffsTotal);

        WriteReceipt(task, "agent-a", "done", true, ResultFile("a-work.md"), "请测试");
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual("agent-b", task.AwaitingAgentId);
        WriteReceipt(task, "agent-b", "done", true, ResultFile("b-report.md"), "请按报告修改");
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        WriteReceipt(task, "agent-a", "done", false, ResultFile("a-fix.md"));
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.AreEqual("modified_not_retested", task.CompletionNote);
    }

    [TestMethod]
    public void TheLastRelayStepCannotOpenAnotherTurn()
    {
        var task = Manual();
        service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions([new("agent-b", RelayStepRole.Work, "实现")]));
        task = Reload(task);
        WriteReceipt(task, "agent-b", "done", true, ResultFile("more.md"), "还要再改一版");
        var result = Import();
        Assert.HasCount(1, result.Errors);
        StringAssert.Contains(result.Errors[0].Message, "最后一步");
        Assert.AreEqual(TaskState.AwaitingManual, Reload(task).State);
    }

    [TestMethod]
    public void PlanIsRejectedBeforeAnyWorkAndForRepeatedAgents()
    {
        var task = Manual();
        Assert.AreEqual("relay_step_repeat", Assert.ThrowsExactly<RelayException>(() =>
            service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions(
                [new("agent-b", RelayStepRole.Work), new("agent-b", RelayStepRole.Test)]))).Code);
        Assert.AreEqual("relay_step_agent", Assert.ThrowsExactly<RelayException>(() =>
            service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions([new("agent-x", RelayStepRole.Work)]))).Code);
        service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions([new("agent-b", RelayStepRole.Work)]));
        task = Reload(task);
        WriteReceipt(task, "agent-b", "done", false, ResultFile("done.md"));
        Import();
        // Once work started, the plan cannot be replaced.
        Assert.AreEqual("handoff_options_started", Assert.ThrowsExactly<RelayException>(() =>
            service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions([new("agent-b", RelayStepRole.Work)]))).Code);
    }

    [TestMethod]
    public void SingleHandoffAndLegacyPlanKeepTheirMeaning()
    {
        var task = Manual();
        WriteReceipt(task, "agent-b", "done", false, ResultFile("single.md"));
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.IsNull(task.CompletionNote, "A plain handoff never shows a retest conclusion.");
        Assert.IsFalse(service.Status(task.TaskId).Prompt.Contains("复测", StringComparison.Ordinal));
    }

    // ---------- B. session identity, timeouts and late results ----------

    [TestMethod]
    public async Task StreamingHostSessionIdIsSavedBeforeTheTurnEnds()
    {
        var script = Path.Combine(root, "dsh-jsonl.ps1");
        File.WriteAllText(script, """
            param([string]$Prompt)
            [Console]::Out.WriteLine('{"type":"session","sessionId":"created-agent-b"}')
            [Console]::Out.Flush()
            exit 1
            """);
        var command = new CliCommand("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "{prompt}"], null, root);
        Enable("agent-b", command, "dsh-jsonl");
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "工作", null, null, true, root, root)).Task;

        await service.SendFileHandoffAsync(task.TaskId, service.FileHandoffPrompt(task));

        task = Reload(task);
        Assert.AreEqual("unknown", task.CliFileStatus);
        var binding = service.TaskSession(task.TaskId, "agent-b");
        Assert.IsNotNull(binding);
        Assert.AreEqual("created-agent-b", binding!.SessionId);
        Assert.AreEqual(0, binding.Turn);
        StringAssert.Contains(binding.Evidence!, "before the turn finished");
        // The unconfirmed turn must not be sent again automatically.
        Assert.IsFalse(service.CanSendFileHandoff(task));
        // A restart keeps the exact conversation.
        var restarted = new RelayService(service.Home);
        Assert.AreEqual("created-agent-b", restarted.TaskSession(task.TaskId, "agent-b")!.SessionId);
    }

    [TestMethod]
    public async Task AcpReportsTheNewSessionIdForTheTaskBinding()
    {
        var script = Path.Combine(root, "acp-early.ps1");
        File.WriteAllText(script, """
            param([string]$Log)
            $source=[Console]::In; $sink=[Console]::Out
            function Send($value){$sink.WriteLine(($value|ConvertTo-Json -Compress -Depth 20));$sink.Flush()}
            while($null -ne ($line=$source.ReadLine())){
              $frame=$line|ConvertFrom-Json
              if($frame.method -eq 'initialize'){Send @{jsonrpc='2.0';id=$frame.id;result=@{protocolVersion=1;agentCapabilities=@{loadSession=$true}}}}
              elseif($frame.method -eq 'session/new'){
                [IO.File]::AppendAllText($Log,"new`n")
                Send @{jsonrpc='2.0';id=$frame.id;result=@{sessionId='acp-session-1'}}
              }
              elseif($frame.method -eq 'session/prompt'){
                [IO.File]::AppendAllText($Log,"prompt`n")
                Send @{jsonrpc='2.0';method='session/update';params=@{sessionId=$frame.params.sessionId;update=@{sessionUpdate='agent_message_chunk';content=@{type='text';text='ok'}}}}
                Send @{jsonrpc='2.0';id=$frame.id;result=@{stopReason='end_turn'}}
                exit 0
              }
            }
            """);
        var log = Path.Combine(root, "acp-order.log");
        var command = new CliCommand("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, log], null, root);
        Enable("agent-b", command, "acp");
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "工作", null, null, true, root, root)).Task;

        await service.SendFileHandoffAsync(task.TaskId, service.FileHandoffPrompt(task));

        // The id comes from session/new, before the prompt is even submitted.
        Assert.AreEqual("acp-session-1", service.TaskSession(task.TaskId, "agent-b")!.SessionId);
        CollectionAssert.AreEqual(new[] { "new", "prompt" }, File.ReadAllLines(log));
        Assert.AreEqual("submitted", Reload(task).CliFileStatus);
    }

    [TestMethod]
    public async Task WaitingForTheHostDoesNotConsumeTheTurnBudget()
    {
        var script = Path.Combine(root, "slow-start.ps1");
        File.WriteAllText(script, "Start-Sleep -Milliseconds 1500\nexit 1\n");
        var command = new CliCommand("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script, "{prompt}"], null, root);
        Enable("agent-b", command, "text");
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "工作", null, null, true, root, root,
            TurnTimeoutMinutes: 30)).Task;
        var before = task.DeadlineAt;

        await service.SendFileHandoffAsync(task.TaskId, service.FileHandoffPrompt(task));

        task = Reload(task);
        Assert.AreEqual("unknown", task.CliFileStatus);
        Assert.IsNotNull(task.CliFileError);
        // Nothing reached the Agent, so the turn keeps its work time and the wait is recorded separately.
        Assert.IsTrue(task.DeadlineAt > before, "the deadline must move past the host wait");
        Assert.IsGreaterThanOrEqualTo(1, task.HostWaitSeconds);
        Assert.IsTrue(service.Audit(task.TaskId).Any(entry => entry.Event == "file.host_wait"));
    }

    [TestMethod]
    public void DeliveryFailedPauseAcceptsTheLateRealResult()
    {
        var task = Manual();
        new RelayStore(service.Home).Write(s =>
        {
            var stored = s.Task(task.TaskId);
            stored.State = TaskState.Paused; stored.PausedFrom = TaskState.AwaitingManual;
            stored.PauseReason = "delivery_failed"; stored.PauseMessage = "桥接未确认投递";
            s.Save(stored); return true;
        });
        task = Reload(task);
        WriteReceipt(task, "agent-b", "done", false, ResultFile("late.md"));
        var result = Import();
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Message)));
        task = Reload(task);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.IsNull(task.PauseReason);
        Assert.IsTrue(service.Audit(task.TaskId).Any(entry => entry.Event == "handoff.late_receipt"));
    }

    [TestMethod]
    public void OldAttemptsAndFinishedTasksKeepLateResultsAsEvidenceOnly()
    {
        var task = Manual();
        WriteReceipt(task, "agent-b", "done", false, ResultFile("done.md"));
        Import();
        task = Reload(task);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        WriteReceipt(task, "agent-b", "failed", false, null, "迟到的失败", fileName: "receipt-late.json");
        var result = Import();
        Assert.IsEmpty(result.UpdatedTaskIds);
        task = Reload(task);
        Assert.AreEqual(TaskState.Succeeded, task.State, "a finished task must not be reopened by a late receipt");
        Assert.HasCount(2, task.TurnStatuses);
        Assert.IsFalse(task.TurnStatuses[^1].Accepted);

        // Unchanged evidence can be skipped even while another process has the file open.
        var latePath = Path.Combine(service.TurnControlDirectory(task), "receipt-late.json");
        using (File.Open(latePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.IsEmpty(Import().Errors);
        Assert.HasCount(2, Reload(task).TurnStatuses);

        WriteReceipt(task, "agent-b", "failed", false, null, "迟到文件补充说明", fileName: "receipt-late.json");
        Assert.IsEmpty(Import().Errors);
        Assert.HasCount(3, Reload(task).TurnStatuses);
        var restarted = new RelayService(service.Home);
        Assert.IsEmpty(restarted.ImportManualReceipts().Errors);
        Assert.HasCount(3, restarted.Status(task.TaskId).Task.TurnStatuses);
    }

    [TestMethod]
    public void ReceiptsOfOldTurnsCannotChangeTheNewTurn()
    {
        var task = Manual();
        WriteReceipt(task, "agent-b", "done", true, ResultFile("first.md"), "继续");
        Import();
        task = Reload(task);
        Assert.AreEqual(1, task.ManualTurn);
        // A file for the previous turn cannot move the task, even if it is rewritten now.
        WriteReceipt(task, "agent-b", "failed", false, null, "旧轮失败", fileName: "receipt.json", turn: 0);
        var result = Import();
        Assert.IsEmpty(result.UpdatedTaskIds);
        Assert.AreEqual(TaskState.AwaitingReply, Reload(task).State);
    }

    // ---------- C. status records and receipts ----------

    [TestMethod]
    public void QuickStatusUpdatesAreAllKeptAndDuplicatesDoNotAdvance()
    {
        var task = Manual();
        WriteReceipt(task, "agent-b", "received", fileName: "receipt.json");
        WriteReceipt(task, "agent-b", "working", fileName: "receipt-2.json");
        WriteReceipt(task, "agent-b", "done", false, ResultFile("final.md"), fileName: "receipt-3.json");
        var result = Import();
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(error => error.Message)));
        task = Reload(task);
        CollectionAssert.AreEqual(new[] { "received", "working", "done" },
            task.TurnStatuses.Where(entry => entry.Accepted).Select(entry => entry.Status).ToArray());
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.AreEqual("submitted", service.FileHandoffProgress(task).Status!.Agent);

        var again = Import();
        Assert.IsEmpty(again.UpdatedTaskIds);
        Assert.HasCount(3, Reload(task).TurnStatuses);

        var restarted = new RelayService(service.Home);
        Assert.HasCount(3, restarted.Status(task.TaskId).Task.TurnStatuses);
    }

    [TestMethod]
    public void ReceivedAfterWorkingIsRecordedButNeverRewinds()
    {
        var task = Manual();
        WriteReceipt(task, "agent-b", "working", fileName: "receipt.json");
        Import();
        WriteReceipt(task, "agent-b", "received", fileName: "receipt-2.json");
        var result = Import();
        Assert.HasCount(1, result.Errors);
        StringAssert.Contains(result.Errors[0].Message, "退回 received");
        task = Reload(task);
        Assert.AreEqual(TaskState.Running, task.State);
        Assert.HasCount(2, task.TurnStatuses);
        Assert.IsFalse(task.TurnStatuses.Single(entry => entry.Status == "received").Accepted);
        Assert.AreEqual("working", service.FileHandoffProgress(task).Status!.Agent);

        for (var i = 0; i < 5; i++)
        {
            Assert.IsEmpty(Import().Errors);
            task = Reload(task);
            Assert.HasCount(2, task.TurnStatuses);
            Assert.AreEqual(TaskState.Running, task.State);
            Assert.AreEqual("working", service.FileHandoffProgress(task).Status!.Agent);
        }
    }

    [TestMethod]
    public void WrongFieldTypeNamesTheFieldAndKeepsTheFile()
    {
        var task = Manual();
        var path = Path.Combine(service.TurnControlDirectory(task), "receipt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            {"task_id":"{{task.TaskId}}","attempt_id":"{{task.AttemptId}}","sender":"agent-b","turn":0,
             "status":"done","needs_reply":false,"deliverable":["D:\\out\\a.md"]}
            """);
        var result = Import();
        Assert.HasCount(1, result.Errors);
        StringAssert.Contains(result.Errors[0].Message, "deliverable");
        StringAssert.Contains(result.Errors[0].Message, "数组");
        Assert.IsTrue(File.Exists(path), "the rejected receipt stays as evidence");
        Assert.AreEqual(TaskState.AwaitingManual, Reload(task).State);
    }

    [TestMethod]
    public void UnsupportedFieldIsNamedInsteadOfAGenericFormatError()
    {
        var task = Manual();
        var path = Path.Combine(service.TurnControlDirectory(task), "receipt.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            {"task_id":"{{task.TaskId}}","attempt_id":"{{task.AttemptId}}","sender":"agent-b","turn":0,
             "status":"done","needs_reply":false,"notes":"补充说明"}
            """);
        var result = Import();
        Assert.HasCount(1, result.Errors);
        StringAssert.Contains(result.Errors[0].Message, "notes");
        StringAssert.Contains(result.Errors[0].Message, "next_action");
        Assert.AreEqual(TaskState.AwaitingManual, Reload(task).State);
    }

    [TestMethod]
    public void MissingReceiptFieldsAreNamed()
    {
        var error = Assert.ThrowsExactly<RelayException>(() => ManualTransfers.ParseReceipt("""{"task_id":"example"}"""));
        var detail = error.Message.Split(Environment.NewLine)[1];
        StringAssert.Contains(detail, "缺少必填字段");
        foreach (var name in new[] { "attempt_id", "sender", "turn", "status" })
            StringAssert.Contains(detail, name);
    }

    [TestMethod]
    public void MissingPauseStateOnlyRejectsTheAffectedReceipt()
    {
        var broken = Manual();
        var healthy = service.CreateFileHandoff(new("agent-a", "agent-b", ResultFile("separate-work.md"))).Task;
        new RelayStore(service.Home).Write(s =>
        {
            var task = s.Task(broken.TaskId);
            task.State = TaskState.Paused;
            task.PauseReason = "turn_timeout";
            s.Save(task);
            return true;
        });
        WriteReceipt(broken, "agent-b", "done", false);
        WriteReceipt(healthy, "agent-b", "done", false);

        var result = Import();
        Assert.AreEqual(broken.TaskId, result.Errors.Single().TaskId);
        StringAssert.Contains(result.Errors.Single().Message, "暂停前的状态");
        Assert.AreEqual(TaskState.Paused, Reload(broken).State);
        Assert.AreEqual(TaskState.Succeeded, Reload(healthy).State);
    }

    [TestMethod]
    public void LightweightStatusRegistrationFillsTheIdentityFields()
    {
        var task = Manual();
        service.RegisterTurnStatus(task.TaskId, "received");
        Assert.AreEqual(TaskState.Acknowledged, Reload(task).State);
        service.RegisterTurnStatus(task.TaskId, "working");
        task = Reload(task);
        Assert.AreEqual(TaskState.Running, task.State);
        Assert.AreEqual("working", service.FileHandoffProgress(task).Status!.Agent);
        Assert.AreEqual("manual_receipt_order", Assert.ThrowsExactly<RelayException>(() =>
            service.RegisterTurnStatus(task.TaskId, "received")).Code);
        Assert.AreEqual("manual_receipt_status", Assert.ThrowsExactly<RelayException>(() =>
            service.RegisterTurnStatus(task.TaskId, "done")).Code);
    }

    [TestMethod]
    public void LightweightStatusLeavesTheAgentsReceiptIntact()
    {
        var task = Manual();
        WriteReceipt(task, "agent-b", "working", nextAction: "Agent's progress");
        var path = service.ManualReceiptPath(task);
        var original = File.ReadAllText(path);
        Import();

        Assert.AreEqual("manual_receipt_order", Assert.ThrowsExactly<RelayException>(() =>
            service.RegisterTurnStatus(task.TaskId, "received")).Code);
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.HasCount(1, Reload(task).TurnStatuses);

        service.RegisterTurnStatus(task.TaskId, "working", "Additional progress");
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.HasCount(2, Reload(task).TurnStatuses);
        Assert.IsEmpty(Import().Errors);
        Assert.HasCount(2, Reload(task).TurnStatuses);
    }

    [TestMethod]
    public void SubmittedReceiptDoesNotStartTheNextStepWhileTheHostStillRuns()
    {
        var task = Manual();
        new RelayStore(service.Home).Write(s =>
        {
            var stored = s.Task(task.TaskId);
            stored.CliFileTurn = 0; stored.CliFileStatus = "submitting"; stored.CliFileOperation = "create";
            s.Save(stored); return true;
        });
        task = Reload(task);
        WriteReceipt(task, "agent-b", "done", true, ResultFile("next.md"), "继续");
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual(1, task.ManualTurn);
        var progress = service.FileHandoffProgress(task);
        Assert.AreEqual("running", progress.Status!.Host);
        Assert.IsFalse(service.CanSendFileHandoff(task), "the next turn waits for the host to finish");
        StringAssert.Contains(progress.DeliveryMessage!, "执行结束未知");

        // Once the host call returns, the next turn may start and the status reflects the new turn.
        new RelayStore(service.Home).Write(s =>
        {
            var stored = s.Task(task.TaskId);
            stored.CliFileStatus = "submitted"; stored.HostTurnEnded = true; s.Save(stored); return true;
        });
        var released = Reload(task);
        // The new turn has no delivery or status of its own yet, so it may now be sent.
        Assert.AreEqual("not_started", service.FileHandoffProgress(released).Status!.Host);
        Assert.AreEqual("none", service.FileHandoffProgress(released).Status!.Agent);
        Assert.IsTrue(released.TurnStatuses.Any(entry => entry.Turn == 0 && entry.Status == "done" && entry.Accepted));
    }

    [TestMethod]
    public void UnknownHostEndIsReportedAsUnknownNotSuccess()
    {
        var task = Manual();
        new RelayStore(service.Home).Write(s =>
        {
            var stored = s.Task(task.TaskId);
            stored.CliFileTurn = 0; stored.CliFileStatus = "unknown"; stored.HostTurnEnded = null;
            s.Save(stored); return true;
        });
        task = Reload(task);
        var status = service.FileHandoffProgress(task).Status!;
        Assert.AreEqual("sent_unknown", status.Send);
        Assert.AreEqual("unknown", status.Host);
        Assert.AreEqual("none", status.Agent);
        Assert.IsTrue(status.NeedsUser);
    }

    // ---------- D. images ----------

    [TestMethod]
    public void ImagesOnlyTravelWithTheFirstTurnUnlessTheUserSelectsALaterTurn()
    {
        var first = Image("一.png"); var second = Image("二.png");
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "看图处理", null, null, true, root, root,
            Images: [first, second])).Task;
        var context = Context(task);
        Assert.HasCount(2, context.GetProperty("images").EnumerateArray());
        StringAssert.Contains(context.GetProperty("instructions").GetString()!, "Inspect every attached image");

        // A relay plan advances without CLI delivery, so the later turn is observable immediately.
        service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions(
            [new("agent-b", RelayStepRole.Work, "处理图片"), new("agent-a", RelayStepRole.Work, "复核")]));
        task = Reload(task);
        WriteReceipt(task, "agent-b", "done", true, ResultFile("one.md"), "复核");
        Import();
        task = Reload(task);
        context = Context(task);
        Assert.IsEmpty(context.GetProperty("images").EnumerateArray());
        Assert.DoesNotContain("Inspect every attached image", context.GetProperty("instructions").GetString()!, StringComparison.Ordinal);
        StringAssert.Contains(context.GetProperty("instructions").GetString()!, "No images are attached to this turn");

        // The user may attach only the selected image to one specific later turn.
        service.AllowTurnImages(task.TaskId, task.ManualTurn, [task.Images![1].ImageId]);
        task = Reload(task);
        context = Context(task);
        var images = context.GetProperty("images").EnumerateArray().ToArray();
        Assert.HasCount(1, images);
        Assert.AreEqual(task.Images![1].ImageId, images[0].GetProperty("image_id").GetString());
        StringAssert.Contains(context.GetProperty("instructions").GetString()!, "Inspect every attached image");
    }

    [TestMethod]
    public void ImageSelectionIsLimitedToLaterTurnsAndExistingImages()
    {
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "看图", null, null, true, root, root,
            Images: [Image("一.png")])).Task;
        Assert.AreEqual("image_turn_invalid", Assert.ThrowsExactly<RelayException>(() =>
            service.AllowTurnImages(task.TaskId, 0)).Code);
        Assert.AreEqual("task_image_missing", Assert.ThrowsExactly<RelayException>(() =>
            service.AllowTurnImages(task.TaskId, 2, ["not-an-image"])).Code);
        service.ConfigureRelayPlan(task.TaskId, new RelayPlanOptions(
            [new("agent-b", RelayStepRole.Work, "处理图片"), new("agent-a", RelayStepRole.Work, "复核")]));
        Assert.AreEqual("image_turn_invalid", Assert.ThrowsExactly<RelayException>(() =>
            service.AllowTurnImages(task.TaskId, 2)).Code);
        Assert.IsNull(Reload(task).ImageTurn);
    }

    // ---------- E. independent acceptance ----------

    [TestMethod]
    public void ReviewerThatAlsoWorkedUsesANewConversationForTheReview()
    {
        var task = Manual();
        // agent-b authors the work, so the sender agent-a reviews; agent-a also writes the criteria first.
        service.BindManualTaskSession(task.TaskId, "agent-a", "origin-session");
        service.ConfigureFileHandoff(task.TaskId, new FileHandoffOptions(
            new CollaborationOptions("agent-b", null, 5, GenerateCriteria: true, ReviewerFinal: true, IndependentAcceptance: true)));
        task = Reload(task);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        WriteCriteria(task);
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual("agent-b", task.AwaitingAgentId);

        WriteSubmission(task);
        Assert.IsEmpty(Import().Errors);
        task = Reload(task);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual("review", task.Collaboration!.Phase);
        // The reviewer had a conversation of its own from the criteria step; the review must not reuse it.
        var binding = service.TaskSession(task.TaskId, "agent-a");
        Assert.IsTrue(binding is null || binding.SessionId is null, "review must start a fresh conversation");
        Assert.IsTrue(service.Audit(task.TaskId).Any(entry => entry.Event == "collaboration.independent_session"));
        StringAssert.Contains(Context(task).GetProperty("instructions").GetString()!, "does not isolate");
    }

    private void WriteCriteria(TaskRecord task)
    {
        var receipt = new ManualReceipt(task.TaskId, task.AttemptId, "agent-a", task.ManualTurn, "done",
            Outcome: "criteria_ready", AcceptanceCriteria: "构建通过");
        var path = service.ManualReceiptPath(task);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RelayJson.Write(receipt));
    }

    private void WriteSubmission(TaskRecord task)
    {
        var result = ResultFile("submission.md");
        var receipt = new ManualReceipt(task.TaskId, task.AttemptId, "agent-b", task.ManualTurn, "done",
            ResultFile: result, Outcome: "submitted");
        var path = service.ManualReceiptPath(task);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RelayJson.Write(receipt));
    }

    // ---------- helpers ----------

    private JsonElement Context(TaskRecord task)
    {
        service.FileHandoffPrompt(task);
        return JsonDocument.Parse(File.ReadAllText(service.ManualContextPath(task))).RootElement.Clone();
    }

    private ImageInput Image(string fileName) => new(fileName, "image/png", Png);

    private void Enable(string agent, CliCommand command, string format)
    {
        var recipe = new CliRecipe(1, "fixture " + format, "fixture", command, format is "acp" or "dsh-desktop" ? command : null,
            [], "fixture", "tests", "MIT", "", format);
        service.ImportCliRecipe(agent, RelayJson.Write(recipe));
        var extension = service.CliExtension(agent)!;
        new RelayStore(service.Home).Write(store =>
        {
            store.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id=$id",
                ("$json", RelayJson.Write(extension with { CreateEvidence = new("fixture", 0, "fixture-session", DateTimeOffset.UtcNow) })), ("$id", agent));
            return true;
        });
        service.SetPermission("agent-a", agent, "allow");
    }
}
