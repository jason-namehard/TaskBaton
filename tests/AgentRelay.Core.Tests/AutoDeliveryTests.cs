using System.Text.Json;
using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AutoDeliveryTests
{
    private string root = null!;
    private RelayService service = null!;
    private readonly TestClock clock = new();

    // Acts like a print-mode Agent: it reads the handoff context, writes the turn's receipt, then reports its session.
    private const string Agent = """
        param([string]$Root,[string]$Agent,[string]$Session,[string]$Prompt)
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        [IO.File]::AppendAllText((Join-Path $Root "$Agent.log"),$Session+[Environment]::NewLine)
        $mode=if(Test-Path (Join-Path $Root "$Agent.mode")){(Get-Content (Join-Path $Root "$Agent.mode") -Raw).Trim()}else{'ok'}
        if($mode -eq 'auth'){[Console]::Error.WriteLine('Authentication required. Please use /login command to sign in');exit 0}
        $m=[regex]::Match($Prompt,'RELAY-[A-Z0-9]+').Value
        $f=[regex]::Match($Prompt,'Create the file (.+?) containing').Groups[1].Value
        if($f -and $mode -ne 'nowrite'){[IO.File]::WriteAllText($f,$m)}
        $context=[regex]::Match($Prompt,'Read (.+context\.json)').Groups[1].Value
        # A host that answers from another conversation never sees this task.
        if($context -and $mode -ne 'other'){
          $c=Get-Content $context -Raw|ConvertFrom-Json
          $queue=Join-Path $Root "$Agent.outcomes";$lines=@(Get-Content $queue);$outcome=$lines[0]
          Set-Content $queue @($lines|Select-Object -Skip 1)
          $r=$c.receipt_template;$r.outcome=$outcome
          if($outcome -eq 'submitted'){[IO.File]::WriteAllText($c.reply_file,"work "+[guid]::NewGuid());$r.result_file=$c.reply_file}
          if($outcome -eq 'changes_requested'){$r.open_items=@("issue "+[guid]::NewGuid())}
          if($outcome -eq 'accepted'){$r.deliverable='docs/result.md'}
          [IO.File]::WriteAllText($c.receipt_file,($r|ConvertTo-Json -Depth 5))
        }
        if($mode -eq 'interrupted'){exit 1}
        $id=if($Session -eq 'create'){"created-$Agent"}elseif($mode -eq 'other'){'another-session'}else{$Session}
        @{session_id=$id;reply=$(if($m){$m}else{'accepted'});accepted=$true}|ConvertTo-Json -Compress
        """;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay"), clock); service.Initialize();
        service.RegisterManual("agent-a", "fixture", "甲"); service.RegisterManual("agent-b", "fixture", "乙");
        var script = Path.Combine(root, "agent.ps1"); File.WriteAllText(script, Agent);
        foreach (var id in new[] { "agent-a", "agent-b" })
        {
            CliCommand Command(string session) => new("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
                root, id, session, "{prompt}"], null, root);
            service.ImportCliRecipe(id, RelayJson.Write(new CliRecipe(1, "Local agent fixture", "fixture", Command("create"),
                Command("{session_id}"), [], "fixture", "local tests", "MIT", "No model calls.")));
            Sessions(id, "source-a", "created-" + id);
        }
    }

    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    private string[] Calls(string agent) => File.Exists(Path.Combine(root, agent + ".log")) ? File.ReadAllLines(Path.Combine(root, agent + ".log")) : [];
    private void Mode(string agent, string mode) => File.WriteAllText(Path.Combine(root, agent + ".mode"), mode);
    private void Outcomes(string agent, params string[] outcomes) => File.WriteAllLines(Path.Combine(root, agent + ".outcomes"), outcomes);

    private void Sessions(string agent, params string[] ids)
    {
        var folder = Path.Combine(root, agent + "-sessions"); Directory.CreateDirectory(folder);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, "state.db"), Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE sessions(id TEXT,title TEXT,cwd TEXT,profile_name TEXT,last_activity_at REAL,model TEXT,source TEXT,hidden INTEGER,archived INTEGER)";
        command.ExecuteNonQuery();
        foreach (var id in ids)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO sessions VALUES($id,'Fixture',$cwd,NULL,1000,'model','desktop',0,0)";
            insert.Parameters.AddWithValue("$id", id); insert.Parameters.AddWithValue("$cwd", root); insert.ExecuteNonQuery();
        }
        service.ConfigureSessionDiscovery(agent, "hermes", folder);
    }

    private async Task Verify(string agent)
    {
        var created = await service.RunCliProbeAsync(agent, "create");
        Assert.IsTrue(created.MachinePassed, created.Error); Assert.IsTrue(created.FileWritten);
        service.ConfirmCliProbe(agent, created.ProbeId, true);
        var resumed = await service.RunCliProbeAsync(agent, "resume");
        Assert.IsTrue(resumed.MachinePassed, resumed.Error);
        service.ConfirmCliProbe(agent, resumed.ProbeId, true);
        File.Delete(Path.Combine(root, agent + ".log"));
    }

    private TaskRecord Loop(bool autoDeliver = true) => service.CreateCliHandoff(new("agent-a", "agent-b", "实现并通过验收", "source-a", null,
        true, root, root, new CollaborationOptions("agent-b", "可运行"), autoDeliver)).Task;

    private async Task<TaskRecord> SyncUntilSettled(string taskId)
    {
        for (var pass = 0; pass < 12; pass++)
        {
            var result = await service.SyncFileHandoffsAsync(true);
            Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors.Select(e => e.Message)));
            Assert.IsEmpty(result.DeliveryErrors, string.Join("; ", result.DeliveryErrors.Select(e => e.Message)));
            if (result.UpdatedTaskIds.Length == 0 && result.SentTaskIds.Length == 0) break;
        }
        return service.Status(taskId).Task;
    }

    [TestMethod]
    public async Task UnattendedRunContinuesInTheExactVerifiedConversationAfterReopening()
    {
        await Verify("agent-a"); await Verify("agent-b");
        Outcomes("agent-b", "submitted"); Outcomes("agent-a", "approved");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 4, 480);
        Assert.HasCount(1, (await service.SyncFileHandoffsAsync(true)).SentTaskIds);

        service = new RelayService(service.Home, clock);
        Assert.HasCount(1, (await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        CollectionAssert.AreEqual(new[] { "source-a" }, Calls("agent-a"));
        Assert.AreEqual(2, service.Status(task.TaskId).Task.UnattendedRun!.SendsStarted);
    }

    [TestMethod]
    public async Task VerifiedAgentsRunTheWholeReviewLoopInTheirOwnExactSessions()
    {
        await Verify("agent-a"); await Verify("agent-b");
        Outcomes("agent-b", "submitted", "submitted", "accepted");
        Outcomes("agent-a", "changes_requested", "approved");
        var task = await SyncUntilSettled(Loop().TaskId);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        Assert.AreEqual("delivered", service.FileHandoffProgress(task).Completion);
        CollectionAssert.AreEqual(new[] { "create", "created-agent-b", "created-agent-b" }, Calls("agent-b"));
        CollectionAssert.AreEqual(new[] { "source-a", "source-a" }, Calls("agent-a"));
        Assert.HasCount(5, task.Collaboration!.History);
    }

    [TestMethod]
    public async Task UnverifiedRecipeCanBeSentByHandButIsNeverSentUnattended()
    {
        Outcomes("agent-b", "submitted");
        var task = Loop();
        Assert.IsTrue(service.CanSendFileHandoff(task)); Assert.IsFalse(service.CanAutoDeliver(task));
        Assert.AreEqual("cli_ready", service.FileHandoffProgress(task).Delivery);
        StringAssert.Contains(service.FileHandoffProgress(task).DeliveryMessage!, "自动新建会话尚未验证");
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        Assert.IsEmpty(Calls("agent-b"));
        await service.SendFileHandoffAsync(task.TaskId, service.FileHandoffPrompt(task));
        await service.SyncFileHandoffsAsync(false);
        // The hand-sent turn whose Agent wrote its receipt is end-to-end evidence for new conversations.
        var extension = service.CliExtension("agent-b")!;
        Assert.IsNotNull(extension.CreateEvidence); Assert.IsTrue(extension.CanCreate); Assert.IsFalse(extension.CanResume);
        Assert.AreEqual("review", service.Status(task.TaskId).Task.Collaboration!.Phase);
    }

    [TestMethod]
    public async Task OptInIsRequiredForUnattendedTurns()
    {
        await Verify("agent-b");
        Outcomes("agent-b", "submitted");
        var task = Loop(autoDeliver: false);
        Assert.IsTrue(service.CanAutoDeliver(task));
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        Assert.AreEqual("cli_ready", service.FileHandoffProgress(task).Delivery);
        Assert.IsEmpty(Calls("agent-b"));
    }

    [TestMethod]
    public async Task LoginRefusalBlocksUnattendedCallsUntilTheUserResolvesIt()
    {
        await Verify("agent-b");
        Mode("agent-b", "auth"); Outcomes("agent-b", "submitted");
        var task = Loop();
        var first = await service.SyncFileHandoffsAsync(true);
        Assert.HasCount(1, first.SentTaskIds);
        task = service.Status(task.TaskId).Task;
        Assert.AreEqual("unknown", task.CliFileStatus);
        StringAssert.Contains(task.CliFileError, "单独登录");
        Assert.AreEqual("auth_required", service.CliExtension("agent-b")!.Block!.Kind);
        Assert.AreEqual("cli_blocked", service.FileHandoffProgress(task).Delivery);
        Assert.AreEqual("user", service.FileHandoffProgress(task).WaitingOn);
        Assert.IsNotNull(service.FileHandoffProgress(task).DeliveryMessage);
        service.AllowFileHandoffResend(task.TaskId, "目标会话没有收到", true);
        task = service.Status(task.TaskId).Task;
        Assert.IsFalse(service.CanSendFileHandoff(task)); Assert.IsFalse(service.CanAutoDeliver(task));
        var blocked=await Assert.ThrowsExactlyAsync<RelayException>(()=>service.SendFileHandoffAsync(task.TaskId,service.FileHandoffPrompt(task)));
        Assert.AreEqual("cli_blocked",blocked.Code);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        Assert.HasCount(1, Calls("agent-b"));
        new RelayStore(service.Home).Write(s=>
        {
            var current=s.Task(task.TaskId);current.DeadlineAt=DateTimeOffset.UtcNow.AddMinutes(-1);s.Save(current);return true;
        });
        service.ImportManualReceipts();
        var paused=service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Paused,paused.State);
        Assert.AreEqual("delivery_failed",paused.PauseReason);
        StringAssert.Contains(paused.PauseMessage!,"单独登录");
        Mode("agent-b", "ok"); service.ClearCliBlock("agent-b");
        task = service.ContinueFileHandoff(task.TaskId).Task;
        Assert.IsTrue(service.CanSendFileHandoff(task));
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        await service.SendFileHandoffAsync(task.TaskId,service.FileHandoffPrompt(task));
        await service.SyncFileHandoffsAsync(false);
        Assert.AreEqual("review", service.Status(task.TaskId).Task.Collaboration!.Phase);
    }

    [TestMethod]
    public async Task DifferentSessionOnResumeStopsAutomaticContinuation()
    {
        await Verify("agent-a"); await Verify("agent-b");
        Outcomes("agent-b", "submitted"); Mode("agent-a", "other"); Outcomes("agent-a", "approved");
        var task = await SyncUntilSettled(Loop().TaskId);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual("unknown", task.CliFileStatus);
        StringAssert.Contains(task.CliFileError, "不同的会话 ID");
        Assert.AreEqual("user", service.FileHandoffProgress(task).WaitingOn);
        Assert.HasCount(1, Calls("agent-a"));
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
    }

    [TestMethod]
    public async Task ProbeThatRepliesWithoutWritingTheFileReportsMissingToolPermission()
    {
        Mode("agent-b", "nowrite");
        var probe = await service.RunCliProbeAsync("agent-b", "create");
        Assert.IsFalse(probe.MachinePassed); Assert.IsFalse(probe.FileWritten);
        StringAssert.Contains(probe.Error, "工具权限");
        Assert.AreEqual("permission_required", service.CliExtension("agent-b")!.Block!.Kind);
        Mode("agent-b", "ok");
        Assert.IsTrue((await service.RunCliProbeAsync("agent-b", "create")).MachinePassed);
        Assert.IsNull(service.CliExtension("agent-b")!.Block);
    }

    [TestMethod]
    public async Task ManualTakeoverMidLoopUsesTheSameLifecycle()
    {
        await Verify("agent-b");
        Outcomes("agent-b", "submitted", "accepted");
        var task = service.CreateCliHandoff(new("agent-a", "agent-b", "实现", null, null, true, root, root,
            new CollaborationOptions("agent-b"), true)).Task;
        task = await SyncUntilSettled(task.TaskId);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual("copy_prompt", service.FileHandoffProgress(task).Delivery);
        service.FileHandoffPrompt(task);
        var context = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!, "context.json")));
        using (context) Assert.AreEqual("reviewer", context.RootElement.GetProperty("collaboration").GetProperty("role").GetString());
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId, "agent-a",
            task.ManualTurn, "done", Outcome: "approved")));
        task = await SyncUntilSettled(task.TaskId);
        Assert.AreEqual(TaskState.Succeeded, task.State);
        CollectionAssert.AreEqual(new[] { "create", "created-agent-b" }, Calls("agent-b"));
    }

    [TestMethod]
    public async Task UnattendedSendLimitSurvivesRestartAndAllowsManualTakeover()
    {
        await Verify("agent-a"); await Verify("agent-b");
        Outcomes("agent-b", "submitted");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 1, 480);
        Assert.HasCount(1, (await service.SyncFileHandoffsAsync(true)).SentTaskIds);

        service = new RelayService(service.Home, clock);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        task = service.Status(task.TaskId).Task;
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual(1, task.UnattendedRun!.SendsStarted);
        Assert.AreEqual(UnattendedHandoffStatus.SendLimit, service.UnattendedStatus(task));
        Assert.IsEmpty(Calls("agent-a"));
        Assert.IsTrue(service.CanSendFileHandoff(task));
        Outcomes("agent-a", "approved");
        await service.SendFileHandoffAsync(task.TaskId, service.FileHandoffPrompt(task));
        CollectionAssert.AreEqual(new[] { "source-a" }, Calls("agent-a"));
        Assert.AreEqual(1, service.Status(task.TaskId).Task.UnattendedRun!.SendsStarted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnattendedRunCannotSendAfterElapsedDowntimeOrOperatorStop(bool stop)
    {
        await Verify("agent-b");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 4, 1);
        if (stop) service.StopUnattendedHandoff(task.TaskId);
        else clock.UtcNow = clock.UtcNow.AddMinutes(1);

        service = new RelayService(service.Home, clock);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        task = service.Status(task.TaskId).Task;
        Assert.AreEqual(stop ? UnattendedHandoffStatus.Stopped : UnattendedHandoffStatus.TimeLimit,
            service.UnattendedStatus(task));
        Assert.AreEqual(0, task.UnattendedRun!.SendsStarted);
        Assert.IsEmpty(Calls("agent-b"));
    }

    [TestMethod]
    public async Task UnattendedRunStopsWhenTheBoundConversationDisappears()
    {
        await Verify("agent-a"); await Verify("agent-b");
        Outcomes("agent-b", "submitted");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 4, 480);
        await service.SyncFileHandoffsAsync(true);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(root, "agent-a-sessions", "state.db"), Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM sessions WHERE id='source-a'";
            command.ExecuteNonQuery();
        }
        var result = await service.SyncFileHandoffsAsync(true);
        Assert.IsEmpty(result.SentTaskIds);
        Assert.HasCount(1, result.DeliveryErrors);
        service = new RelayService(service.Home, clock);
        task = service.Status(task.TaskId).Task;
        Assert.AreEqual(UnattendedHandoffStatus.SessionUnverified, service.UnattendedStatus(task));
        Assert.AreEqual(1, task.UnattendedRun!.SendsStarted);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).DeliveryErrors);
        Assert.IsEmpty(Calls("agent-a"));
    }

    [TestMethod]
    public async Task UnreadableReceiptHoldsUnattendedDeliveryWithoutSpendingTheBudget()
    {
        await Verify("agent-b");
        Outcomes("agent-b", "submitted");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 4, 480);
        var receipt = service.ManualReceiptPath(task);
        File.WriteAllText(receipt, "{");

        var result = await service.SyncFileHandoffsAsync(true);
        Assert.HasCount(1, result.Errors);
        Assert.IsEmpty(result.SentTaskIds);
        Assert.AreEqual(0, service.Status(task.TaskId).Task.UnattendedRun!.SendsStarted);
        Assert.IsEmpty(Calls("agent-b"));

        File.Delete(receipt);
        Assert.HasCount(1, (await service.SyncFileHandoffsAsync(true)).SentTaskIds);
    }

    [TestMethod]
    public async Task AValidReceiptWithoutAHostConclusionDoesNotStartTheNextUnattendedTurn()
    {
        await Verify("agent-a"); await Verify("agent-b");
        Outcomes("agent-b", "submitted"); Mode("agent-b", "interrupted");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 4, 480);
        Assert.HasCount(1, (await service.SyncFileHandoffsAsync(true)).SentTaskIds);

        service = new RelayService(service.Home, clock);
        var result = await service.SyncFileHandoffsAsync(true);
        Assert.IsEmpty(result.Errors);
        Assert.IsEmpty(result.SentTaskIds);
        task = service.Status(task.TaskId).Task;
        Assert.AreEqual(1, task.ManualTurn);
        Assert.AreEqual("agent-a", task.AwaitingAgentId);
        Assert.AreEqual(UnattendedHandoffStatus.HostUnconfirmed, service.UnattendedStatus(task));
        Assert.IsEmpty(Calls("agent-a"));
        Assert.IsTrue(service.CanSendFileHandoff(task));
    }

    [TestMethod]
    public async Task ExpiredInterruptedSendReleasesManualTakeoverAfterItsReceiptAdvancedTheTurn()
    {
        await Verify("agent-a");
        var task = Loop(autoDeliver: false);
        service.StartUnattendedHandoff(task.TaskId, 4, 480);
        new RelayStore(service.Home).Write(s =>
        {
            var saved = s.Task(task.TaskId);
            saved.CliFileTurn = 0;
            saved.CliFileStatus = "submitting";
            s.Save(saved);
            return true;
        });
        var reply = service.TurnReplyPath(task);
        File.WriteAllText(reply, "Late work from the interrupted host.");
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId,
            task.AttemptId, "agent-b", 0, "done", ResultFile: reply, Outcome: "submitted")));

        service = new RelayService(service.Home, clock);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        task = service.Status(task.TaskId).Task;
        Assert.AreEqual(1, task.ManualTurn);
        Assert.AreEqual("submitting", task.CliFileStatus);
        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        task = service.ContinueFileHandoff(task.TaskId).Task;
        Assert.AreEqual("unknown", task.CliFileStatus);
        Assert.IsTrue(service.CanSendFileHandoff(task));
        Assert.IsFalse(service.CanAutoDeliver(task));
        Assert.IsEmpty(Calls("agent-a"));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
