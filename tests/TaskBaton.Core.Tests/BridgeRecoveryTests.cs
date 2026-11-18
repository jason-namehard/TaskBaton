using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class BridgeRecoveryTests
{
    private string root=null!;
    private RelayService service=null!;
    private CliExtensionRecord before=null!;

    [TestInitialize]
    public void Setup()
    {
        root=Path.Combine(Path.GetTempPath(),"TaskBaton.Tests",Guid.NewGuid().ToString());
        service=new(root);service.Initialize();service.RegisterManual("dsh","DSH","DSH");
        var command=new CliCommand("not-started",[],null,null);
        before=service.ImportCliRecipe("dsh",RelayJson.Write(new CliRecipe(1,"fixture","DSH",command,null,[],"","","","","dsh-desktop")))
            with {Block=new("auth_required","Saved failure",DateTimeOffset.UtcNow)};
        Save(before);
    }
    [TestCleanup]public void Cleanup()=>Directory.Delete(root,true);
    private void Save(CliExtensionRecord record)=>new RelayStore(root).Write(s=>s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id='dsh'",("$json",RelayJson.Write(record))));

    [TestMethod]
    public void SuccessfulConnectionClearsOldAuthenticationBlockWithoutInventingVerification()
    {
        var result=service.ApplyDesktopBridgeRecovery(before,[new("block","fail","old"),new("host.desktop.connection","pass","connected")]);
        Assert.IsTrue(result.BlockCleared);Assert.IsNull(service.CliExtension("dsh")!.Block);
        Assert.IsFalse(service.CliExtension("dsh")!.CanCreate);Assert.IsFalse(service.CliExtension("dsh")!.CanResume);
        StringAssert.Contains(result.Message,"尚未验证");Assert.IsEmpty(service.Tasks());
    }
    [TestMethod]
    public void FailedConnectionOrInvalidPrerequisiteLeavesTheBlockIntact()
    {
        foreach(var checks in new CliHealthCheck[][]
        {
            [new("host.desktop.connection","fail","denied")],
            [new("host.desktop.connection","pass","connected"),new("create.executable","fail","missing")],
            [new("host.desktop.auth","pass","readable")]
        })
        {
            Assert.IsFalse(service.ApplyDesktopBridgeRecovery(before,checks).BlockCleared);
            Assert.AreEqual(before.Block,service.CliExtension("dsh")!.Block);
        }
    }
    [TestMethod]
    public async Task PublicRecoveryDoesNotClearAConnectionThatCannotBeChecked()
    {
        var result=await service.RecheckDesktopBridgeAsync("dsh");
        Assert.IsFalse(result.BlockCleared);Assert.AreEqual(before.Block,service.CliExtension("dsh")!.Block);
    }
    [TestMethod]
    public void ANewFailureArrivingDuringTheCheckCannotBeClearedByTheOldResult()
    {
        Save(before with {Block=before.Block! with {At=before.Block.At.AddMinutes(1)}});
        Assert.ThrowsExactly<RelayException>(()=>service.ApplyDesktopBridgeRecovery(before,[new("host.desktop.connection","pass","connected")]));
        Assert.IsNotNull(service.CliExtension("dsh")!.Block);
    }
}
