using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LifecycleTests
{
    private string root = null!;
    private string home = null!;
    private ManualClock clock = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        home = Path.Combine(root, "data");
        clock = new ManualClock();
        service = Create();
        service.Initialize();
        foreach (var agent in new[] { "agent-a", "agent-b" })
        {
            var config = Path.Combine(root, agent + ".toml");
            File.WriteAllText(config, "name = \"fixture\"");
            service.Register(agent, "fixture", config);
            service.ValidateAgent(agent);
        }
        service.SetPermission("agent-a", "agent-b", "allow");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, recursive: true);

    private RelayService Create(params IDeliveryAdapter[] adapters) => new(home, clock, adapters);
    private static DispatchRequest Request(string mode = "manual", string key = "dispatch-1", string adapter = "fixture") =>
        new("agent-a", "agent-b", "## Instructions\nReview the parser.", mode, adapter, key);
    private static void WriteResult(TaskRecord task) => File.WriteAllText(task.ResultPath, "## Result\nReviewed.\n## Verification\nUnit tests passed.");

    [TestMethod]
    public async Task DirectionalPermissionDoesNotBlockCompletionCallback()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.DispatchAsync(Request("auto") with { From = "agent-b", To = "agent-a" }));
        Assert.AreEqual("permission_denied", error.Code);
        WriteResult(task);
        var done = service.Complete(task.TaskId, task.AttemptId, "agent-b", "done", task.ResultPath);
        Assert.AreEqual(TaskState.Succeeded, done.Task.State);
    }

    [TestMethod]
    public async Task Repeating124CallsCreatesOneTaskExecutionAndNotification()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("accepted", "receipt-1", null));
        service = Create(adapter);
        var task = (await service.DispatchAsync(Request("auto"))).Task;
        for (var i = 0; i < 124; i++)
        {
            var replay = await Create(adapter).DispatchAsync(Request("auto"));
            Assert.AreEqual(task.TaskId, replay.Task.TaskId);
            service.Status(task.TaskId);
        }
        Assert.AreEqual(1, adapter.Calls);
        WriteResult(task);
        for (var i = 0; i < 124; i++) service.Complete(task.TaskId, task.AttemptId, "agent-b", "done", task.ResultPath);
        Assert.HasCount(1, service.Tasks());
        Assert.AreEqual(1, service.Audit(task.TaskId).Count(e => e.Event == "task.completed"));
        Assert.AreEqual(1, service.Inbox().Count(n => n.Event.EndsWith(":ready", StringComparison.Ordinal)));
        Assert.AreEqual(1, adapter.Calls);
    }

    [TestMethod]
    public async Task IdempotencyKeyCannotChangeBusinessInput()
    {
        await service.DispatchAsync(Request());
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.DispatchAsync(Request() with { Content = "Different work" }));
        Assert.AreEqual("idempotency_conflict", error.Code);
    }

    [TestMethod]
    public async Task FileRegistrationDoesNotCompleteTaskAndWrongSenderCannotComplete()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        WriteResult(task);
        var artifact = service.RegisterArtifact(task.TaskId, task.AttemptId, "result", task.ResultPath);
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
        var error = Assert.ThrowsExactly<RelayException>(() => service.Complete(task.TaskId, task.AttemptId, "agent-a", "done", artifact.Path));
        Assert.AreEqual("callback_denied", error.Code);
    }

    [TestMethod]
    public async Task ExplicitFailureSurvivesRestartAndDoesNotFabricateSuccess()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        File.WriteAllText(task.ResultPath, "## Failure\nCompiler failed.");
        service.Complete(task.TaskId, task.AttemptId, "agent-b", "fail", task.ResultPath, true, "compile_failed", "Compiler failed", true);
        var restored = Create().Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Failed, restored.State);
        Assert.AreEqual("compile_failed", restored.ErrorCode);
        var possible = restored.SideEffectsPossible ?? throw new AssertFailedException("Side effects were not recorded.");
        Assert.IsTrue(possible);
        Assert.IsTrue(File.Exists(restored.ResultPath));
    }

    [TestMethod]
    public async Task TimeoutIsReadOnlyUntilRecoveryAndValidLateCompletionIsAccepted()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        clock.Advance(TimeSpan.FromMinutes(31));
        var before = service.Audit(task.TaskId).Count;
        var view = service.Status(task.TaskId);
        Assert.IsTrue(view.DeadlineExceeded);
        Assert.AreEqual(TaskState.AwaitingManual, view.Task.State);
        Assert.HasCount(before, service.Audit(task.TaskId));
        await Create().RecoverAsync(task.TaskId);
        Assert.AreEqual(TaskState.TimedOut, service.Status(task.TaskId).Task.State);
        WriteResult(task);
        var done = service.Complete(task.TaskId, task.AttemptId, "agent-b", "late", task.ResultPath);
        Assert.AreEqual(TaskState.Succeeded, done.Task.State);
        Assert.AreEqual("late_completion", service.Audit(task.TaskId).Last().Detail);
    }

    [TestMethod]
    public async Task RetryRequiresApprovalAndRejectsPreviousAttemptCallbacks()
    {
        var old = (await service.DispatchAsync(Request())).Task;
        clock.Advance(TimeSpan.FromMinutes(31));
        await service.RecoverAsync();
        var denied = await Assert.ThrowsExactlyAsync<RelayException>(() => service.RetryAsync(old.TaskId, "Try again", false));
        Assert.AreEqual("approval_required", denied.Code);
        var next = (await service.RetryAsync(old.TaskId, "Try again", true)).Task;
        Assert.AreNotEqual(old.AttemptId, next.AttemptId);
        WriteResult(old);
        var error = Assert.ThrowsExactly<RelayException>(() => service.Complete(old.TaskId, old.AttemptId, "agent-b", "old", old.ResultPath));
        Assert.AreEqual("stale_attempt", error.Code);
    }

    [TestMethod]
    public async Task UnknownSubmissionNeverAutomaticallyRetries()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("unknown", null, "timeout"));
        service = Create(adapter);
        var task = (await service.DispatchAsync(Request("auto"))).Task;
        Assert.AreEqual(TaskState.DeliveryUnknown, task.State);
        await Create(adapter).RecoverAsync(task.TaskId);
        await Create(adapter).DispatchAsync(Request("auto"));
        Assert.AreEqual(1, adapter.Calls);
    }

    [TestMethod]
    public async Task ProvenRejectionIsBoundedToThreeSubmissionsAcrossRestart()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("rejected", null, "not submitted", true));
        service = Create(adapter);
        var task = (await service.DispatchAsync(Request("auto"))).Task;
        Assert.AreEqual(TaskState.Failed, task.State);
        await Create(adapter).RecoverAsync(task.TaskId);
        Assert.AreEqual(3, adapter.Calls);
        Assert.AreEqual(3, service.DeliveryEvidence(task.TaskId)!.Submissions);
    }

    [TestMethod]
    public async Task CrashDuringDeliveryRecoversToUnknownWithoutResending()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("accepted", "receipt", null)) { Crash = true };
        service = Create(adapter);
        await Assert.ThrowsExactlyAsync<IOException>(() => service.DispatchAsync(Request("auto")));
        var task = service.Tasks().Single().Task;
        clock.Advance(TimeSpan.FromSeconds(41));
        await Create(adapter).RecoverAsync(task.TaskId);
        Assert.AreEqual(TaskState.DeliveryUnknown, service.Status(task.TaskId).Task.State);
        Assert.AreEqual(1, adapter.Calls);
    }

    [TestMethod]
    public async Task ConcurrentDispatchClaimsOnlyOnce()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("accepted", "receipt", null)) { Delay = true };
        var first = Create(adapter).DispatchAsync(Request("auto"));
        var second = Create(adapter).DispatchAsync(Request("auto"));
        var results = await Task.WhenAll(first, second);
        Assert.AreEqual(results[0].Task.TaskId, results[1].Task.TaskId);
        Assert.AreEqual(1, adapter.Calls);
    }

    [TestMethod]
    public async Task ExpiredArtifactsAreRemovedWithoutTouchingExternalFiles()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        var external = Path.Combine(root, "external.md");
        File.WriteAllText(external, "Keep me");
        var invalid = Assert.ThrowsExactly<RelayException>(() => service.Complete(task.TaskId, task.AttemptId, "agent-b", "done", external));
        Assert.AreEqual("unmanaged_path", invalid.Code);
        clock.Advance(TimeSpan.FromDays(4));
        Assert.IsEmpty(service.Cleanup(false).Paths);
        Assert.AreEqual(0, service.Cleanup(true).Deleted);
        Assert.IsTrue(File.Exists(task.RequestPath));
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
        await service.RecoverAsync(task.TaskId);
        Assert.IsGreaterThan(0, service.Cleanup(false).Eligible);
        Assert.IsTrue(File.Exists(task.RequestPath));
        Assert.IsGreaterThan(0, service.Cleanup(true).Deleted);
        Assert.IsFalse(File.Exists(task.RequestPath));
        Assert.IsTrue(File.Exists(external));
        Assert.AreEqual(TaskState.TimedOut, service.Status(task.TaskId).Task.State);
        Assert.AreEqual(0, service.Cleanup(true).Deleted);
    }

    [TestMethod]
    public async Task CleaningPreviousAttemptCannotTimeoutNewAttempt()
    {
        var old = (await service.DispatchAsync(Request())).Task;
        clock.Advance(TimeSpan.FromDays(2));
        await service.RecoverAsync();
        var next = (await service.RetryAsync(old.TaskId, "restart", true)).Task;
        clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromSeconds(1)));
        service.Cleanup(true);
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(next.TaskId).Task.State);
        Assert.IsTrue(File.Exists(next.RequestPath));
    }

    [TestMethod]
    public async Task ConfirmModeCannotDeliverBeforeApproval()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("accepted", "receipt", null));
        service = Create(adapter);
        var task = (await service.DispatchAsync(Request("confirm"))).Task;
        Assert.AreEqual(TaskState.AwaitingConfirmation, task.State);
        Assert.AreEqual(0, adapter.Calls);
        Assert.AreEqual(TaskState.Dispatched, (await service.ApproveAsync(task.TaskId)).Task.State);
        Assert.AreEqual(1, adapter.Calls);
    }

    [TestMethod]
    public async Task InvalidRegisteredPathIsNotReplacedAutomatically()
    {
        var agent = service.Agents().Single(a => a.AgentId == "agent-b");
        File.Delete(agent.ConfigPath);
        Assert.AreEqual("invalid", service.ValidateAgent("agent-b").ValidationStatus);
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.DispatchAsync(Request("auto")));
        Assert.AreEqual("agent_invalid", error.Code);
        Assert.AreEqual(agent.ConfigPath, service.Agents().Single(a => a.AgentId == "agent-b").ConfigPath);
    }

    [TestMethod]
    public async Task CancelledTaskRetainsLateEvidenceWithoutChangingState()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        service.Cancel(task.TaskId, "Stop waiting");
        WriteResult(task);
        var late = service.Complete(task.TaskId, task.AttemptId, "agent-b", "late", task.ResultPath);
        Assert.AreEqual(TaskState.Cancelled, late.Task.State);
        Assert.AreEqual("task.late_result", service.Audit(task.TaskId).Last().Event);
    }

    [TestMethod]
    public async Task ResultSubmissionAndAcceptedSnapshotBothFollowTtl()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        WriteResult(task);
        var done = service.Complete(task.TaskId, task.AttemptId, "agent-b", "done", task.ResultPath);
        clock.Advance(TimeSpan.FromDays(4));
        service.Cleanup(true);
        Assert.IsFalse(File.Exists(task.ResultPath));
        Assert.IsFalse(File.Exists(done.Task.ResultPath));
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
        StringAssert.Contains(service.Status(task.TaskId).Prompt, "已过期或缺失");
    }

    [TestMethod]
    public async Task AnOldUnregisteredFileHasUnverifiedOwnershipAndIsPreserved()
    {
        var task = (await service.DispatchAsync(Request())).Task;
        var orphan = Path.Combine(Path.GetDirectoryName(task.RequestPath)!, "interrupted.tmp");
        File.WriteAllText(orphan, "interrupted publication");
        clock.Advance(new DateTimeOffset(File.GetCreationTimeUtc(orphan)).AddDays(4) - clock.GetUtcNow());
        var file = service.WorkspaceFiles().Single(entry => entry.Path == orphan);
        Assert.AreEqual(WorkspaceFileOwner.Unknown, file.Owner);
        Assert.AreEqual(WorkspaceFileStatus.Unverified, file.Status);
        CollectionAssert.DoesNotContain(service.Cleanup(false).Paths, orphan);
        service.Cleanup(true);
        Assert.IsTrue(File.Exists(orphan));
    }

    [TestMethod]
    public async Task FastCompletionBeforeDeliveryReturnsDoesNotGetOverwritten()
    {
        var adapter = new FixtureAdapter(new DeliveryResult("accepted", "receipt", null));
        service = Create(adapter);
        adapter.OnDelivery = task =>
        {
            WriteResult(task);
            service.Complete(task.TaskId, task.AttemptId, "agent-b", "fast-done", task.ResultPath);
        };
        var done = await service.DispatchAsync(Request("auto"));
        Assert.AreEqual(TaskState.Succeeded, done.Task.State);
        Assert.AreEqual("settled", service.DeliveryEvidence(done.Task.TaskId)!.State);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class FixtureAdapter(DeliveryResult result) : IDeliveryAdapter
    {
        public string Id => "fixture";
        public int Calls;
        public bool Crash { get; init; }
        public bool Delay { get; init; }
        public Action<TaskRecord>? OnDelivery { get; set; }
        public async Task<DeliveryResult> DeliverAsync(TaskRecord task, string home, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Crash) throw new IOException("simulated crash after submission");
            if (Delay) await Task.Delay(100, cancellationToken);
            OnDelivery?.Invoke(task);
            return result;
        }
    }
}
