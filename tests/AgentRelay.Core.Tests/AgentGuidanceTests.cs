using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class AgentGuidanceTests
{
    private string root = null!;
    private RelayService service = null!;
    private readonly HashSet<string> stableDirectories = new(StringComparer.Ordinal);

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "manual", "节点甲");
        service.RegisterManual("agent-b", "manual", "节点乙");
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(root, true);
        foreach (var directory in stableDirectories)
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private string Stable(RelayService relay, string id)
    {
        var path = relay.StableInstructionPath(id); stableDirectories.Add(Path.GetDirectoryName(path)!); return path;
    }

    [TestMethod]
    public void AgentCanFillManifestWithoutInventingConfigPaths()
    {
        var manifest = """{"name":"文档助手","host":"dsh","config_path":null,"cli_path":null,"instruction_paths":[]}""";
        var agent = service.SetupAgentFromJson(manifest);
        Assert.AreEqual("文档助手", agent.DisplayName);
        Assert.AreEqual("manual_ready", agent.ValidationStatus);
        Assert.AreEqual("", agent.ConfigPath);
        var updated = service.SetupAgentFromJson("""{"name":"更新名称","host":"dsh"}""", agent.AgentId);
        Assert.AreEqual(agent.AgentId, updated.AgentId);
        Assert.AreEqual("更新名称", service.Agents().Single(a => a.AgentId == agent.AgentId).DisplayName);
        Assert.ThrowsExactly<RelayException>(() => service.SetupAgentFromJson("""{"name":"wrong","host":"dsh","session_id":"not-a-config"}"""));
        Assert.ThrowsExactly<RelayException>(() => service.SetupAgentFromJson("""{"name":"wrong","host":"dsh","config_path":"relative.json"}"""));
        Assert.ThrowsExactly<RelayException>(() => service.SetupAgentFromJson(manifest, "unknown-agent"));
    }

    [TestMethod]
    public void LowercaseManifestParsingFillsADraftWithoutRegistering()
    {
        var before = service.Agents().Count;
        var draft = RelayService.ParseAgentSetupManifest("```json\n{\"name\":\"调查助手\",\"host\":\"dsh\",\"config_path\":null,\"cli_path\":null,\"instruction_paths\":[]}\n```");
        Assert.AreEqual("调查助手", draft.Name);
        Assert.IsNull(draft.ConfigPath);
        Assert.HasCount(before, service.Agents());
        Assert.ThrowsExactly<RelayException>(() => RelayService.ParseAgentSetupManifest("""{"name":"one","name":"two","host":"dsh"}"""));
        Assert.ThrowsExactly<RelayException>(() => RelayService.ParseAgentSetupManifest("""{"name":"one","host":"dsh","instruction_paths":["relative.md"]}"""));
    }

    [TestMethod]
    public void SetupPromptUsesKnownCommandOrExplicitJsonFallback()
    {
        const string command = "& 'E:\\Program Files\\Relay\\relay.exe'";
        var direct = service.AgentSetupPrompt("agent-a", command);
        StringAssert.Contains(direct, command + " --home");
        StringAssert.Contains(direct, " --id agent-a");
        StringAssert.Contains(direct, ".NET 10 runtime");
        var fallback = service.AgentSetupPrompt("agent-a", null);
        StringAssert.Contains(fallback, "按字段手动填写");
        Assert.DoesNotContain(" agent setup --manifest", fallback, StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void SetupPromptRequiresCleanupOfOnlyItsOwnUnneededArtifactsAndReportsTheResult(bool update, bool hasCommand)
    {
        var prompt = service.AgentSetupPrompt(update ? "agent-a" : null, hasCommand ? "relay" : null);
        StringAssert.Contains(prompt, "本次创建的临时文件、测试和探针及其产物的确切路径或标识");
        StringAssert.Contains(prompt, "本次配置创建、归属明确且已不再需要的临时产物");
        StringAssert.Contains(prompt, "临时 JSON、废弃 Markdown、压缩包、备份、测试和探针产物、临时脚本");
        StringAssert.Contains(prompt, "最终生效的配置、运行必需文件及必要的最终说明");
        StringAssert.Contains(prompt, "配置前已有的文件、其他会话的产物、仍在执行或使用中的数据以及无法确认归属的数据");
        StringAssert.Contains(prompt, "不按扩展名或目录批量删除");
        StringAssert.Contains(prompt, "列出保留项和清理摘要");
        StringAssert.Contains(prompt, "暂时无法清理的临时产物说明路径和原因");
        Assert.DoesNotContain("只向用户报告已登记", prompt, StringComparison.Ordinal);
    }

    [TestMethod]
    public void NewSetupPromptsHaveUniqueRequestsAndNeverTargetSelectedNodes()
    {
        var first = service.AgentSetupPrompt(relayCommand: "relay");
        var second = service.AgentSetupPrompt(relayCommand: "relay");
        Assert.AreNotEqual(first, second);
        StringAssert.Contains(first, "agent list");
        StringAssert.Contains(first, "节点甲");
        StringAssert.Contains(first, "不要使用任何已有节点 ID");
        Assert.DoesNotContain("--manifest <该 JSON 文件的绝对路径> --id", first, StringComparison.Ordinal);
        StringAssert.Contains(first, "本次登记命令返回的 Agent ID");
        var agent = service.SetupAgentFromJson("""{"name":"仅有一个名称"}""");
        Assert.AreEqual("manual", agent.Host);
        Assert.AreEqual("仅有一个名称", agent.DisplayName);
        Assert.DoesNotContain(agent.AgentId, new[] { "agent-a", "agent-b" });
    }

    [TestMethod]
    public void PreviewAndOneLineInstallationPreserveUserWorkOnRemoval()
    {
        var file = Path.Combine(root, "project", "AGENTS.md"); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        const string original = "# My instructions\nKeep the user's coding conventions.\n";
        File.WriteAllText(file, original);
        var preview = service.PreviewGuidance("agent-a", file); Stable(service, "agent-a");
        Assert.AreEqual(original, File.ReadAllText(file));
        Assert.IsEmpty(service.GuidanceRegistrations());
        Assert.DoesNotContain('\n', preview.Rule);
        Assert.IsFalse(preview.InstructionPath.StartsWith(service.Home + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        var registration = service.InstallGuidance("agent-a", file);
        Assert.AreEqual(original, File.ReadAllText(Path.Combine(service.Home, registration.BackupRelativePath)));
        service.InstallGuidance("agent-a", file);
        Assert.HasCount(1, service.GuidanceRegistrations());
        Assert.AreEqual(1, File.ReadAllText(file).Split(":start -->", StringSplitOptions.None).Length - 1);
        File.AppendAllText(file, "\nUser added another rule after installation.\n");
        Assert.AreEqual(1, service.RemoveGuidance("agent-a"));
        Assert.AreEqual(original + "\nUser added another rule after installation.\n", File.ReadAllText(file));
        Assert.IsFalse(File.Exists(registration.InstructionPath));
        Assert.AreEqual(0, service.RemoveGuidance("agent-a"));
    }

    [TestMethod]
    public void RemovingRuleRestoresFileWithoutFinalNewline()
    {
        var file = Path.Combine(root, "no-newline.md");
        const string original = "User instruction without a final newline.";
        File.WriteAllText(file, original);
        Stable(service, "agent-a");
        service.InstallGuidance("agent-a", file);
        service.InstallGuidance("agent-a", file);
        service.RemoveGuidance("agent-a");
        Assert.AreEqual(original, File.ReadAllText(file));
    }

    [TestMethod]
    public void WorkspacesWithSameNodeIdHaveDifferentStableEntries()
    {
        var other = new RelayService(Path.Combine(root, "other")); other.Initialize();
        other.RegisterManual("agent-a", "manual", "另一个节点");
        var first = Stable(service, "agent-a"); var second = Stable(other, "agent-a");
        Assert.AreNotEqual(first, second);
        service.GuidancePrompt("agent-a"); other.GuidancePrompt("agent-a");
        StringAssert.Contains(File.ReadAllText(first), "节点甲");
        StringAssert.Contains(File.ReadAllText(second), "另一个节点");
    }

    [TestMethod]
    public void RelocationKeepsStableEntryAndRefreshesCurrentRoot()
    {
        var file = Path.Combine(service.Home, "AGENTS.md"); File.WriteAllText(file, "User rules.\n");
        var oldHome = service.Home; var instructions = Stable(service, "agent-a");
        service.InstallGuidance("agent-a", file);
        var moved = service.Relocate(Path.Combine(root, "moved")); moved.RefreshGuidance();
        Assert.AreEqual(instructions, moved.StableInstructionPath("agent-a"));
        StringAssert.Contains(File.ReadAllText(instructions), "当前中转站：" + moved.Home);
        Assert.DoesNotContain("当前中转站：" + oldHome, File.ReadAllText(instructions), StringComparison.Ordinal);
        Assert.AreEqual(Path.Combine(moved.Home, "AGENTS.md"), moved.GuidanceRegistrations().Single().InstructionFile);
        Assert.AreEqual(1, moved.RemoveAllGuidance());
        Assert.AreEqual("User rules.\n", File.ReadAllText(Path.Combine(moved.Home, "AGENTS.md")));
    }

    [TestMethod]
    public void DamagedOwnedBlockStopsRemovalRatherThanRestoringOldBackup()
    {
        var file = Path.Combine(root, "AGENTS.md"); File.WriteAllText(file, "User rule.\n");
        Stable(service, "agent-a"); service.InstallGuidance("agent-a", file);
        var damaged = File.ReadAllText(file).Replace(":end -->", ":user-edited -->", StringComparison.Ordinal);
        File.WriteAllText(file, damaged);
        Assert.AreEqual("guidance_block_changed", Assert.ThrowsExactly<RelayException>(() => service.RemoveGuidance("agent-a")).Code);
        Assert.AreEqual(damaged, File.ReadAllText(file));
        Assert.HasCount(1, service.GuidanceRegistrations());
    }

    [TestMethod]
    public void RemovingAllOwnedRulesKeepsUnrelatedInstructionText()
    {
        var file = Path.Combine(root, "AGENTS.md"); File.WriteAllText(file, "Original.\n");
        Stable(service, "agent-a"); Stable(service, "agent-b");
        service.InstallGuidance("agent-a", file); service.InstallGuidance("agent-b", file);
        File.AppendAllText(file, "User's latest rule.\n");
        Assert.AreEqual(2, service.RemoveAllGuidance());
        Assert.AreEqual("Original.\nUser's latest rule.\n", File.ReadAllText(file));
        Assert.IsEmpty(service.GuidanceRegistrations());
    }
}
