using System.Text.Json.Nodes;
using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class AgentModelTests
{
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("zcode", "ZCode Desktop", "Zcode");
        service.RegisterManual("dsh", "DeepSeek Harness", "DSH");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private void Import(string agent, string format, CliCommand command) => service.ImportCliRecipe(agent, RelayJson.Write(
        new CliRecipe(1, "fixture", agent == "zcode" ? "ZCode Desktop" : "DeepSeek Harness", command, command with { Arguments = [.. command.Arguments, "{session_id}"] },
            [], "", "", "", "", format)));

    [TestMethod]
    public async Task ZcodeListsTheDesktopProvidersAndWritesOnlyTheDefaultSelection()
    {
        var config = Path.Combine(root, "provider_config.json");
        File.WriteAllText(config, """
            {"schemaVersion":1,"config":{"providerConfigRules":{"providerRules":[
              {"providerId":"off","enabled":false,"config":{"modelOrder":["x"]}},
              {"providerId":"oc","providerName":"OpenCode Go","enabled":true,"config":{"apiKey":"kept","modelOrder":["ds","step"]}}]},
             "modelConfigRules":{"providerModelRules":[
              {"providerId":"oc","modelId":"ds","config":{"enabled":true,"optionSpecs":{"reasoningLevel":{"values":["low","high","max"]}}}},
              {"providerId":"oc","modelId":"step","config":{"enabled":true,"optionSpecs":{"reasoningLevel":{"values":["low","medium","high"]}}}}],
              "manualProviderModelRules":[]}}}
            """);
        Import("zcode", "zcode-json", new("node", ["zcode.cjs", "-p", "{prompt}", "--json"], null, null, new() { ["ZCODE_PERSONAL_PROVIDER_CONFIG_FILE"] = config }));
        var catalog = await service.AgentModelsAsync("zcode");
        Assert.IsTrue(catalog.Supported); Assert.IsNull(catalog.Current);
        CollectionAssert.AreEqual(new[] { "ds", "step" }, catalog.Options.Select(o => o.ModelId).ToArray());
        Assert.AreEqual("high", RelayService.MiddleReasoningLevel(catalog.Options[0].ReasoningLevels));
        Assert.AreEqual("medium", RelayService.MiddleReasoningLevel(catalog.Options[1].ReasoningLevels));
        await Assert.ThrowsExactlyAsync<RelayException>(() => service.SetAgentModelAsync("zcode", new("oc", "ds", "medium")));
        var saved = await service.SetAgentModelAsync("zcode", new("oc", "ds", "high"));
        Assert.AreEqual(new AgentModelSelection("oc", "ds", "high"), saved.Current);
        var written = JsonNode.Parse(File.ReadAllText(config))!["config"]!;
        Assert.AreEqual("kept", written["providerConfigRules"]!["providerRules"]![1]!["config"]!["apiKey"]!.GetValue<string>());
        Assert.IsTrue(File.Exists(config + ".bak-relay-model"));
    }

    [TestMethod]
    public void DshComposedConfigYieldsProvidersModelsLevelsAndTheCurrentDefault()
    {
        var (options, current) = RelayService.ParseDshConfig("""
            # == base
            - id: agent-default-model
              name: '@deepseek-ai/dsh-agent-default-model'
              config:
                provider: opencode-local
                model: deepseek-v4.1-flash
                reasoningEffort: high
            - id: llm-pi-ai
              name: '@deepseek-ai/dsh-llm-pi-ai'
              config:
                providers:
                  opencode-local:
                    displayName: OpenCode Go
                    models:
                      - id: deepseek-v4.1-flash
                        name: DeepSeek
                        reasoningEfforts:
                          'off': null
                          low: low
                          high: high
                          max: max
                        input:
                          - text
                      - id: step-5-preview
                        reasoningEfforts:
                          low: low
                          medium: medium
            - id: permission
              config:
                defaultPreset: danger-full-access
            """);
        Assert.AreEqual(new AgentModelSelection("opencode-local", "deepseek-v4.1-flash", "high"), current);
        Assert.HasCount(2, options);
        CollectionAssert.AreEqual(new[] { "low", "high", "max" }, options[0].ReasoningLevels);
        Assert.AreEqual("OpenCode Go", options[0].ProviderName);
        CollectionAssert.AreEqual(new[] { "low", "medium" }, options[1].ReasoningLevels);
    }

    [TestMethod]
    public async Task DshWithoutARelayPatchLayerCannotBeConfiguredAndOtherHostsSayWhereToChoose()
    {
        Import("dsh", "dsh-jsonl", new("dsh", ["--profile", "headless", "--json", "{prompt}"], null, null));
        var dsh = await service.AgentModelsAsync("dsh");
        Assert.IsFalse(dsh.Supported); StringAssert.Contains(dsh.Message, "补丁层");
        service.RegisterManual("plain", "manual", "Plain");
        Assert.IsFalse((await service.AgentModelsAsync("plain")).Supported);
    }
}
