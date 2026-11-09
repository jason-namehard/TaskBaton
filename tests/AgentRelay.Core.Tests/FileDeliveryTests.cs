using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class FileDeliveryTests
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
        service.RegisterManual("agent-a", "fixture", "甲"); service.RegisterManual("agent-b", "fixture", "乙");
        source = Path.Combine(root, "source.md"); File.WriteAllText(source, "Review this file.");
    }

    // Probes ask the Agent to write a file; a fixture that can use tools writes it like a real Agent would.
    private const string ProbeFile = "$f=[regex]::Match($p,'Create the file (.+?) containing').Groups[1].Value;if($f){[IO.File]::WriteAllText($f,$m)};";

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }

    private string CountFile(string agent) => Path.Combine(root, agent + ".calls");
    private CliRecipe Recipe(string agent, bool invalidDelivery = false)
    {
        var command = new CliCommand("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::InputEncoding=[Text.UTF8Encoding]::new($false);[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            "$p=[Console]::In.ReadToEnd();$m=[regex]::Match($p,'RELAY-[A-Z0-9]+').Value;" + ProbeFile +
            "if($m){@{session_id='fixture-session';reply=$m;accepted=$true}|ConvertTo-Json -Compress}else{" +
            "[IO.File]::AppendAllText($env:RELAY_FIXTURE_COUNT,'sent'+[Environment]::NewLine);" +
            (invalidDelivery ? "[Console]::Out.Write('not-a-receipt')" :
                "@{session_id='fixture-delivery-session';reply='Accepted work';accepted=$true}|ConvertTo-Json -Compress") + "}"],
            "{prompt}", null, new Dictionary<string, string> { ["RELAY_FIXTURE_COUNT"] = CountFile(agent) });
        return new(1, "Local file delivery fixture", "fixture", command, null, [], "fixture", "tests", "MIT", "No model calls.");
    }

    private async Task Verify(string agent, bool invalidDelivery = false)
    {
        service.ImportCliRecipe(agent, RelayJson.Write(Recipe(agent, invalidDelivery)));
        var probe = await service.RunCliProbeAsync(agent, "create");
        Assert.IsTrue(probe.MachinePassed, probe.Error);
        service.ConfirmCliProbe(agent, probe.ProbeId, true);
    }

    private TaskRecord Create() => service.CreateFileHandoff(new("agent-a", "agent-b", source, "Review")).Task;
    private int Calls(string agent) => File.Exists(CountFile(agent)) ? File.ReadAllLines(CountFile(agent)).Length : 0;

    private async Task<TaskRecord> InterruptedSend()
    {
        await Verify("agent-b");
        service.SetPermission("agent-a", "agent-b", "allow");
        var task = Create();
        task = service.ConfigureFileHandoff(task.TaskId, new(AutoDeliver: true)).Task;
        Assert.IsTrue(service.CanAutoDeliver(task));
        new RelayStore(service.Home).Write(s =>
        {
            var current = s.Task(task.TaskId);
            current.CliFileTurn = current.ManualTurn; current.CliFileStatus = "submitting";
            s.Save(current);
            return true;
        });
        // Reopen the persisted station as a new Relay process would after an interrupted send.
        service = new RelayService(service.Home, clock);
        return service.Status(task.TaskId).Task;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterruptedSendTimesOutWithoutResendingAndCanBeExplicitlyRetried(bool recover)
    {
        var task = await InterruptedSend();
        clock.Advance(TimeSpan.FromMinutes(31));
        if (recover) await service.RecoverAsync(task.TaskId);
        else service.ImportManualReceipts();
        var paused = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Paused, paused.State);
        Assert.AreEqual("turn_timeout", paused.PauseReason);
        Assert.AreEqual("unknown", paused.CliFileStatus);
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        Assert.AreEqual(0, Calls("agent-b"));
        service.ContinueFileHandoff(task.TaskId);
        Assert.AreEqual("cli_unconfirmed", service.FileHandoffProgress(service.Status(task.TaskId).Task).Delivery);
        Assert.IsFalse(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        Assert.ThrowsExactly<RelayException>(() => service.AllowFileHandoffResend(task.TaskId, "Not received", false));
        service.AllowFileHandoffResend(task.TaskId, "Target did not receive this turn", true);
        service = new RelayService(service.Home, clock);
        Assert.IsTrue(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        Assert.IsEmpty((await service.SyncFileHandoffsAsync(true)).SentTaskIds);
        var refused = await Assert.ThrowsExactlyAsync<RelayException>(() =>
            service.SendFileHandoffAsync(task.TaskId, "Read the file.", automatic: true));
        Assert.AreEqual("file_cli_changed", refused.Code);
        Assert.AreEqual(0, Calls("agent-b"));
        await service.SendFileHandoffAsync(task.TaskId, "Read the file.");
        Assert.AreEqual("submitted", service.Status(task.TaskId).Task.CliFileStatus);
        Assert.AreEqual(1, Calls("agent-b"));
    }

    [TestMethod]
    public async Task RecoveryKeepsAnInFlightSendBeforeItsDeadline()
    {
        var task = await InterruptedSend();
        service.ImportManualReceipts();
        await service.RecoverAsync(task.TaskId);
        Assert.AreEqual("submitting", service.Status(task.TaskId).Task.CliFileStatus);
        Assert.IsFalse(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        Assert.AreEqual(0, Calls("agent-b"));
    }

    [TestMethod]
    public async Task TimedOutInterruptedSendStillAcceptsTheLateReceipt()
    {
        var task = await InterruptedSend();
        clock.Advance(TimeSpan.FromMinutes(31));
        service.ImportManualReceipts();
        Assert.AreEqual("unknown", service.Status(task.TaskId).Task.CliFileStatus);
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId,
            task.AttemptId, "agent-b", 0, "done", false)));
        var imported = service.ImportManualReceipts();
        Assert.IsEmpty(imported.Errors);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        Assert.AreEqual(0, Calls("agent-b"));
    }

    [TestMethod]
    public async Task APausedTurnCanAuthorizeAManualResendAndStillAcceptItsLateReceipt()
    {
        var task = await InterruptedSend();
        clock.Advance(TimeSpan.FromMinutes(31));
        service.ImportManualReceipts();
        Assert.AreEqual(TaskState.Paused, service.Status(task.TaskId).Task.State);
        var stale = Assert.ThrowsExactly<RelayException>(() =>
            service.AllowFileHandoffResend(task.TaskId, "Checked the target", true, expectedTurn: 1));
        Assert.AreEqual("file_resend_not_needed", stale.Code);

        var ready = service.AllowFileHandoffResend(task.TaskId, "Checked the target", true, expectedTurn: 0).Task;
        Assert.AreEqual(TaskState.AwaitingManual, ready.State);
        Assert.AreEqual(0, ready.ManualResendTurn);
        Assert.IsTrue(service.CanSendFileHandoff(ready));
        Assert.IsFalse(service.CanAutoDeliver(ready));

        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId,
            task.AttemptId, "agent-b", 0, "working")));
        var refused = await Assert.ThrowsExactlyAsync<RelayException>(() =>
            service.SendFileHandoffAsync(task.TaskId, "Read the file.", expectedTurn: 0));
        Assert.AreEqual("file_cli_changed", refused.Code);
        var working = service.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Running, working.State);
        Assert.IsNull(working.ManualResendTurn);
        Assert.IsFalse(service.CanSendFileHandoff(working));
        Assert.AreEqual(0, Calls("agent-b"));
    }

    [TestMethod]
    [DataRow("continue")]
    [DataRow("import")]
    [DataRow("recover")]
    public async Task APreviouslyPausedInterruptedSendCanBeRecovered(string recovery)
    {
        var task = await InterruptedSend();
        clock.Advance(TimeSpan.FromMinutes(31));
        new RelayStore(service.Home).Write(s =>
        {
            var current = s.Task(task.TaskId);
            current.PausedFrom = current.State; current.State = TaskState.Paused;
            current.PauseReason = "turn_timeout";
            s.Save(current);
            return true;
        });
        if (recovery == "import") service.ImportManualReceipts();
        else if (recovery == "recover") await service.RecoverAsync(task.TaskId);
        if (recovery != "continue") Assert.AreEqual("unknown", service.Status(task.TaskId).Task.CliFileStatus);
        var continued = service.ContinueFileHandoff(task.TaskId).Task;
        Assert.AreEqual("unknown", continued.CliFileStatus);
        service.UseNewTaskSession(task.TaskId, "agent-b");
        Assert.IsTrue(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        Assert.AreEqual(0, Calls("agent-b"));
    }

    [TestMethod]
    public async Task RegisteredRecipeCanSendWithoutAVisibilityProbeWhenDirectionIsEnabled()
    {
        service.ImportCliRecipe("agent-b", RelayJson.Write(Recipe("agent-b")));
        service.SetPermission("agent-a", "agent-b", "allow");
        var task = Create();
        Assert.IsTrue(service.CanSendFileHandoff(task));
        var sent = await service.SendFileHandoffAsync(task.TaskId, "Read the file.");
        Assert.AreEqual(1, Calls("agent-b"));
        Assert.AreEqual("created", service.TaskSession(task.TaskId,"agent-b")!.Binding);
        Assert.AreEqual("submitted",sent.Task.CliFileStatus);
    }

    [TestMethod]
    public async Task VerifiedRecipeRequiresEnabledDirection()
    {
        await Verify("agent-b");
        var task = Create();
        Assert.IsFalse(service.CanSendFileHandoff(task));
        await Assert.ThrowsExactlyAsync<RelayException>(() => service.SendFileHandoffAsync(task.TaskId, "Read the file."));
        service.SetPermission("agent-a", "agent-b", "allow");
        Assert.IsTrue(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        service.SetPermission("agent-a", "agent-b", "deny");
        Assert.IsFalse(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        Assert.AreEqual(0, Calls("agent-b"));
    }

    [TestMethod]
    public async Task SuccessfulSubmissionSendsOnlyOnceAndDoesNotCompleteWork()
    {
        await Verify("agent-b"); service.SetPermission("agent-a", "agent-b", "allow");
        var task = Create();
        var sent = await service.SendFileHandoffAsync(task.TaskId, "Read the file and write its receipt.");
        Assert.AreEqual(TaskState.Dispatched, sent.Task.State);
        Assert.AreEqual("submitted", sent.Task.CliFileStatus);
        Assert.AreEqual("fixture-delivery-session", sent.Task.CliFileSessionId);
        Assert.AreEqual(0, sent.Task.CliFileTurn);
        Assert.IsFalse(service.CanSendFileHandoff(sent.Task));
        await Assert.ThrowsExactlyAsync<RelayException>(() => service.SendFileHandoffAsync(task.TaskId, "Do not send twice."));
        Assert.AreEqual(1, Calls("agent-b"));
    }

    [TestMethod]
    public async Task UncertainSubmissionCannotBeAutomaticallyRetried()
    {
        await Verify("agent-b", invalidDelivery: true); service.SetPermission("agent-a", "agent-b", "allow");
        var task = Create();
        var sent = await service.SendFileHandoffAsync(task.TaskId, "Read the file.");
        Assert.AreEqual("unknown", sent.Task.CliFileStatus);
        Assert.AreEqual(TaskState.AwaitingManual, sent.Task.State);
        Assert.AreEqual(0, sent.Task.CliFileTurn);
        Assert.IsFalse(service.CanSendFileHandoff(sent.Task));
        await Assert.ThrowsExactlyAsync<RelayException>(() => service.SendFileHandoffAsync(task.TaskId, "Do not retry unknown submission."));
        service.RegisterTurnStatus(task.TaskId, "working");
        Assert.AreEqual("file_resend_not_needed", Assert.ThrowsExactly<RelayException>(() =>
            service.AllowFileHandoffResend(task.TaskId, "Checked the target", true)).Code);
        Assert.AreEqual(1, Calls("agent-b"));
    }

    [TestMethod]
    public async Task ReplyTurnChecksTheOppositeRecipientAndDirection()
    {
        await Verify("agent-b"); service.SetPermission("agent-a", "agent-b", "allow");
        var task = Create(); await service.SendFileHandoffAsync(task.TaskId, "Read the file.");
        var reply = Path.Combine(root, "reply.md"); File.WriteAllText(reply, "Please choose option B.");
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId,
            task.AttemptId, "agent-b", 0, "done", true, reply, "Choose an option.")));
        var imported = service.ImportManualReceipts(); Assert.IsEmpty(imported.Errors);
        var next = service.Status(task.TaskId).Task;
        Assert.AreEqual(1, next.ManualTurn);
        Assert.AreEqual("agent-a", next.AwaitingAgentId);
        Assert.IsFalse(service.CanSendFileHandoff(next));
        await Verify("agent-a");
        Assert.IsFalse(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        service.SetPermission("agent-b", "agent-a", "allow");
        Assert.IsFalse(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        service.UseNewTaskSession(task.TaskId,"agent-a");
        Assert.IsTrue(service.CanSendFileHandoff(service.Status(task.TaskId).Task));
        var sent = await service.SendFileHandoffAsync(task.TaskId, "Read B's reply.");
        Assert.AreEqual(1, sent.Task.CliFileTurn);
        Assert.AreEqual("cli:agent-a", sent.Task.AdapterId);
        Assert.AreEqual(TaskState.Dispatched, sent.Task.State);
        Assert.AreEqual(1, Calls("agent-a")); Assert.AreEqual(1, Calls("agent-b"));
    }
}
