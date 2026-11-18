using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class ManualTaskPromptTests
{
    private string root=null!;
    private RelayService service=null!;
    [TestInitialize]
    public void Setup()
    {
        root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        service=new(root);service.Initialize();
        service.RegisterManual("writer","manual","Writer");service.RegisterManual("reviewer","manual","Reviewer");
    }
    [TestCleanup]public void Cleanup()=>Directory.Delete(root,true);
    [TestMethod]
    public void CompletePromptCarriesTheSelectedNodesAndWorkWithoutCreatingATask()
    {
        var work="Write the API notes, exchange three rounds, and finish after approval.";
        var prompt=service.ManualTaskPrompt("writer","reviewer",work);
        StringAssert.Contains(prompt,work);Assert.HasCount(2,prompt.Split(work,StringSplitOptions.None));
        StringAssert.Contains(prompt,"\"from\": \"writer\"");StringAssert.Contains(prompt,"\"to\": \"reviewer\"");
        StringAssert.Contains(prompt,"Writer");StringAssert.Contains(prompt,"Reviewer");
        StringAssert.Contains(prompt,"当前一轮写完不代表整个任务结束");Assert.IsEmpty(service.Tasks());
    }
    [TestMethod]
    public void InvalidWorkAndNodesDoNotProduceACopyableDraft()
    {
        Assert.ThrowsExactly<RelayException>(()=>service.ManualTaskPrompt("writer","reviewer"," "));
        Assert.ThrowsExactly<RelayException>(()=>service.ManualTaskPrompt("writer","writer","work"));
        Assert.ThrowsExactly<RelayException>(()=>service.ManualTaskPrompt("missing","reviewer","work"));
        Assert.IsEmpty(service.Tasks());
    }
}
