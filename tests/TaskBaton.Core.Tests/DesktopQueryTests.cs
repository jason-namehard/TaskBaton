using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class DesktopQueryTests
{
    [TestMethod]
    public void GraphKeepsDirectionPermissionAndCapabilitySeparate()
    {
        var root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        var service=new RelayService(Path.Combine(root,"data"));service.Initialize();
        var config=Path.Combine(root,"config.toml");File.WriteAllText(config,"name = 'fixture'");
        foreach(var id in new[]{"agent-a","agent-b","agent-c"})service.Register(id,"fixture",config);
        service.ValidateAgent("agent-a");service.ValidateAgent("agent-b");
        service.SetPermission("agent-a","agent-b","allow");service.SetPermission("agent-a","agent-c","allow");
        service.SetPermission("agent-c","agent-b","deny");
        var edges=service.Connections();
        Assert.AreEqual("Unavailable",edges.Single(e=>e.From=="agent-a" && e.To=="agent-b").Display);
        Assert.AreEqual("Unavailable",edges.Single(e=>e.From=="agent-b" && e.To=="agent-a").Display);
        Assert.AreEqual("allow",edges.Single(e=>e.From=="agent-a" && e.To=="agent-c").Permission);
        Assert.AreEqual("Unavailable",edges.Single(e=>e.From=="agent-a" && e.To=="agent-c").Display);
        Assert.AreEqual("Unavailable",edges.Single(e=>e.From=="agent-c" && e.To=="agent-b").Display);
        File.Delete(config);
        Assert.AreEqual("Unavailable",service.Connections().Single(e=>e.From=="agent-a" && e.To=="agent-b").Display);
    }

    [TestMethod]
    public async Task SummaryCountsTasksOnceAndReadDoesNotChangeTask()
    {
        var root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        var service=new RelayService(Path.Combine(root,"data"));service.Initialize();
        var config=Path.Combine(root,"config.toml");File.WriteAllText(config,"name = 'fixture'");
        foreach(var id in new[]{"agent-a","agent-b"}){service.Register(id,"fixture",config);service.ValidateAgent(id);}
        service.SetPermission("agent-a","agent-b","allow");
        var task=(await service.DispatchAsync(new("agent-a","agent-b","Review","manual","manual","one"))).Task;
        File.WriteAllText(task.ResultPath,"## Result\nReady.");
        service.Complete(task.TaskId,task.AttemptId,"agent-b","done",task.ResultPath);
        var waiting=await service.DispatchAsync(new("agent-a","agent-b","Review","manual","manual","two"));
        var summary=service.Summary();
        Assert.AreEqual(new HomeSummary(2,1,1,3),summary);
        var notice=service.Inbox().First();service.MarkNotificationRead(notice.NotificationId);
        Assert.IsTrue(service.Inbox().Single(n=>n.NotificationId==notice.NotificationId).IsRead);
        Assert.AreEqual(TaskState.Succeeded,service.Status(task.TaskId).Task.State);
        Assert.AreEqual(TaskState.AwaitingManual,service.Status(waiting.Task.TaskId).Task.State);
        Assert.ThrowsExactly<RelayException>(()=>service.MarkNotificationRead(long.MaxValue));
    }
}
