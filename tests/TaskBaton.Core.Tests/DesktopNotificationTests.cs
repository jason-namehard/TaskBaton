using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class DesktopNotificationTests
{
    private string root=null!;
    private RelayService service=null!;
    private TaskRecord task=null!;

    [TestInitialize]
    public void Setup()
    {
        root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        service=new RelayService(Path.Combine(root,"relay"));service.Initialize();
        service.RegisterManual("a","manual","写稿人");service.RegisterManual("b","manual","审阅人");
        var input=Path.Combine(root,"input.md");File.WriteAllText(input,"Review this.");
        task=service.CreateFileHandoff(new("a","b",input)).Task;
    }

    [TestCleanup]
    public void Cleanup()=>Directory.Delete(root,true);

    [TestMethod]
    public void CurrentPackagePathReplacesAnEnabledStalePathWithoutStartingAProcess()
    {
        var missing=Path.Combine(root,"removed-preview","TaskBaton.Notify.exe");
        new RelayStore(service.Home).Write(s=>s.Execute("INSERT INTO desktop VALUES(1,$exe)",("$exe",missing)));
        var current=Path.Combine(root,"TaskBaton.Notify.exe");File.WriteAllText(current,"A test file, not an executable.");
        Assert.AreEqual(current,service.RebindDesktopExecutable(current));
        Assert.AreEqual(current,service.DesktopExecutable());
        File.Delete(current);
        Assert.ThrowsExactly<RelayException>(()=>service.RebindDesktopExecutable(current));
        Assert.AreEqual(current,service.DesktopExecutable());
    }

    [TestMethod]
    public void PathRefreshNeverEnablesNotificationsTheUserDisabled()
    {
        var current=Path.Combine(root,"TaskBaton.Notify.exe");File.WriteAllText(current,"fixture");
        Assert.IsNull(service.RebindDesktopExecutable(current));
        Assert.IsNull(service.DesktopExecutable());
    }

    [TestMethod]
    public void ExplicitFallbackEnablePreservesTheUserChoiceWithoutPretendingTheHelperExists()
    {
        var missing=Path.Combine(root,"missing-package","TaskBaton.Notify.exe");
        service.EnableDesktopFallback(missing);
        Assert.AreEqual(missing,service.DesktopExecutable());
        Assert.IsFalse(File.Exists(missing));
        service.DisableDesktop();
        Assert.IsNull(service.DesktopExecutable());
    }

    [TestMethod]
    public async Task MissingHelperReturnsAnUnavailableResultWithoutChangingTaskOutcome()
    {
        var result=await new DesktopNotifier(Path.Combine(root,"removed.exe")).NotifyAsync(service.Home,task,CancellationToken.None);
        Assert.AreEqual("unavailable",result.State);
        Assert.AreEqual(OperatingSystem.IsWindows()?"desktop_executable_missing":"desktop_platform",result.ErrorCode);
        Assert.AreEqual(TaskState.AwaitingManual,service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void NotificationContentNamesTheCurrentWaitingAgentAndDoesNotRequestMoreWorkAfterCompletion()
    {
        StringAssert.Contains(service.NotificationContent(task).Title,"审阅人");
        task.State=TaskState.AwaitingReply;task.AwaitingAgentId="a";
        StringAssert.Contains(service.NotificationContent(task).Title,"写稿人");
        task.State=TaskState.Succeeded;
        var done=service.NotificationContent(task);
        StringAssert.Contains(done.Title,"已完成");
        StringAssert.Contains(done.Message,"无需继续传递");
        Assert.DoesNotContain("复制",done.Message);
    }

    [TestMethod]
    public void UnconfirmedOrFailedCliDeliveryDoesNotClaimTheAgentReceivedIt()
    {
        task.CliFileTurn=task.ManualTurn;task.CliFileStatus="unknown";task.CliFileError="目标会话尚未确认。";
        var unknown=service.NotificationContent(task);
        StringAssert.Contains(unknown.Title,"待确认");StringAssert.Contains(unknown.Message,task.CliFileError);
        task.CliFileStatus="failed";
        StringAssert.Contains(service.NotificationContent(task).Title,"启动失败");
        task.CliFileTurn=task.ManualTurn-1;
        StringAssert.Contains(service.NotificationContent(task).Title,"等待");
    }
}
