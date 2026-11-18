using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ExecutionTests
{
    private string root = null!;
    private string home = null!;
    private TestClock clock = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.ExecutionTests", Guid.NewGuid().ToString());
        home = Path.Combine(root, "data"); clock = new TestClock();
        service = NewService(); service.Initialize();
        foreach (var id in new[] { "agent-a", "agent-b" })
        {
            var path = Path.Combine(root, id + ".json"); File.WriteAllText(path, "{}");
            service.Register(id, "fixture", path); service.ValidateAgent(id);
        }
        service.SetPermission("agent-a", "agent-b", "allow");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, recursive: true);
    private RelayService NewService() => new(home, clock, [new AcceptedTransport()]);
    private async Task<TaskRecord> Dispatch() => (await service.DispatchAsync(new DispatchRequest(
        "agent-a", "agent-b", "请计算 4+9+16+25，给出中文结果。", "auto", "fixture", "work-1"))).Task;

    [TestMethod]
    public async Task OneShotReceiverStartsAgentAndExplicitlyCompletes()
    {
        var task = await Dispatch();
        var receiver = new Receiver(); var executor = new Executor();
        var done = await service.RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: receiver);
        Assert.AreEqual(TaskState.Succeeded, done.Task.State);
        Assert.AreEqual(1, receiver.Calls); Assert.AreEqual(1, executor.Calls);
        var execution = service.ExecutionEvidence(task.TaskId)!;
        Assert.AreEqual("finished", execution.State); Assert.AreEqual("fixture-session", execution.SessionId);
        CollectionAssert.Contains(service.Audit(task.TaskId).Select(e => e.Event).ToArray(), "task.acknowledged");
        CollectionAssert.Contains(service.Audit(task.TaskId).Select(e => e.Event).ToArray(), "task.completed");
    }

    [TestMethod]
    public async Task Repeated124RunsAcrossServicesDoNotStartAnotherAgent()
    {
        var task = await Dispatch();
        var receiver = new Receiver(); var executor = new Executor();
        for (var i = 0; i < 124; i++)
            await NewService().RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: receiver);
        Assert.AreEqual(1, executor.Calls); Assert.AreEqual(1, receiver.Calls);
        Assert.AreEqual(1, service.Audit(task.TaskId).Count(e => e.Event == "execution.claimed"));
    }

    [TestMethod]
    public async Task ConcurrentWorkersHaveOneExecutionOwner()
    {
        var task = await Dispatch(); var executor = new Executor { Delay = true }; var receiver = new Receiver();
        await Task.WhenAll(
            service.RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: receiver),
            NewService().RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: receiver));
        Assert.AreEqual(1, executor.Calls); Assert.AreEqual(1, receiver.Calls);
    }

    [TestMethod]
    public async Task ExplicitAgentFailureIsPersistedWithFailureReport()
    {
        var task = await Dispatch();
        var executor = new Executor { Outcome = new AgentOutcome("failed", "## 失败\n缺少输入。", "missing_input", "未提供必填输入。", "fixture-failure") };
        var failed = await service.RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: new Receiver());
        Assert.AreEqual(TaskState.Failed, failed.Task.State);
        Assert.AreEqual("missing_input", failed.Task.ErrorCode);
        StringAssert.Contains(File.ReadAllText(failed.Task.ResultPath), "缺少输入");
        Assert.AreEqual("finished", NewService().ExecutionEvidence(task.TaskId)!.State);
    }

    [TestMethod]
    public async Task MissingCallbackRemainsUnknownAndDoesNotRestart()
    {
        var task = await Dispatch();
        var executor = new Executor { Outcome = new AgentOutcome("missing_callback", "", "executor_timed_out", "No declaration") };
        await service.RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: new Receiver());
        await NewService().RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: new Receiver());
        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(TaskState.Running, service.Status(task.TaskId).Task.State);
        Assert.AreEqual("unknown", service.ExecutionEvidence(task.TaskId)!.State);
        clock.Advance(TimeSpan.FromMinutes(31)); await service.RecoverAsync(task.TaskId);
        Assert.AreEqual(TaskState.TimedOut, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public async Task CrashAfterClaimBecomesUnknownOnRecoveryWithoutReexecution()
    {
        var task = await Dispatch(); var executor = new Executor { Crash = true };
        await Assert.ThrowsExactlyAsync<IOException>(() => service.RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", 10,
            executor, new Receiver()));
        clock.Advance(TimeSpan.FromSeconds(56));
        await NewService().RecoverAsync(task.TaskId);
        await NewService().RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: new Receiver());
        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual("unknown", service.ExecutionEvidence(task.TaskId)!.State);
        Assert.AreEqual("inspect_execution_evidence", service.Status(task.TaskId).NextAction);
    }

    [TestMethod]
    public async Task ReceiveMismatchDoesNotLaunchAgent()
    {
        var task = await Dispatch(); var executor = new Executor();
        var receiver = new Receiver { Reject = true };
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.RunTaskAsync(task.TaskId, task.AttemptId,
            "agent-b", executor: executor, receiver: receiver));
        Assert.AreEqual("message_mismatch", error.Code); Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual("unknown", service.ExecutionEvidence(task.TaskId)!.State);
    }

    [TestMethod]
    public async Task OldWorkerCannotExecuteNewAttempt()
    {
        var task = await Dispatch(); clock.Advance(TimeSpan.FromMinutes(31)); await service.RecoverAsync();
        await service.RetryAsync(task.TaskId, "operator retry", true);
        var executor = new Executor();
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.RunTaskAsync(task.TaskId, task.AttemptId,
            "agent-b", executor: executor, receiver: new Receiver()));
        Assert.AreEqual("stale_attempt", error.Code); Assert.AreEqual(0, executor.Calls);
    }

    [TestMethod]
    public async Task DirectionSwitchNoLongerBlocksExecution()
    {
        var task = await Dispatch(); service.SetPermission("agent-a", "agent-b", "deny");
        var executor = new Executor();
        await service.RunTaskAsync(task.TaskId, task.AttemptId, "agent-b", executor: executor, receiver: new Receiver());
        Assert.AreEqual(1, executor.Calls);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
    private sealed class AcceptedTransport : IDeliveryAdapter
    {
        public string Id => "fixture";
        public Task<DeliveryResult> DeliverAsync(TaskRecord task, string home, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(new DeliveryResult("accepted", "fixture-receipt", null));
    }
    private sealed class Receiver : IMessageReceiver
    {
        public int Calls;
        public bool Reject { get; init; }
        public Task ReceiveAsync(TaskRecord task, string home, OutboxItem evidence, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Reject) throw new RelayException("message_mismatch", "fixture mismatch", 6);
            return Task.CompletedTask;
        }
    }
    private sealed class Executor : IAgentExecutor
    {
        public int Calls;
        public bool Delay { get; init; }
        public bool Crash { get; init; }
        public AgentOutcome Outcome { get; init; } = new("succeeded", "## Result\n答案是 54。", "", "verified", "fixture-session");
        public async Task<AgentOutcome> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            StringAssert.Contains(input.RequestMarkdown, "请计算");
            if (Crash) throw new IOException("simulated worker crash");
            if (Delay) await Task.Delay(100, cancellationToken);
            return Outcome;
        }
    }
}
