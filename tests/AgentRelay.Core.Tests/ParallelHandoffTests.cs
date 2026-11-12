using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class ParallelHandoffTests
{
    private string root = null!;
    private string project = null!;
    private RelayService service = null!;
    private readonly TestClock clock = new();

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        project = Path.Combine(root, "project");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "shared.txt"), "Original");
        File.WriteAllText(Path.Combine(project, "retire.txt"), "Retire");
        service = new RelayService(Path.Combine(root, "station"), clock);
        service.Initialize();
        foreach (var agent in new[] { "sender", "worker", "reviewer", "other" })
            service.RegisterManual(agent, "fixture", agent);
    }

    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    private ParallelHandoffDraft Draft(params ParallelWork[] work) =>
        new("Parallel fixture", "sender", "reviewer", project, ["shared.txt", "retire.txt"], work, "Reconcile the proposed changes.");

    private void Finish(ParallelMember member, bool success)
    {
        var task = service.Status(member.TaskId).Task;
        var report = success ? service.TurnReplyPath(task) : null;
        if (report is not null) File.WriteAllText(report, "Report for " + member.Instructions);
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(new ManualReceipt(task.TaskId, task.AttemptId,
            member.AgentId, 0, success ? "done" : "failed", false, report, success ? null : "Fixture failed")));
        var imported = service.ImportManualReceipts();
        Assert.IsEmpty(imported.Errors);
    }

    [TestMethod]
    public void PartialMergeKeepsBothVersionsAndTheCurrentProjectWithTheirOwners()
    {
        var shared = new FileInfo(Path.Combine(project, "shared.txt")) { IsReadOnly = true };
        ParallelReview group;
        try
        {
            group = service.CreateParallelHandoff(Draft(new("worker", "A"), new("worker", "B"), new("other", "C")));
            Assert.IsTrue(new FileInfo(shared.FullName).IsReadOnly);
            Assert.IsTrue(group.Workers.All(worker => !new FileInfo(Path.Combine(worker.WorkingDirectory, "shared.txt")).IsReadOnly));
        }
        finally { shared.IsReadOnly = false; }
        var a = group.Workers[0];
        var b = group.Workers[1];
        var c = group.Workers[2];
        Assert.AreNotEqual(a.WorkingDirectory, b.WorkingDirectory);
        Assert.AreEqual("Original", File.ReadAllText(Path.Combine(b.WorkingDirectory, "shared.txt")));
        File.WriteAllText(Path.Combine(a.WorkingDirectory, "shared.txt"), "A");
        File.Delete(Path.Combine(a.WorkingDirectory, "retire.txt"));
        File.WriteAllText(Path.Combine(b.WorkingDirectory, "shared.txt"), "B");
        File.WriteAllText(Path.Combine(c.WorkingDirectory, "discard.txt"), "Failed work");
        Finish(a, true);
        var incomplete = service.ReviewParallelHandoff(group.GroupId);
        Assert.AreEqual("parallel_not_ready", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateParallelMerge(incomplete, true, true)).Code);
        Finish(b, true);
        Finish(c, false);

        service = new RelayService(service.Home, clock);
        var before = service.ReviewParallelHandoff(group.GroupId);
        Assert.IsTrue(before.CanMerge && before.Partial);
        Assert.AreEqual(TaskState.Failed, before.Workers[2].State);
        Assert.AreEqual("Fixture failed", before.Workers[2].Detail);
        CollectionAssert.AreEqual(new[] { "shared.txt" }, before.SharedConflicts);
        Assert.IsTrue(before.Workers[0].Changes.Any(change => change is { Path: "retire.txt", Kind: "deleted", Snapshot: null }));
        var aSnapshot = before.Workers[0].Changes.Single(change => change.Path == "shared.txt").Snapshot!;
        File.WriteAllText(Path.Combine(a.WorkingDirectory, "shared.txt"), "Later edit");
        Assert.AreEqual("A", File.ReadAllText(aSnapshot));
        File.WriteAllText(shared.FullName, "Edited by user");
        Assert.AreEqual("parallel_review_changed", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateParallelMerge(before, true, true)).Code);

        var review = service.ReviewParallelHandoff(group.GroupId);
        CollectionAssert.AreEqual(new[] { "shared.txt" }, review.ProjectConflicts);
        Assert.AreEqual("parallel_partial_approval", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateParallelMerge(review, false, true)).Code);
        Assert.AreEqual("parallel_conflict_approval", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateParallelMerge(review, true, false)).Code);
        var merge = service.CreateParallelMerge(review, true, true).Task;
        Assert.AreEqual(merge.TaskId, service.CreateParallelMerge(review, true, true).Task.TaskId);
        Assert.AreEqual("reviewer", merge.RecipientId);
        service.FileHandoffPrompt(merge);
        using var context = JsonDocument.Parse(File.ReadAllText(service.ManualContextPath(merge)));
        var manifestPath = context.RootElement.GetProperty("parallel").GetProperty("merge_manifest").GetString()!;
        var folder = Path.GetDirectoryName(manifestPath)!;
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var workers = manifest.RootElement.GetProperty("workers");
        Assert.AreEqual(a.TaskId, workers[0].GetProperty("task_id").GetString());
        Assert.AreEqual("worker", workers[0].GetProperty("agent_id").GetString());
        Assert.AreEqual("A", File.ReadAllText(Path.Combine(folder, a.TaskId, "files", "shared.txt")));
        Assert.AreEqual("B", File.ReadAllText(Path.Combine(folder, b.TaskId, "files", "shared.txt")));
        Assert.AreEqual("Edited by user", File.ReadAllText(Path.Combine(folder, "project-current", "shared.txt")));
        Assert.AreEqual("failed", workers[2].GetProperty("state").GetString());
        Assert.AreEqual(JsonValueKind.Null, workers[2].GetProperty("report").ValueKind);

        var mergeMember = service.ReviewParallelHandoff(group.GroupId).Merge!;
        File.WriteAllText(Path.Combine(mergeMember.WorkingDirectory, "shared.txt"), "Reviewed A and B");
        Finish(mergeMember, true);
        Assert.AreEqual("Edited by user", File.ReadAllText(shared.FullName));
        Assert.AreEqual("Retire", File.ReadAllText(Path.Combine(project, "retire.txt")));
        var final = service.ReviewParallelHandoff(group.GroupId).Merge!;
        Assert.AreEqual("Reviewed A and B", File.ReadAllText(final.Changes.Single().Snapshot!));

        clock.UtcNow = clock.UtcNow.AddDays(4);
        var owned = service.WorkspaceFiles().Single(file => file.Path == aSnapshot);
        Assert.AreEqual(WorkspaceFileOwner.User, owned.Owner);
        Assert.AreEqual(a.TaskId, owned.OwnerTaskId);
        service.Cleanup(true);
        Assert.AreEqual("A", File.ReadAllText(aSnapshot));
    }

    [TestMethod]
    public void IdenticalChangesShareAttributionButFileAndFolderProposalsConflict()
    {
        var group = service.CreateParallelHandoff(Draft(new("worker", "A"), new("other", "B")));
        foreach (var worker in group.Workers)
            File.WriteAllText(Path.Combine(worker.WorkingDirectory, "shared.txt"), "Same edit");
        File.WriteAllText(Path.Combine(group.Workers[0].WorkingDirectory, "shape"), "File");
        var folder = Path.Combine(group.Workers[1].WorkingDirectory, "shape");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "part.txt"), "Nested");
        foreach (var worker in group.Workers) Finish(worker, true);
        var review = service.ReviewParallelHandoff(group.GroupId);
        CollectionAssert.AreEqual(new[] { "shape", Path.Combine("shape", "part.txt") }, review.SharedConflicts);
        Assert.IsTrue(review.Workers.All(worker => worker.Changes.Any(change => change.Path == "shared.txt")));
    }

    [TestMethod]
    public void InvalidInputsAndUnverifiedCompletionCannotCreateAUsableMerge()
    {
        var draft = Draft(new("worker", "A"), new("other", "B"));
        Assert.AreEqual("parallel_relative_path", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateParallelHandoff(draft with { InputFiles = ["../outside.txt"] })).Code);
        Assert.AreEqual("parallel_input_unavailable", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateParallelHandoff(draft with { InputFiles = ["shared.txt", "missing.txt"] })).Code);
        Assert.IsEmpty(service.Tasks());
        var group = service.CreateParallelHandoff(draft);
        service.ForceCompleteTask(group.Workers[0].TaskId, "Operator closed the assignment", true);
        Finish(group.Workers[1], false);
        var review = service.ReviewParallelHandoff(group.GroupId);
        Assert.IsFalse(review.CanMerge);
        Assert.IsTrue(review.Workers.All(worker => !worker.HasResult));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
