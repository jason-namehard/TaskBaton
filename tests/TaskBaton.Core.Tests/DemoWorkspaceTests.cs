using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class DemoWorkspaceTests
{
    [TestMethod]
    public async Task DemoUsesIsolatedFilesAndExplicitOutcomesWithoutExecutors()
    {
        var root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        var service = await DemoWorkspace.CreateAsync(root);
        Assert.HasCount(3, service.Agents());
        Assert.HasCount(3, service.Tasks());
        Assert.IsEmpty(service.Executors());
        Assert.IsEmpty(service.Resumers());
        Assert.IsEmpty(service.Adapters());
        Assert.IsNull(service.DesktopExecutable());
        Assert.AreEqual(new HomeSummary(3, 1, 2, 3), service.Summary());
        Assert.IsTrue(service.Agents().All(a => a.Host == "demo" && a.ConfigPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
        foreach (var view in service.Tasks().Where(v => v.Task.State is TaskState.Succeeded or TaskState.Failed))
        {
            StringAssert.Contains(File.ReadAllText(view.Task.ResultPath), "演示");
            Assert.IsTrue(service.Audit(view.Task.TaskId).Any(e => e.Event is "task.completed" or "task.failed"));
        }
        var original = service.Agents().First().ConfigPath;
        var content = File.ReadAllText(original);
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => DemoWorkspace.CreateAsync(root));
        Assert.AreEqual("demo_directory_exists", error.Code);
        Assert.AreEqual(content, File.ReadAllText(original));
        Assert.HasCount(3, new RelayService(root).Tasks());
    }
}
