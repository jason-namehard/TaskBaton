using AgentRelay.Core;
using System.Text.Json;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class RelayRelocationTests
{
    private string root = null!;
    private string home = null!;
    private string target = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.RelocationTests", Guid.NewGuid().ToString());
        home = Path.Combine(root, "source");
        target = Path.Combine(root, "target");
        service = new RelayService(home);
        service.Initialize();
        var config = Path.Combine(home, "profiles", "agent.json");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "{}");
        service.Register("a", "fixture", config);
        service.RegisterManual("b", "fixture", "My chosen name");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    [TestMethod]
    public async Task ActiveTaskBlocksMigrationWithoutCreatingDestination()
    {
        await service.DispatchAsync(new("a", "b", "Review this", "manual", "manual", "active"));
        var error = Assert.ThrowsExactly<RelayException>(() => service.Relocate(target));
        Assert.AreEqual("relocation_active_tasks", error.Code);
        Assert.IsFalse(Directory.Exists(target));
        Assert.HasCount(2, service.Agents());
    }

    [TestMethod]
    public async Task CompletedTaskAndManagedPathsMoveWhileOriginalSourceIsRetained()
    {
        var request = new DispatchRequest("a", "b", "Review this", "manual", "manual", "completed");
        var task = (await service.DispatchAsync(request)).Task;
        File.WriteAllText(task.ResultPath, "## Result\nDone.\n## Verification\nChecked.");
        service.Complete(task.TaskId, task.AttemptId, "b", "done", task.ResultPath);

        var moved = service.Relocate(target);
        var relocated = moved.Status(task.TaskId).Task;
        Assert.AreEqual(target, moved.Home);
        Assert.IsTrue(relocated.RequestPath.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(relocated.RequestPath));
        Assert.IsTrue(File.Exists(relocated.ResultPath));
        Assert.IsTrue(File.Exists(task.RequestPath));
        Assert.AreEqual(Path.Combine(target, "profiles", "agent.json"), moved.Agents().Single(a => a.AgentId == "a").ConfigPath);
        Assert.AreEqual("My chosen name", moved.Agents().Single(a => a.AgentId == "b").DisplayName);
        Assert.AreEqual(relocated.ResultPath, (await moved.DispatchAsync(request)).Task.ResultPath);
        var error = Assert.ThrowsExactly<RelayException>(() => service.Settings());
        Assert.AreEqual("relocation_moved", error.Code);
        StringAssert.Contains(error.Message, target);
    }

    [TestMethod]
    public void FailedCopyKeepsSourceUsableAndVerificationRetriesCurrentSource()
    {
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "relay.relocation.incomplete.json"), RelayJson.Write(new RelayRelocationRequest(home, target)));
        // A file occupies a destination directory needed by the copy.
        var obstruction = Path.Combine(target, "profiles");
        File.WriteAllText(obstruction, "occupied");
        var error = Assert.ThrowsExactly<RelayException>(() => service.Relocate(target));
        Assert.AreEqual("relocation_failed", error.Code);
        Assert.HasCount(2, service.Agents());
        Assert.AreEqual("relocation_incomplete", Assert.ThrowsExactly<RelayException>(() => new RelayService(target).Initialize()).Code);

        File.Delete(obstruction);
        File.WriteAllText(Path.Combine(target, "obsolete.txt"), "left by previous copy");
        File.WriteAllText(Path.Combine(home, "new.txt"), "current source");
        var moved = service.VerifyRelocation();
        Assert.AreEqual(target, moved.Home);
        Assert.IsFalse(File.Exists(Path.Combine(target, "obsolete.txt")));
        Assert.AreEqual("current source", File.ReadAllText(Path.Combine(target, "new.txt")));
        Assert.HasCount(2, moved.Agents());
    }

    [TestMethod]
    public void NonemptyAndNestedDestinationsAreRejected()
    {
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "user.txt"), "keep");
        Assert.AreEqual("relocation_target_not_empty", Assert.ThrowsExactly<RelayException>(() => service.Relocate(target)).Code);
        Assert.AreEqual("keep", File.ReadAllText(Path.Combine(target, "user.txt")));
        Assert.AreEqual("relocation_target_invalid", Assert.ThrowsExactly<RelayException>(() => service.Relocate(Path.Combine(home, "nested"))).Code);
        Assert.AreEqual("relocation_no_pending", Assert.ThrowsExactly<RelayException>(() => service.VerifyRelocation()).Code);
    }

    [TestMethod]
    public async Task VerificationWithoutPendingMigrationDoesNotReportSuccessForActiveTasks()
    {
        var task = (await service.DispatchAsync(new("a", "b", "Still waiting", "manual", "manual", "pending"))).Task;
        Assert.AreEqual(TaskState.AwaitingManual, task.State);
        var error = Assert.ThrowsExactly<RelayException>(() => service.VerifyRelocation());
        Assert.AreEqual("relocation_no_pending", error.Code);
        StringAssert.Contains(error.Message, home);
        Assert.AreEqual("relocation_same_directory", Assert.ThrowsExactly<RelayException>(() => service.Relocate(home)).Code);
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public async Task CompletedHistoryExpiresOnlyInTheCurrentRelayAndPreservedBackupIsNotCleaned()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var task = (await service.DispatchAsync(new("a", "b", "Archive this", "manual", "manual", "retention"))).Task;
        File.WriteAllText(task.ResultPath, "Done.");
        var completed = service.Complete(task.TaskId, task.AttemptId, "b", "completed", task.ResultPath).Task;
        var moved = service.Relocate(target);
        var relocated = moved.Status(task.TaskId).Task;
        clock.UtcNow = clock.UtcNow.AddDays(4);
        Assert.IsGreaterThan(0, moved.Cleanup(true).Deleted);
        Assert.IsFalse(File.Exists(relocated.RequestPath));
        Assert.IsFalse(File.Exists(relocated.ResultPath));
        Assert.IsTrue(File.Exists(completed.RequestPath));
        Assert.IsTrue(File.Exists(completed.ResultPath));
    }

    private sealed class RelocationClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [TestMethod]
    public void ManualFileFollowupBlocksMigrationAndCompletedHistoryRetainsExternalSource()
    {
        var original = Path.Combine(root, "handoff.zip");
        File.WriteAllBytes(original, [1, 2, 3, 4]);
        var task = service.CreateFileHandoff(new("a", "b", original, "Review file")).Task;
        Assert.AreEqual("relocation_active_tasks", Assert.ThrowsExactly<RelayException>(() => service.Relocate(target)).Code);
        var reply = Path.Combine(root, "reply.md"); File.WriteAllText(reply, "Choose option 2.");
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId, "b", 0, "done", true, reply, "确认第二个选项")));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
        Assert.AreEqual(TaskState.AwaitingReply, service.Status(task.TaskId).Task.State);
        Assert.AreEqual("relocation_active_tasks", Assert.ThrowsExactly<RelayException>(() => service.Relocate(target)).Code);
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId, "a", 1, "done", false)));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);

        var moved = service.Relocate(target);
        var history = moved.Status(task.TaskId).Task;
        Assert.AreEqual(TaskState.Succeeded, history.State);
        Assert.AreEqual(original, history.ManualSourceFile);
        Assert.IsTrue(history.ManualCurrentFile!.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.IsTrue(File.Exists(history.ManualCurrentFile));
        Assert.IsTrue(File.Exists(original));
        Assert.IsTrue(File.Exists(reply));
    }

    [TestMethod]
    public async Task QueuedMoveWaitsForActiveTaskAndPersistsUntilItCanComplete()
    {
        var task = (await service.DispatchAsync(new("a", "b", "Work", "manual", "manual", "queued"))).Task;
        service.QueueRelocation(target);
        Assert.AreEqual(new RelayRelocationRequest(home, target), new RelayService(home).PendingRelocation());
        Assert.IsNull(service.TryCompletePendingRelocation());
        Assert.IsFalse(Directory.Exists(target));

        service.Cancel(task.TaskId, "No more work");
        var moved = service.TryCompletePendingRelocation();
        Assert.IsNotNull(moved);
        Assert.AreEqual(target, moved.Home);
        Assert.AreEqual(target, RelayService.ResolveRelocatedHome(home));
        Assert.IsNull(moved.PendingRelocation());
        Assert.AreEqual(TaskState.Cancelled, moved.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public async Task CancelledTaskWaitsForItsCliSendBeforeMoving()
    {
        var started = Path.Combine(root, "started");
        var release = Path.Combine(root, "release");
        var command = new CliCommand("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
            "[IO.File]::WriteAllText($env:RELAY_STARTED,'started');" +
            "while(!(Test-Path -LiteralPath $env:RELAY_RELEASE)){Start-Sleep -Milliseconds 20};" +
            "@{session_id='test-session';reply='done';accepted=$true}|ConvertTo-Json -Compress"],
            "{prompt}", null, new() { ["RELAY_STARTED"] = started, ["RELAY_RELEASE"] = release });
        service.ImportCliRecipe("b", RelayJson.Write(new CliRecipe(1, "fixture", "fixture", command, null, [], "", "", "", "")));
        service.SetPermission("a", "b", "allow");
        var file = Path.Combine(root, "send.txt"); File.WriteAllText(file, "work");
        var task = service.CreateFileHandoff(new("a", "b", file, "Review")).Task;
        var send = service.SendFileHandoffAsync(task.TaskId, "Read the file.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(started)) await Task.Delay(20, timeout.Token);
            service.Cancel(task.TaskId, "Stop waiting");
            service.QueueRelocation(target);
            Assert.IsNull(service.TryCompletePendingRelocation());
            Assert.IsFalse(Directory.Exists(target));
        }
        finally { File.WriteAllText(release, "continue"); }
        var finished = await send;
        Assert.AreEqual(TaskState.Cancelled, finished.Task.State);
        Assert.AreEqual("submitted", finished.Task.CliFileStatus);
        var moved = service.TryCompletePendingRelocation();
        Assert.IsNotNull(moved);
        Assert.AreEqual(TaskState.Cancelled, moved.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void InterruptedCancelledSendEventuallyStopsBlockingMove()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var file = Path.Combine(root, "work.txt"); File.WriteAllText(file, "work");
        var task = service.CreateFileHandoff(new("a", "b", file)).Task;
        new RelayStore(home).Write(s =>
        {
            var current = s.Task(task.TaskId);
            current.CliFileTurn = current.ManualTurn;
            current.CliFileStatus = "submitting";
            current.DeadlineAt = clock.UtcNow.AddMinutes(-1);
            s.Save(current);
            s.Event(current, "file.cli_submitting", "operator", clock.UtcNow, "create");
            return true;
        });
        service.Cancel(task.TaskId, "Stop waiting");
        service.QueueRelocation(target);
        Assert.IsNull(service.TryCompletePendingRelocation());
        clock.UtcNow = clock.UtcNow.AddSeconds(59);
        Assert.IsNull(service.TryCompletePendingRelocation());
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        Assert.IsNotNull(service.TryCompletePendingRelocation());
    }

    [TestMethod]
    public void CleanupWaitsTwentyFourHoursAndRemovesClosedSqliteFiles()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        File.WriteAllText(Path.Combine(home, "temporary.txt"), "migrated");
        var moved = service.Relocate(target);
        var plan = moved.RelocationCleanupPlans().Single();
        Assert.AreEqual(clock.UtcNow.AddHours(24), plan.DeleteAfter);
        clock.UtcNow = plan.DeleteAfter.AddTicks(-1);
        Assert.HasCount(1, moved.CleanupRelocatedHomes());
        Assert.IsTrue(Directory.Exists(home));

        clock.UtcNow = plan.DeleteAfter;
        Assert.IsEmpty(moved.CleanupRelocatedHomes());
        Assert.IsFalse(Directory.Exists(home));
        Assert.AreEqual("migrated", File.ReadAllText(Path.Combine(target, "temporary.txt")));
        Assert.HasCount(2, moved.Agents());
    }

    [TestMethod]
    public void CleanupKeepsAndReportsChangedAndNewFiles()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var changed = Path.Combine(home, "changed.txt");
        var unchanged = Path.Combine(home, "unchanged.txt");
        File.WriteAllText(changed, "before");
        File.WriteAllText(unchanged, "remove");
        var moved = service.Relocate(target);
        File.WriteAllText(changed, "after!");
        var added = Path.Combine(home, "new.txt");
        File.WriteAllText(added, "keep");
        clock.UtcNow = clock.UtcNow.AddHours(24);

        var status = moved.CleanupRelocatedHomes().Single();
        Assert.IsTrue(status.Attempted);
        CollectionAssert.Contains(status.ModifiedFiles, changed);
        CollectionAssert.Contains(status.AddedEntries, added);
        Assert.AreEqual("after!", File.ReadAllText(changed));
        Assert.AreEqual("keep", File.ReadAllText(added));
        Assert.IsFalse(File.Exists(unchanged));
        Assert.IsFalse(File.Exists(Path.Combine(home, "relay.db")));
        File.Delete(changed); File.Delete(added);
        Assert.IsEmpty(moved.CleanupRelocatedHomes());
        Assert.IsFalse(Directory.Exists(home));
    }

    [TestMethod]
    public void CleanupRejectsPathsOutsideTheOldStation()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(outside, "keep");
        var moved = service.Relocate(target);
        var path = Path.Combine(target, "relay.relocation.cleanup.json");
        var plans = RelayJson.Read<RelocationCleanupPlan[]>(File.ReadAllText(path));
        plans[0] = plans[0] with { Files = [new RelocationFileSnapshot("../outside.txt", 4, "irrelevant"), .. plans[0].Files] };
        File.WriteAllText(path, RelayJson.Write(plans));
        clock.UtcNow = clock.UtcNow.AddHours(24);
        Assert.AreEqual("relocation_cleanup_invalid", Assert.ThrowsExactly<RelayException>(() => moved.CleanupRelocatedHomes()).Code);
        Assert.AreEqual("keep", File.ReadAllText(outside));
        Assert.IsTrue(File.Exists(Path.Combine(home, "relay.db")));
    }

    [TestMethod]
    public void ConsecutiveMovesCarryCleanupPlansAndRecoverFromPartiallyCleanedSource()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var oldFile = Path.Combine(home, "old.txt"); File.WriteAllText(oldFile, "old");
        var moved = service.Relocate(target);
        clock.UtcNow = clock.UtcNow.AddHours(1);
        var third = Path.Combine(root, "third");
        var movedAgain = moved.Relocate(third);
        Assert.AreEqual(third, RelayService.ResolveRelocatedHome(home));
        Assert.HasCount(2, movedAgain.RelocationCleanupPlans());
        File.Delete(oldFile); // A previous cleanup may have stopped after deleting a file.
        clock.UtcNow = clock.UtcNow.AddHours(23);
        var remaining = movedAgain.CleanupRelocatedHomes();
        Assert.IsFalse(Directory.Exists(home));
        Assert.HasCount(1, remaining);
        Assert.IsTrue(Directory.Exists(target));
        clock.UtcNow = clock.UtcNow.AddHours(1);
        Assert.IsEmpty(movedAgain.CleanupRelocatedHomes());
        Assert.IsFalse(Directory.Exists(target));
        Assert.HasCount(2, movedAgain.Agents());
    }

    [TestMethod]
    public void MoveCanReuseAnOldFolderAfterItWasRemovedManually()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var moved = service.Relocate(target);
        Assert.AreEqual("relocation_target_invalid", Assert.ThrowsExactly<RelayException>(
            () => moved.QueueRelocation(Path.Combine(home, "nested"))).Code);
        Directory.Delete(home, true);
        var movedBack = moved.Relocate(home);
        var plan = movedBack.RelocationCleanupPlans().Single();
        Assert.AreEqual(target, plan.Source);
        clock.UtcNow = clock.UtcNow.AddHours(24);
        Assert.IsEmpty(movedBack.CleanupRelocatedHomes());
        Assert.IsFalse(Directory.Exists(target));
        Assert.HasCount(2, movedBack.Agents());
    }

    [TestMethod]
    public void CleanupResumesWhenOnlyAnEmptyOldDirectoryRemains()
    {
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var moved = service.Relocate(target);
        Directory.Delete(home, true);
        Directory.CreateDirectory(home);
        clock.UtcNow = clock.UtcNow.AddHours(24);
        Assert.IsEmpty(moved.CleanupRelocatedHomes());
        Assert.IsFalse(Directory.Exists(home));
    }

    [TestMethod]
    public void CleanupRetriesAfterAnOldFileIsReleased()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing is required for this test.");
        var clock = new RelocationClock();
        service = new RelayService(home, clock);
        var file = Path.Combine(home, "locked.txt"); File.WriteAllText(file, "migrated");
        var moved = service.Relocate(target);
        clock.UtcNow = clock.UtcNow.AddHours(24);
        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.AreEqual("relocation_cleanup_failed", Assert.ThrowsExactly<RelayException>(() => moved.CleanupRelocatedHomes()).Code);
            Assert.IsTrue(moved.RelocationCleanupPlans().Single().Attempted);
        }
        Assert.IsEmpty(moved.CleanupRelocatedHomes());
        Assert.IsFalse(Directory.Exists(home));
    }

    [TestMethod]
    public void MoveUpdatesManagedContextRecipeAndSessionPathsWithoutChangingUserText()
    {
        var sourceFile = Path.Combine(root, "request.txt"); File.WriteAllText(sourceFile, "Original " + home);
        var task = service.CreateFileHandoff(new("a", "b", sourceFile, "Keep this text: " + home),
            new OriginSession("fixture", "source", "profile", Path.Combine(home, "project"))).Task;
        service.BindManualTaskSession(task.TaskId, "b", "destination", Path.Combine(home, "project"), "profile", Path.Combine(home, "sessions", "one"));
        service.ConfigureSessionDiscovery("b", "hermes", Path.Combine(home, "sessions"));
        var command = new CliCommand(Path.Combine(home, "agent.exe"),
            ["--home", home, "--home=" + Path.Combine(home, "nested"), "--label", "Keep this text: " + home, "{prompt}"],
            "Keep " + home + " {prompt}", Path.Combine(home, "project"),
            new() { ["RELAY_HOME"] = home, ["LABEL"] = "Keep " + home });
        service.ImportCliRecipe("b", RelayJson.Write(new CliRecipe(1, "fixture", "fixture", command, null, [], "", "", "", "", "text")));
        var originalContext = Path.Combine(Path.GetDirectoryName(service.ManualReceiptPath(task))!, "context.json");
        service.FileHandoffPrompt(task);
        new RelayStore(home).Write(s =>
        {
            var current = s.Task(task.TaskId);
            current.CliNewSessionCwd = Path.Combine(home, "project");
            current.Collaboration = new CollaborationRecord
            {
                AuthorId = "b", ReviewerId = "a", Phase = "implement", SubmissionPath = current.RequestPath,
                ApprovedSubmissionPath = current.RequestPath,
                History = [new CollaborationStep(0, "b", "implement", "submitted", DateTimeOffset.UtcNow, current.RequestPath)]
            };
            s.Save(current);
            var cached = new SessionDiscoveryResult("b", "hermes", true,
                [new SessionRecord("cached", "title", Path.Combine(home, "project"), "profile", Path.Combine(home, "sessions", "cached"), "unknown", null)],
                DateTimeOffset.UtcNow, false);
            s.Execute("INSERT INTO session_discovery_cache VALUES($id,$json)", ("$id", "b"), ("$json", RelayJson.Write(cached)));
            return true;
        });
        service.Cancel(task.TaskId, "Finished");

        var moved = service.Relocate(target);
        var relocated = moved.Status(task.TaskId).Task;
        Assert.AreEqual(Path.Combine(target, "project"), relocated.OriginSession!.Cwd);
        Assert.AreEqual(Path.Combine(target, "project"), relocated.CliNewSessionCwd);
        Assert.AreEqual(relocated.RequestPath, relocated.Collaboration!.SubmissionPath);
        Assert.AreEqual(relocated.RequestPath, relocated.Collaboration.ApprovedSubmissionPath);
        Assert.AreEqual(relocated.RequestPath, relocated.Collaboration.History.Single().ResultPath);
        Assert.AreEqual("Keep this text: " + home, relocated.ManualNextAction);
        var binding = moved.SessionBindings(task.TaskId).Single(item => item.AgentId == "b");
        Assert.AreEqual(Path.Combine(target, "project"), binding.Cwd);
        Assert.AreEqual(Path.Combine(target, "sessions", "one"), binding.SessionPath);
        Assert.AreEqual(Path.Combine(target, "sessions"), moved.DiscoverySource("b")!.StorageRoot);
        Assert.AreEqual(Path.Combine(target, "sessions", "cached"), moved.CachedSessions("b")!.Sessions.Single().SessionPath);
        var movedCommand = moved.CliExtension("b")!.Recipe.Create;
        Assert.AreEqual(Path.Combine(target, "agent.exe"), movedCommand.Executable);
        Assert.AreEqual(target, movedCommand.Arguments[1]);
        Assert.AreEqual("--home=" + Path.Combine(target, "nested"), movedCommand.Arguments[2]);
        Assert.AreEqual("Keep this text: " + home, movedCommand.Arguments[4]);
        Assert.AreEqual("Keep " + home + " {prompt}", movedCommand.Input);
        Assert.AreEqual(target, movedCommand.Environment!["RELAY_HOME"]);
        Assert.AreEqual("Keep " + home, movedCommand.Environment["LABEL"]);
        var movedContext = originalContext.Replace(home, target, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(movedContext));
        Assert.AreEqual(relocated.RequestPath, document.RootElement.GetProperty("current_file").GetString());
        Assert.AreEqual("Keep this text: " + home, document.RootElement.GetProperty("request").GetString());
        StringAssert.StartsWith(document.RootElement.GetProperty("receipt_file").GetString()!, target);
        StringAssert.Contains(moved.FileHandoffPrompt(relocated), target);
        Assert.AreEqual("Original " + home, File.ReadAllText(relocated.RequestPath));
    }
}
