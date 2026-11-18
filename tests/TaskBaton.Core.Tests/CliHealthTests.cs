using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class CliHealthTests
{
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")) { ZcodeUserDirectory = Path.Combine(root, "zcode-user") }; service.Initialize();
        service.RegisterManual("agent-b", "fixture", "乙");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private string Status(string id) => service.CliHealth("agent-b").Single(c => c.Id == id).Status;
    private void Import(CliCommand create, string format, CliCommand? resume = null) => service.ImportCliRecipe("agent-b",
        RelayJson.Write(new CliRecipe(1, "fixture", "fixture", create, resume, [], "", "", "", "", format)));

    [TestMethod]
    public async Task ReadOnlyDesktopConnectionCheckKeepsSavedBlockersAndDoesNotStartTheHost()
    {
        Import(new("relay-test-" + Guid.NewGuid().ToString("N") + ".exe", [], null, null,
            new() { ["DSH_HOME"] = Path.Combine(root, "missing-dsh-home") }), "dsh-desktop");
        var blocked = service.CliExtension("agent-b")! with { Block = new("auth_required", "Saved authentication failure", DateTimeOffset.UtcNow) };
        new RelayStore(service.Home).Write(s => s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id=$id",
            ("$json", RelayJson.Write(blocked)), ("$id", "agent-b")));
        var checks = await service.CliHealthAsync("agent-b");
        Assert.AreEqual("fail", checks.Single(c => c.Id == "host.desktop.connection").Status);
        Assert.AreEqual("fail", checks.Single(c => c.Id == "block").Status);
        Assert.AreEqual(blocked.Block, service.CliExtension("agent-b")!.Block);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "missing-dsh-home")));
    }

    [TestMethod]
    public async Task LocalChecksForAnUnconfiguredAgentDoNotAttemptADesktopConnection()
    {
        var checks = await service.CliHealthAsync("agent-b");
        Assert.HasCount(1, checks);
        Assert.AreEqual("recipe", checks.Single().Id);
    }

    [TestMethod]
    public void MissingRecipeProgramAndFolderAreReportedWithoutRunningAnything()
    {
        Assert.AreEqual("fail", service.CliHealth("agent-b").Single().Status);
        Import(new(Path.Combine(root, "missing.exe"), ["{prompt}"], null, Path.Combine(root, "no-folder")), "text");
        Assert.AreEqual("fail", Status("create.executable"));
        Assert.AreEqual("fail", Status("create.cwd"));
        Assert.AreEqual("warn", Status("resume"));
        Assert.AreEqual("warn", Status("verified.create"));
    }

    [TestMethod]
    public void CodeBuddyRecipesNeedTheStandaloneCliAndANonInteractivePermissionFlag()
    {
        Import(new("pwsh", [@"D:\workbuddy\resources\app.asar.unpacked\cli\dist\codebuddy-lite-wb.mjs", "-p", "{prompt}", "--output-format", "json"], null, null), "claude-json");
        Assert.AreEqual("fail", Status("host.login"));
        Assert.AreEqual("fail", Status("host.permission"));
        Import(new("pwsh", ["codebuddy", "-p", "{prompt}", "--output-format", "json", "-y"], null, null), "claude-json");
        Assert.IsFalse(service.CliHealth("agent-b").Any(c => c.Id.StartsWith("host.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ZcodeNeedsAnAccountPlanRegisteredForTheCommandLine()
    {
        var config = Path.Combine(root, "zcode", "provider_config.json"); Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{"schemaVersion":1,"config":{"providerConfigRules":{},"modelConfigRules":{}}}""");
        File.WriteAllText(Path.Combine(root, "zcode", "credentials.json"), """{"oauth:zai:access_token":"secret"}""");
        Import(new("pwsh", ["zcode.cjs", "-p", "{prompt}", "--json"], null, null,
            new() { ["ZCODE_PERSONAL_PROVIDER_CONFIG_FILE"] = config }), "zcode-json");
        Assert.AreEqual("fail", Status("host.account"));
        Assert.AreEqual("warn", Status("host.model"));
        File.WriteAllText(Path.Combine(root, "zcode", "credentials.json"), """{"account-provider:account:zai-start-plan:identity":"x"}""");
        File.WriteAllText(config, """{"schemaVersion":1,"config":{"defaultModelSelection":{"providerId":"p","modelId":"m"}}}""");
        Assert.AreEqual("pass", Status("host.account"));
        Assert.AreEqual("pass", Status("host.model"));

        // A default model on a provider with its own key needs no account plan.
        File.WriteAllText(Path.Combine(root, "zcode", "credentials.json"), "{}");
        File.WriteAllText(config, """{"config":{"defaultModelSelection":{"providerId":"custom","modelId":"m"},"providerConfigRules":{"providerRules":[{"providerId":"custom","providerName":"OpenCode Go","enabled":true,"config":{"access":{"type":"x","apiKey":"secret"}}}]}}}""");
        Assert.AreEqual("pass", Status("host.account"));
        File.WriteAllText(config, """{"config":{"defaultModelSelection":{"providerId":"custom","modelId":"m"},"providerConfigRules":{"providerRules":[{"providerId":"custom","enabled":false,"config":{"access":{"apiKey":"secret"}}}]}}}""");
        Assert.AreEqual("fail", Status("host.account"));
    }

    [TestMethod]
    public void ZcodeRecipesAlwaysLoadToolsOnDemand()
    {
        service.RegisterManual("zcode", "ZCode Desktop", "Zcode");
        var command = new CliCommand("node", ["zcode.cjs", "-p", "{prompt}", "--json"], null, null);
        var imported = service.ImportCliRecipe("zcode", RelayJson.Write(new CliRecipe(1, "z", "ZCode Desktop", command,
            command with { Arguments = ["zcode.cjs", "--resume", "{session_id}", "-p", "{prompt}", "--json"] }, [], "", "", "", "", "zcode-json")));
        CollectionAssert.AreEqual(new[] { "--toolsearch-mode", "local" }, imported.Recipe.Create.Arguments[^2..]);
        CollectionAssert.AreEqual(new[] { "--toolsearch-mode", "local" }, imported.Recipe.Resume!.Arguments[^2..]);
        Assert.IsFalse(service.CliHealth("zcode").Any(c => c.Id.StartsWith("host.toolsearch", StringComparison.Ordinal)));
        var other = service.ImportCliRecipe("agent-b", RelayJson.Write(new CliRecipe(1, "f", "fixture", new("pwsh", ["{prompt}"], null, null), null, [], "", "", "", "", "text")));
        Assert.DoesNotContain("--toolsearch-mode", other.Recipe.Create.Arguments);
    }

    [TestMethod]
    public void ZcodeRecipesLaunchTheWayTheDesktopDoesSoContinuedConversationsKeepTheirCache()
    {
        var user = Path.Combine(root, "zcode-user"); Directory.CreateDirectory(Path.Combine(user, "v2"));
        var settings = new Dictionary<string, object> { ["dynamicWorkflowMode"] = "onDemand", ["memoryEnabled"] = false, ["dataBaseDir"] = Path.Combine(root, "workspace") };
        File.WriteAllText(Path.Combine(user, "v2", "setting.json"), System.Text.Json.JsonSerializer.Serialize(settings));
        var provider = Path.Combine(root, "workspace", ".zcode", "v2", "provider_config.json");
        service.RegisterManual("zcode", "ZCode Desktop", "Zcode");
        var command = new CliCommand("node", ["zcode.cjs", "-p", "{prompt}", "--json"], null, null,
            new() { ["ZCODE_PERSONAL_PROVIDER_CONFIG_FILE"] = provider });
        var imported = service.ImportCliRecipe("zcode", RelayJson.Write(new CliRecipe(1, "z", "ZCode Desktop", command,
            command with { Arguments = ["zcode.cjs", "--resume", "{session_id}", "-p", "{prompt}", "--json"] }, [], "", "", "", "", "zcode-json")));
        foreach (var launch in new[] { imported.Recipe.Create, imported.Recipe.Resume! })
        {
            Assert.AreEqual("desktop", CliRecipes.ZcodeArgument(launch, "--surface"));
            Assert.AreEqual("onDemand", CliRecipes.ZcodeArgument(launch, "--workflow-mode"));
            Assert.AreEqual(Path.Combine(Path.GetDirectoryName(provider)!, "visualizations"), launch.Environment!["ZCODE_GEN_UI_OUTPUT_ROOT"]);
        }
        Assert.IsFalse(service.CliHealth("zcode").Any(c => c.Id.StartsWith("host.parity.", StringComparison.Ordinal) && c.Id != "host.parity.memory"));

        // Memory is a command-line setting the desktop also reads, so Relay reports a mismatch instead of writing it.
        var health = () => service.CliHealth("zcode").Single(c => c.Id == "host.parity.memory");
        Assert.AreEqual("warn", health().Status);
        StringAssert.Contains(health().Message, "\"memory\": false");
        Directory.CreateDirectory(Path.Combine(user, "cli"));
        File.WriteAllText(Path.Combine(user, "cli", "config.json"), """{"features":{"memory":false}}""");
        Assert.AreEqual("pass", health().Status);
        File.WriteAllText(Path.Combine(user, "v2", "setting.json"), """{"dynamicWorkflowMode":"alwaysOn","memoryEnabled":true}""");
        Assert.AreEqual("warn", health().Status);
        Assert.AreEqual("warn", service.CliHealth("zcode").Single(c => c.Id == "host.parity.workflow.resume").Status);
        // Without dataBaseDir the desktop keeps its data in ~/.zcode/v2, so this provider file is no longer the desktop's.
        Assert.AreEqual("warn", service.CliHealth("zcode").Single(c => c.Id == "host.parity.provider").Status);

        // An explicit surface is kept, and doctor reports that it differs from the desktop's.
        var terminal = service.ImportCliRecipe("agent-b", RelayJson.Write(new CliRecipe(1, "f", "fixture",
            new("pwsh", ["zcode.cjs", "-p", "{prompt}", "--json", "--surface", "terminal"], null, null), null, [], "", "", "", "", "zcode-json")));
        Assert.IsNull(terminal.Recipe.Create.Environment);
        Assert.AreEqual("warn", service.CliHealth("agent-b").Single(c => c.Id == "host.parity.genui.create").Status);
    }

    [TestMethod]
    public void WorkBuddyExplainsWhyItCannotHaveABridge()
    {
        service.RegisterManual("workbuddy", "WorkBuddy", "WorkBuddy");
        var bridge = service.Bridge("workbuddy");
        Assert.AreEqual("none", bridge.Kind);
        StringAssert.Contains(bridge.HostNote, "设计限制");
        Assert.IsNull(service.Bridge("agent-b").HostNote);
    }

    [TestMethod]
    public void SetupPromptAsksBeforeInstallingBridgesAndPrefersAcp()
    {
        var prompt = service.AgentSetupPrompt();
        StringAssert.Contains(prompt, "Agent Client Protocol");
        StringAssert.Contains(prompt, "不要自行安装");
        StringAssert.Contains(prompt, "cli-recipe doctor");
    }

    [TestMethod]
    public async Task WindowsPrefersTheCmdShimOverTheExtensionlessPosixShim()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows PATHEXT behavior is required.");
        var toolDir = Path.Combine(root, "tools"); Directory.CreateDirectory(toolDir);
        File.WriteAllText(Path.Combine(toolDir, "probe-agent"), "#!/bin/sh\nexit 0\n");
        File.WriteAllText(Path.Combine(toolDir, "probe-agent.cmd"), "@echo off\r\nexit /b 0\r\n");
        File.WriteAllText(Path.Combine(toolDir, "probe-run.cmd"), "@echo off\r\necho {\"accepted\":true}\r\n");
        var previous = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", toolDir + Path.PathSeparator + previous);
        try
        {
            var resolved = RelayProcess.ResolveExecutable("probe-agent");
            Assert.IsNotNull(resolved);
            StringAssert.EndsWith(resolved!, ".cmd", StringComparison.OrdinalIgnoreCase);
            Import(new("probe-agent", ["{prompt}"], null, null), "text");
            Assert.AreEqual("pass", Status("create.executable"));
            var run = await RelayProcess.RunAsync("probe-run", [], null, TimeSpan.FromSeconds(10));
            Assert.AreEqual(0, run.ExitCode);
            StringAssert.Contains(run.Output, "accepted");
        }
        finally { Environment.SetEnvironmentVariable("PATH", previous); }
    }
}
