using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ContinuationTests
{
    private string root = null!;
    private string home = null!;
    private Clock clock = null!;
    private RelayService service = null!;
    private OriginSession origin = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.ContinuationTests", Guid.NewGuid().ToString());
        home = Path.Combine(root, "relay"); clock = new Clock(); service = NewService(); service.Initialize();
        foreach (var id in new[] { "agent-a", "agent-b" })
        {
            var path = Path.Combine(root, id + ".toml"); File.WriteAllText(path, "fixture=true");
            service.Register(id, "fixture", path); service.ValidateAgent(id);
        }
        service.SetPermission("agent-a", "agent-b", "allow");
        origin = new OriginSession("fixture", Guid.NewGuid().ToString(), Path.Combine(root, "agent-a.toml"), root);
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);
    private RelayService NewService() => new(home, clock);
    private async Task<TaskRecord> Complete(bool withOrigin = true, bool failed = false)
    {
        var task = (await service.DispatchAsync(new DispatchRequest("agent-a", "agent-b", "请计算 54。", "manual", "manual", "test",
            OriginSession: withOrigin ? origin : null))).Task;
        File.WriteAllText(task.ResultPath, "## 结果\n54");
        service.Complete(task.TaskId, task.AttemptId, "agent-b", "final", task.ResultPath, failed,
            failed ? "missing_input" : null, failed ? "缺少输入" : null);
        return task;
    }

    [TestMethod]
    public async Task ResultAndPendingContinuationSurviveRestartBeforeSubmission()
    {
        var task = await Complete();
        Assert.AreEqual("pending", NewService().Status(task.TaskId).Task.ResumeStatus);
        var resumer = new Resumer(); var desktop = new Notifier();
        await NewService().ProcessContinuationsAsync(task.TaskId, resumer, desktop);
        Assert.AreEqual(1, resumer.Calls); Assert.AreEqual(0, desktop.Calls);
        Assert.AreEqual(origin, resumer.Input!.Task.OriginSession);
        StringAssert.Contains(resumer.Input.Prompt, task.TaskId);
        Assert.AreEqual("submitted", service.Status(task.TaskId).Task.ResumeStatus);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public async Task Repeated124CallbacksAndStatusReadsCannotResubmitContinuation()
    {
        var task = await Complete(); var resumer = new Resumer(); var desktop = new Notifier();
        for (var i = 0; i < 124; i++)
        {
            NewService().Complete(task.TaskId, task.AttemptId, "agent-b", "final", task.ResultPath);
            NewService().Status(task.TaskId);
            await NewService().ProcessContinuationsAsync(task.TaskId, resumer, desktop);
        }
        Assert.AreEqual(1, resumer.Calls); Assert.HasCount(1, service.Continuations(task.TaskId));
    }

    [TestMethod]
    public async Task ConcurrentContinuationConsumersHaveOneOwner()
    {
        var task = await Complete(); var resumer = new Resumer { Delay = true }; var notifier = new Notifier();
        await Task.WhenAll(service.ProcessContinuationsAsync(task.TaskId, resumer, notifier),
            NewService().ProcessContinuationsAsync(task.TaskId, resumer, notifier));
        Assert.AreEqual(1, resumer.Calls);
    }

    [TestMethod]
    [DataRow("failed")]
    [DataRow("unknown")]
    public async Task FailedOrUnknownResumePreservesResultAndSendsOneFallback(string state)
    {
        var task = await Complete(); var resumer = new Resumer { Result = new ResumeResult(state, "fixture", "需要手工继续") };
        var notifier = new Notifier();
        await service.ProcessContinuationsAsync(task.TaskId, resumer, notifier);
        await NewService().ProcessContinuationsAsync(task.TaskId, resumer, notifier);
        Assert.AreEqual(1, resumer.Calls); Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        Assert.AreEqual(state, service.Status(task.TaskId).Task.ResumeStatus);
        Assert.AreEqual("submitted", service.Continuations(task.TaskId).Single().DesktopState);
        StringAssert.Contains(File.ReadAllText(service.Status(task.TaskId).Task.ResultPath), "54");
    }

    [TestMethod]
    public async Task UnsupportedSessionAndDisabledNotificationsStillHaveCopyablePrompt()
    {
        var task = await Complete(withOrigin: false, failed: true);
        var resumer = new Resumer(); var notifier = new Notifier { Result = new ResumeResult("unavailable", "disabled", "通知已关闭") };
        await service.ProcessContinuationsAsync(task.TaskId, resumer, notifier);
        Assert.AreEqual(0, resumer.Calls); Assert.AreEqual(1, notifier.Calls);
        var view = NewService().Status(task.TaskId);
        Assert.AreEqual(TaskState.Failed, view.Task.State); Assert.AreEqual("missing_input", view.Task.ErrorCode);
        Assert.AreEqual("manual_required", view.Task.ResumeStatus);
        StringAssert.Contains(view.Prompt, view.Task.ResultPath);
        Assert.AreEqual("unavailable", view.Continuation!.DesktopState);
    }

    [TestMethod]
    public async Task CrashDuringResumeBecomesUnknownWithoutReplay()
    {
        var task = await Complete(); var resumer = new Resumer { Crash = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ProcessContinuationsAsync(task.TaskId, resumer, new Notifier(), 10));
        clock.Advance(TimeSpan.FromSeconds(56));
        await NewService().RecoverAsync(task.TaskId);
        await NewService().ProcessContinuationsAsync(task.TaskId, resumer, new Notifier());
        Assert.AreEqual(1, resumer.Calls); Assert.AreEqual("unknown", service.Status(task.TaskId).Task.ResumeStatus);
    }

    [TestMethod]
    public async Task CancelledTaskDoesNotWakeOrigin()
    {
        var task = await Complete(failed: true); service.Cancel(task.TaskId, "operator cancellation");
        var resumer = new Resumer(); var notifier = new Notifier();
        await service.ProcessContinuationsAsync(task.TaskId, resumer, notifier);
        Assert.AreEqual(0, resumer.Calls); Assert.AreEqual(0, notifier.Calls);
        Assert.AreEqual("superseded", service.Continuations(task.TaskId).Single().State);
    }

    [TestMethod]
    public async Task NewAttemptSupersedesOldPendingContinuation()
    {
        var task = await Complete(failed: true);
        var retried = await service.RetryAsync(task.TaskId, "retry approved", true);
        var resumer = new Resumer(); var notifier = new Notifier();
        await service.ProcessContinuationsAsync(task.TaskId, resumer, notifier);
        Assert.AreNotEqual(task.AttemptId, retried.Task.AttemptId);
        Assert.AreEqual(0, resumer.Calls); Assert.AreEqual(0, notifier.Calls);
        Assert.AreEqual("superseded", service.Continuations(task.TaskId).Single().State);
        Assert.AreEqual("manual_required", service.Status(task.TaskId).Task.ResumeStatus);
    }

    [TestMethod]
    public async Task CrashDuringDesktopSubmissionDoesNotResendNotification()
    {
        var task = await Complete(withOrigin: false); var notifier = new Notifier { Crash = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ProcessContinuationsAsync(task.TaskId, notifier: notifier));
        clock.Advance(TimeSpan.FromSeconds(46));
        await NewService().RecoverAsync(task.TaskId);
        await service.ProcessContinuationsAsync(task.TaskId, notifier: notifier);
        Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual("unknown", service.Continuations(task.TaskId).Single().DesktopState);
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public async Task ExpiredArtifactsPreventAutomaticResume()
    {
        var task = await Complete(); clock.Advance(TimeSpan.FromDays(4));
        var resumer = new Resumer(); var notifier = new Notifier();
        await service.ProcessContinuationsAsync(task.TaskId, resumer, notifier);
        Assert.AreEqual(0, resumer.Calls); Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual("continuation_expired", service.Continuations(task.TaskId).Single().ErrorCode);
        StringAssert.Contains(service.Status(task.TaskId).Prompt, "过期或缺失");
    }

    [TestMethod]
    public void BindingRejectsAnotherConfigHostOrExecutable()
    {
        var config = Path.Combine(root, "config.toml"); var exe = Path.Combine(root, "codex.exe");
        var agent = new AgentRecord("agent-a", "codex", config, exe, null, [], "path_verified", null);
        var adapter = new ResumerRecord("agent-a", "codex-queue", exe, "version", config, clock.GetUtcNow());
        var target = origin with { Host = "codex", ConfigProfile = config };
        CodexSessionResumer.RequireBinding(agent, adapter, target);
        foreach (var invalid in new[] { target with { Host = "claude" }, target with { ConfigProfile = Path.Combine(root, "other.toml") },
                     target with { SessionId = "latest" } })
            Assert.AreEqual("origin_binding_mismatch", Assert.ThrowsExactly<RelayException>(() => CodexSessionResumer.RequireBinding(agent, adapter, invalid)).Code);
        Assert.ThrowsExactly<RelayException>(() => CodexSessionResumer.RequireBinding(agent with { CliPath = "other" }, adapter, target));
    }

    [TestMethod]
    public void ExactSessionMetadataRequiresMatchingUuidAndCwd()
    {
        var directory = Path.Combine(root, "sessions", "2026", "10", "22"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "rollout-test-" + origin.SessionId + ".jsonl");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { type = "session_meta", payload = new { id = origin.SessionId, cwd = root } }));
        CodexSessionResumer.RequireSession(origin, root);
        Assert.AreEqual("origin_session_mismatch", Assert.ThrowsExactly<RelayException>(() => CodexSessionResumer.RequireSession(origin with { Cwd = home }, root)).Code);
        Assert.AreEqual("origin_session_missing", Assert.ThrowsExactly<RelayException>(() => CodexSessionResumer.RequireSession(origin with { SessionId = Guid.NewGuid().ToString() }, root)).Code);
    }

    [TestMethod]
    public void QueueReceiptMeansSubmittedAndTimeoutMeansUnknown()
    {
        var accepted = CodexSessionResumer.ParseResult("codex-queue", origin.SessionId,
            new ProcessResult(0, "Queued message message-id for thread " + origin.SessionId + ".", "", false), null, "");
        Assert.AreEqual("submitted", accepted.State);
        Assert.AreEqual("unknown", CodexSessionResumer.ParseResult("codex-queue", origin.SessionId, new ProcessResult(0, "", "", false), null, "").State);
        Assert.AreEqual("unknown", CodexSessionResumer.ParseResult("codex-queue", origin.SessionId, new ProcessResult(-1, "", "", true), null, "").State);
    }

    [TestMethod]
    public void ResumeCompletionRequiresExactSessionTurnAndReply()
    {
        var events = "{\"type\":\"thread.started\",\"thread_id\":\"" + origin.SessionId + "\"}\n{\"type\":\"turn.completed\"}";
        Assert.AreEqual("completed", CodexSessionResumer.ParseResult("codex-exec-resume", origin.SessionId,
            new ProcessResult(0, events, "", false), "54", "answer.md").State);
        Assert.AreEqual("unknown", CodexSessionResumer.ParseResult("codex-exec-resume", "another-session",
            new ProcessResult(0, events, "", false), "54", "answer.md").State);
        Assert.AreEqual("unknown", CodexSessionResumer.ParseResult("codex-exec-resume", origin.SessionId,
            new ProcessResult(0, events, "", false), null, "answer.md").State);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }
    private sealed class Resumer : ISessionResumer
    {
        public int Calls;
        public ResumeInput? Input;
        public bool Crash { get; init; }
        public bool Delay { get; init; }
        public ResumeResult Result { get; init; } = new("submitted");
        public async Task<ResumeResult> ResumeAsync(ResumeInput input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls); Input = input;
            if (Crash) throw new InvalidOperationException("simulated process crash");
            if (Delay) await Task.Delay(100, cancellationToken);
            return Result;
        }
    }
    private sealed class Notifier : IDesktopNotifier
    {
        public int Calls;
        public bool Crash { get; init; }
        public ResumeResult Result { get; init; } = new("submitted");
        public Task<ResumeResult> NotifyAsync(string home, TaskRecord task, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Crash) throw new InvalidOperationException("simulated notifier crash");
            return Task.FromResult(Result);
        }
    }
}
