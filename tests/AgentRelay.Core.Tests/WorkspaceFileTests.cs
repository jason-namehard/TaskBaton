using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class WorkspaceFileTests
{
    private readonly TestClock clock = new();
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay"), clock);
        service.Initialize();
        service.RegisterManual("a", "fixture", "A");
        service.RegisterManual("b", "fixture", "B");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private TaskRecord Create(string name)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, "Work request.");
        return service.CreateFileHandoff(new("a", "b", path)).Task;
    }

    private WorkspaceFileEntry[] Preview() => service.WorkspaceFiles()
        .Where(file => file.Status == WorkspaceFileStatus.Eligible).ToArray();

    [TestMethod]
    public void InventorySeparatesSnapshotsUserFilesAndTaskControlFiles()
    {
        var task = Create("source.md");
        service.FileHandoffPrompt(task);
        var files = service.WorkspaceFiles().ToDictionary(file => file.Path);
        var copy = files[task.RequestPath];
        Assert.AreEqual(WorkspaceFileOwner.Relay, copy.Owner);
        Assert.AreEqual(WorkspaceFileStatus.InUse, copy.Status);
        Assert.AreEqual(task.TaskId, copy.OwnerTaskId);
        Assert.IsTrue(copy.References.Any(reference => reference.TaskId == task.TaskId && reference.Active));
        var source = files[task.ManualSourceFile!];
        Assert.AreEqual(WorkspaceFileOwner.User, source.Owner);
        Assert.AreEqual("source", source.References.Single().Use);
        var context = files[service.ManualContextPath(task)];
        Assert.AreEqual(WorkspaceFileOwner.TaskControl, context.Owner);
        Assert.AreEqual(task.TaskId, context.References.Single().TaskId);
    }

    [TestMethod]
    public void ApplyingAPreviewKeepsNewlyEligibleChangedAndNewlyReferencedFiles()
    {
        var removable = Create("remove.md");
        var edited = Create("edited.md");
        var reused = Create("reused.md");
        foreach (var task in new[] { removable, edited, reused }) service.Cancel(task.TaskId, "Finish the fixture");
        clock.UtcNow = clock.UtcNow.AddDays(2);
        var later = Create("later.md");
        service.Cancel(later.TaskId, "Finish the fixture");
        clock.UtcNow = clock.UtcNow.AddDays(2);

        var preview = Preview();
        CollectionAssert.AreEquivalent(new[] { removable.RequestPath, edited.RequestPath, reused.RequestPath },
            preview.Select(file => file.Path).ToArray());
        Assert.IsTrue(preview.All(file => File.Exists(file.Path)));

        File.AppendAllText(edited.RequestPath, "Changed after preview.");
        var newTask = service.CreateFileHandoff(new("a", "b", reused.RequestPath)).Task;
        clock.UtcNow = clock.UtcNow.AddDays(2);
        var result = service.ApplyWorkspaceCleanup(preview);
        Assert.AreEqual(1, result.Deleted);
        CollectionAssert.AreEquivalent(new[] { edited.RequestPath, reused.RequestPath }, result.Kept!.Select(item => item.Path).ToArray());
        Assert.IsFalse(File.Exists(removable.RequestPath));
        Assert.IsTrue(File.Exists(edited.RequestPath));
        Assert.IsTrue(File.Exists(reused.RequestPath));
        Assert.IsTrue(File.Exists(later.RequestPath));
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(newTask.TaskId).Task.State);
        Assert.AreEqual("expired", service.Artifacts(removable.TaskId).Single().State);
        Assert.AreEqual(0, service.ApplyWorkspaceCleanup(preview).Deleted);
    }

    [TestMethod]
    public void LegacySourceInsideWorkspaceAndDeclaredProjectResultRemainProtected()
    {
        var path = Path.Combine(service.Home, "workspace", "project-source.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "Original project file.");
        var task = service.CreateFileHandoff(new("a", "b", path)).Task;
        var resultPath = service.TurnReplyPath(task);
        File.WriteAllText(resultPath, "A permanent result.");
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId,
            task.AttemptId, "b", 0, "done", false, resultPath, Deliverable: resultPath)));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);

        var original = service.Artifacts(task.TaskId).Single(artifact => artifact.Kind == "request");
        new RelayStore(service.Home).Write(s =>
        {
            s.Execute("UPDATE artifacts SET json=$json WHERE id=$id",
                ("$json", RelayJson.Write(original with { RelayOwned = null })), ("$id", original.ArtifactId));
            return true;
        });
        clock.UtcNow = clock.UtcNow.AddDays(4);
        var files = service.WorkspaceFiles().ToDictionary(file => file.Path);
        Assert.AreEqual(WorkspaceFileOwner.User, files[path].Owner);
        Assert.AreEqual(WorkspaceFileOwner.User, files[resultPath].Owner);
        Assert.IsTrue(files[resultPath].References.Any(reference => reference.Use == "deliverable"));
        service.DeleteTask(task.TaskId, true);
        Assert.AreEqual(0, service.Cleanup(true).Deleted);
        Assert.IsTrue(File.Exists(path));
        Assert.IsTrue(File.Exists(resultPath));
    }

    [TestMethod]
    public void DeletingAnOldTaskRetainsEvidenceNeededToCleanItsOwnedCopy()
    {
        var task = Create("legacy-copy.md");
        var artifact = service.Artifacts(task.TaskId).Single();
        new RelayStore(service.Home).Write(s =>
        {
            s.Execute("UPDATE artifacts SET json=$json WHERE id=$id",
                ("$json", RelayJson.Write(artifact with { RelayOwned = null })), ("$id", artifact.ArtifactId));
            return true;
        });
        service.DeleteTask(task.TaskId, true);
        clock.UtcNow = clock.UtcNow.AddDays(4);
        Assert.AreEqual(task.RequestPath, Preview().Single().Path);
        Assert.AreEqual(1, service.Cleanup(true).Deleted);
        Assert.IsFalse(File.Exists(task.RequestPath));
        Assert.IsTrue(File.Exists(task.ManualSourceFile));
        Assert.IsEmpty(service.Tasks());
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
