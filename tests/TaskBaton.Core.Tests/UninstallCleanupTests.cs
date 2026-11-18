using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class UninstallCleanupTests
{
    private string root = null!, home = null!, location = null!, instructions = null!, stable = null!;
    private RelayService relay = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        home = Path.Combine(root, "自选中转站"); location = Path.Combine(root, "relay.location.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(location, RelayJson.Write(new { home }));
        relay = new RelayService(home); relay.Initialize(); relay.RegisterManual("agent-a", "manual", "A");
        instructions = Path.Combine(root, "AGENTS.md"); File.WriteAllText(instructions, "# User instructions\nKeep this.\n");
        relay.InstallGuidance("agent-a", instructions); stable = relay.StableInstructionPath("agent-a");
        Directory.CreateDirectory(Path.Combine(home, "workspace"));
        File.WriteAllText(Path.Combine(home, "workspace", "user-file.md"), "Keep this file");
        File.WriteAllText(Path.Combine(home, "desktop.preferences.json"), "{}");
        File.WriteAllText(Path.Combine(home, "user-file.txt"), "Keep this too");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(stable)) File.Delete(stable);
        if (Directory.Exists(Path.GetDirectoryName(stable))) Directory.Delete(Path.GetDirectoryName(stable)!);
        Directory.Delete(root, true);
    }

    [TestMethod]
    public void ConfirmedDataCleanupRemovesRulesAndLedgerButPreservesUserFiles()
    {
        Assert.AreEqual(home, UninstallCleanup.ResolveHome(location, null));
        Assert.AreEqual(1, UninstallCleanup.Apply(location, null, home, false, true));
        Assert.AreEqual("# User instructions\nKeep this.\n", File.ReadAllText(instructions));
        Assert.IsFalse(File.Exists(stable)); Assert.IsFalse(File.Exists(Path.Combine(home, "taskbaton.db")));
        Assert.IsFalse(File.Exists(Path.Combine(home, "desktop.preferences.json"))); Assert.IsFalse(File.Exists(location));
        Assert.AreEqual("Keep this file", File.ReadAllText(Path.Combine(home, "workspace", "user-file.md")));
        Assert.AreEqual("Keep this too", File.ReadAllText(Path.Combine(home, "user-file.txt")));
    }

    [TestMethod]
    public void KeepDataCanRemoveOnlyRules()
    {
        Assert.AreEqual(1, UninstallCleanup.Apply(location, null, home, true, false));
        Assert.IsTrue(File.Exists(Path.Combine(home, "taskbaton.db"))); Assert.IsTrue(File.Exists(location));
        Assert.IsTrue(File.Exists(Path.Combine(home, "desktop.preferences.json")));
        Assert.AreEqual("# User instructions\nKeep this.\n", File.ReadAllText(instructions));
    }

    [TestMethod]
    public void DamagedRulePreventsDatabaseRemoval()
    {
        File.WriteAllText(instructions, File.ReadAllText(instructions).Replace(":end -->", ":broken -->"));
        Assert.ThrowsExactly<RelayException>(() => UninstallCleanup.Apply(location, null, home, true, true));
        Assert.IsTrue(File.Exists(Path.Combine(home, "taskbaton.db"))); Assert.IsTrue(File.Exists(location));
    }

    [TestMethod]
    public void ChangedLocationDoesNotDeleteAnyData()
    {
        Assert.ThrowsExactly<RelayException>(() => UninstallCleanup.Apply(location, null, Path.Combine(root, "other"), true, true));
        Assert.IsTrue(File.Exists(Path.Combine(home, "taskbaton.db"))); Assert.IsTrue(File.Exists(location));
    }
    [TestMethod]
    public void MissingLocationIsNotTreatedAsDefaultStation()
    {
        File.Delete(location);
        var error=Assert.ThrowsExactly<RelayException>(() => UninstallCleanup.ResolveHome(location, null));
        Assert.AreEqual("uninstall_location_missing",error.Code);
        Assert.ThrowsExactly<RelayException>(() => UninstallCleanup.Apply(location, null, home, true, true));
        Assert.IsTrue(File.Exists(Path.Combine(home,"taskbaton.db")));
        Assert.IsTrue(File.Exists(instructions));
    }

    [TestMethod]
    public void ExplicitEnvironmentStationWorksWithoutLocationFile()
    {
        File.Delete(location);
        Assert.AreEqual(home,UninstallCleanup.ResolveHome(location,home));
        Assert.AreEqual(1,UninstallCleanup.Apply(location,home,home,true,false));
        Assert.IsTrue(File.Exists(Path.Combine(home,"taskbaton.db")));
    }

    [TestMethod]
    public async Task ExplicitCliHomeCanBePlannedWithoutLocationFile()
    {
        File.Delete(location);
        var output=Path.Combine(root,"plan.txt");
        var exit=await RelayCli.RunAsync(["--home",home,"uninstall","plan","--location-file",location,"--output",output]);
        Assert.AreEqual(0,exit);
        Assert.AreEqual(home,File.ReadAllText(output));
        Assert.IsTrue(File.Exists(Path.Combine(home,"taskbaton.db")));
    }

    [TestMethod]
    public async Task UninstallFailureWritesReadableMessageAlongsideJsonReport()
    {
        var report=Path.Combine(root,"report.json"); var message=Path.Combine(root,"error.txt");
        var exit=await RelayCli.RunAsync(["--home",home,"uninstall","cleanup","--location-file",location,
            "--expected-home",Path.Combine(root,"different"),"--remove-data","--report-path",report,"--error-message-path",message]);
        Assert.AreNotEqual(0,exit);
        using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
        var expected=json.RootElement.GetProperty("error").GetProperty("message").GetString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(expected));
        Assert.AreEqual(expected,File.ReadAllText(message));
        Assert.IsTrue(File.Exists(Path.Combine(home,"taskbaton.db")));
    }
}
