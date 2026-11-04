using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TaskImageTests
{
    private string root = null!;
    private string home = null!;
    private ImageClock clock = null!;
    private RelayService service = null!;

    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+lmXYAAAAASUVORK5CYII=");

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        home = Path.Combine(root, "relay");
        clock = new ImageClock();
        service = new RelayService(home, clock);
        service.Initialize();
        service.RegisterManual("a", "fixture", "甲");
        service.RegisterManual("b", "fixture", "乙");
    }

    [TestCleanup] public void Cleanup() => Directory.Delete(root, true);

    private CliHandoffRequest Request(string prompt = "检查图片", ImageInput[]? images = null,
        CollaborationOptions? collaboration = null) =>
        new("a", "b", prompt, Collaboration: collaboration, Images: images);

    private static ImageInput Image(string name = "截图.png") => new(name, "image/png", Png());

    [TestMethod]
    public void SnapshotsPreserveTextAndImagesAfterInputsDisappearAndServiceRestarts()
    {
        var source = Path.Combine(root, "source.png");
        var bytes = Png();
        File.WriteAllBytes(source, bytes);
        File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
        var input = new ImageInput("来源.png", "image/png", File.ReadAllBytes(source));
        var text = "  查看这两张图。\n保持空格与换行。\n";
        TaskRecord task;
        try
        {
            task = service.CreateCliHandoff(Request(text, [input, Image("第二张.png")])).Task;
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(source));
        }
        finally { File.SetAttributes(source, FileAttributes.Normal); }
        File.Delete(source);
        input.Data[0] = 0;

        var restored = new RelayService(home, clock);
        task = restored.Status(task.TaskId).Task;
        Assert.AreEqual(text, File.ReadAllText(task.RequestPath));
        var images = task.Images!;
        Assert.HasCount(2, images);
        Assert.AreNotEqual(images[0].ImageId, images[1].ImageId);
        foreach (var image in images)
        {
            StringAssert.StartsWith(image.Path, Path.Combine(home, "workspace", task.TaskId, task.AttemptId));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(image.Path));
            var artifact = restored.Artifacts(task.TaskId).Single(item => item.ArtifactId == image.ImageId);
            Assert.AreEqual("image", artifact.Kind);
            Assert.AreEqual(image.Path, artifact.Path);
            Assert.AreEqual(task.CreatedAt.AddDays(restored.Settings().ArtifactTtlDays), artifact.ExpiresAt);
        }
        Assert.AreEqual("来源.png", images[0].FileName);
        AssertContext(restored, task, "single");
    }

    [TestMethod]
    public void ImageOnlyUsesExplicitInstructionAndInvalidInputCreatesNothing()
    {
        Assert.AreEqual("cli_work_required", Assert.ThrowsExactly<RelayException>(() =>
            service.CreateCliHandoff(Request("  "))).Code);
        var invalid = new ImageInput?[]
        {
            null, new("", "image/png", Png()), new("x.png", "image/png", null!),
            new("x.png", "image/png", [1, 2, 3]), new("x.png", "image/jpeg", Png()),
            new("x.gif", "image/gif", Png())
        };
        foreach (var image in invalid)
        {
            Assert.AreEqual("task_image_invalid", Assert.ThrowsExactly<RelayException>(() =>
                service.CreateCliHandoff(Request("", [Image(), image!]))).Code);
            Assert.IsEmpty(service.Tasks());
            Assert.IsEmpty(service.ConnectionPermissions());
            Assert.IsFalse(Directory.Exists(Path.Combine(home, ".cli-drafts")));
            Assert.IsFalse(Directory.Exists(Path.Combine(home, "workspace")));
        }
        var task = service.CreateCliHandoff(Request("", [Image()])).Task;
        Assert.AreEqual("请查看附带的图片。", File.ReadAllText(task.RequestPath));
        AssertContext(service, task, "single");
    }

    [TestMethod]
    public void EveryCollaborationTurnKeepsOriginalImagesAndInstruction()
    {
        var task = service.CreateCliHandoff(Request(images: [Image()],
            collaboration: new("b", GenerateCriteria: true))).Task;
        AssertContext(service, task, "criteria");
        Apply(task, new(task.TaskId, task.AttemptId, "a", task.ManualTurn, "done",
            Outcome: "criteria_ready", AcceptanceCriteria: "图片中的要求已经完成。"));
        task = service.Status(task.TaskId).Task;
        AssertContext(service, task, "implement");
        var first = Path.Combine(root, "first.md"); File.WriteAllText(first, "initial result");
        Apply(task, new(task.TaskId, task.AttemptId, "b", task.ManualTurn, "done", ResultFile: first, Outcome: "submitted"));
        task = service.Status(task.TaskId).Task;
        AssertContext(service, task, "review");
        Apply(task, new(task.TaskId, task.AttemptId, "a", task.ManualTurn, "done",
            Outcome: "changes_requested", OpenItems: ["Fix the missing detail."]));
        task = service.Status(task.TaskId).Task;
        AssertContext(service, task, "revise");
        var second = Path.Combine(root, "second.md"); File.WriteAllText(second, "updated result");
        Apply(task, new(task.TaskId, task.AttemptId, "b", task.ManualTurn, "done", ResultFile: second, Outcome: "submitted"));
        task = service.Status(task.TaskId).Task;
        AssertContext(service, task, "review");
        Apply(task, new(task.TaskId, task.AttemptId, "a", task.ManualTurn, "done", Outcome: "approved"));
        task = service.Status(task.TaskId).Task;
        AssertContext(service, task, "accept");
        Apply(task, new(task.TaskId, task.AttemptId, "b", task.ManualTurn, "done", Outcome: "accepted", Deliverable: "已交付"));
        Assert.AreEqual(TaskState.Succeeded, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void ExpiredOrMissingImagesAreReportedAndCleanupUsesArtifactTtl()
    {
        var task = service.CreateCliHandoff(Request(images: [Image()])).Task;
        var path = task.Images!.Single().Path;
        clock.UtcNow = clock.UtcNow.AddDays(4);
        Assert.AreEqual("task_image_unavailable", Assert.ThrowsExactly<RelayException>(() => service.FileHandoffPrompt(task)).Code);
        CollectionAssert.Contains(service.Cleanup(false).Paths, path);
        service.Cleanup(true);
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual("expired", service.Artifacts(task.TaskId).Single(item => item.Kind == "image").State);
        Assert.AreEqual(TaskState.TimedOut, service.Status(task.TaskId).Task.State);

        var missing = service.CreateCliHandoff(Request(images: [Image()])).Task;
        File.Delete(missing.Images!.Single().Path);
        Assert.AreEqual("task_image_unavailable", Assert.ThrowsExactly<RelayException>(() => service.FileHandoffPrompt(missing)).Code);
    }

    [TestMethod]
    public void RelocationRewritesImageMetadataArtifactsAndExistingContext()
    {
        var task = service.CreateCliHandoff(Request(images: [Image()])).Task;
        service.FileHandoffPrompt(task);
        var originalPath = task.Images!.Single().Path;
        service.Cancel(task.TaskId, "Finish the fixture");
        var target = Path.Combine(root, "moved");
        var moved = service.Relocate(target);
        var relocated = moved.Status(task.TaskId).Task;
        var image = relocated.Images!.Single();
        Assert.AreEqual(originalPath.Replace(home, target, StringComparison.Ordinal), image.Path);
        CollectionAssert.AreEqual(Png(), File.ReadAllBytes(image.Path));
        Assert.AreEqual(image.Path, moved.Artifacts(task.TaskId).Single(item => item.Kind == "image").Path);
        var contextPath = Path.Combine(Path.GetDirectoryName(moved.ManualReceiptPath(relocated))!, "context.json");
        using var document = JsonDocument.Parse(File.ReadAllText(contextPath));
        Assert.AreEqual(image.Path, document.RootElement.GetProperty("images")[0].GetProperty("path").GetString());
        AssertContext(moved, relocated, "single");
    }

    private void Apply(TaskRecord task, ManualReceipt receipt)
    {
        File.WriteAllText(service.ManualReceiptPath(task), RelayJson.Write(receipt));
        Assert.IsEmpty(service.ImportManualReceipts().Errors);
    }

    private static void AssertContext(RelayService relay, TaskRecord task, string phase)
    {
        relay.FileHandoffPrompt(task);
        var path = Path.Combine(Path.GetDirectoryName(relay.ManualReceiptPath(task))!, "context.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var images = root.GetProperty("images").EnumerateArray().ToArray();
        var expected = task.Images!;
        Assert.HasCount(expected.Length, images);
        for (var i = 0; i < images.Length; i++)
        {
            Assert.AreEqual(expected[i].ImageId, images[i].GetProperty("image_id").GetString());
            Assert.AreEqual(expected[i].FileName, images[i].GetProperty("file_name").GetString());
            Assert.AreEqual(expected[i].MediaType, images[i].GetProperty("media_type").GetString());
            Assert.AreEqual(expected[i].Path, images[i].GetProperty("path").GetString());
        }
        StringAssert.Contains(root.GetProperty("instructions").GetString()!, "Inspect every attached image");
        if (phase != "single") Assert.AreEqual(phase, root.GetProperty("collaboration").GetProperty("phase").GetString());
    }

    private sealed class ImageClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
