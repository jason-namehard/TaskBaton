using System.Text.Json;

namespace AgentRelay.Core;

/// <param name="Status">pass, warn or fail. A fail means unattended delivery cannot work until the user fixes it.</param>
public sealed record CliHealthCheck(string Id, string Status, string Message);

public sealed partial class RelayService
{
    /// <summary>Checks local prerequisites and, for DSH desktop, authenticates a read-only API request. Does not clear blockers.</summary>
    public async Task<IReadOnlyList<CliHealthCheck>> CliHealthAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var checks = CliHealth(agentId).ToList();
        var recipe = CliExtension(agentId)?.Recipe;
        if (recipe is null || CliRecipes.OutputFormatFor(recipe) != "dsh-desktop") return checks;
        var home = recipe.Create.Environment?.GetValueOrDefault("DSH_HOME");
        checks.Add(home is null
            ? new("host.desktop.connection", "fail", "调用方案缺少 DSH_HOME，尚不能进行桌面接口认证自检。")
            : await DshDesktopClient.CheckConnectionAsync(recipe.Create.Executable, home, cancellationToken));
        return checks;
    }

    /// <summary>Checks a recipe's local prerequisites without starting a model turn.</summary>
    public IReadOnlyList<CliHealthCheck> CliHealth(string agentId)
    {
        _ = Agents().SingleOrDefault(a => a.AgentId == agentId) ?? throw new RelayException("agent_not_found", "未找到 Agent：" + agentId);
        var extension = CliExtension(agentId);
        if (extension is null)
            return [new("recipe", "fail", "尚未登记 CLI 调用方案。可在 Agent 管理用“一键配置”让 Agent 调查并登记，或继续复制提示词手动交接。")];
        var recipe = extension.Recipe;
        var format = CliRecipes.OutputFormatFor(recipe);
        var checks = new List<CliHealthCheck>();
        foreach (var (name, command) in new[] { ("create", recipe.Create), ("resume", CliRecipes.ResumeCommand(recipe)) })
        {
            if (command is null) continue;
            checks.Add(ResolveExecutable(command.Executable) is { } found
                ? new($"{name}.executable", "pass", "程序存在：" + found)
                : new($"{name}.executable", "fail", "找不到程序 " + command.Executable + "。宿主可能已移动或卸载，请重新运行一键配置或编辑调用方案。"));
            foreach (var script in command.Arguments.Where(a => Path.IsPathFullyQualified(a) && !Directory.Exists(a)))
                if (!File.Exists(script)) checks.Add(new($"{name}.script", "fail", "找不到入口文件 " + script + "。宿主更新后路径可能已改变。"));
            if (command.WorkingDirectory is { } cwd && !Directory.Exists(cwd))
                checks.Add(new($"{name}.cwd", "fail", "工作目录不存在：" + cwd));
        }
        checks.Add(CliRecipes.ResumeCommand(recipe) is null
            ? new("resume", "warn", "方案没有续接命令：每轮都会新建会话，回稿不能回到同一对话。")
            : new("resume", "pass", "已配置续接命令。"));
        checks.AddRange(HostChecks(recipe, format));
        if (CliRecipes.IsZcode(recipe)) checks.AddRange(ZcodeParityChecks(recipe));
        if (extension.Block is { } block) checks.Add(new("block", "fail", block.Message));
        checks.Add(new("verified.create", extension.CanCreate ? "pass" : "warn", extension.CanCreate
            ? "新建会话已验证。" : "新建会话尚未验证：探针通过并确认可见，或一次新建会话的真实交接写回回执后才会自动投递。"));
        if (CliRecipes.ResumeCommand(recipe) is not null)
            checks.Add(new("verified.resume", extension.CanResume ? "pass" : "warn", extension.CanResume
                ? "续接已验证。" : "续接尚未验证：续接探针通过并确认，或一次续接的真实交接写回回执后才会自动投递。"));
        return checks;
    }

    private static IEnumerable<CliHealthCheck> HostChecks(CliRecipe recipe, string format)
    {
        var arguments = recipe.Create.Arguments.Prepend(recipe.Create.Executable).ToArray();
        if (format == "claude-json")
        {
            if (arguments.Any(a => a.Replace('\\', '/').EndsWith("/codebuddy-lite-wb.mjs", StringComparison.OrdinalIgnoreCase)))
                yield return new("host.login", "fail", "WorkBuddy 桌面版没有对外可用的调用接口：自带的 CLI 单独启动用不了桌面登录，打开中的对话也不接受外部提示词。请给 WorkBuddy 选择“无”，用复制提示词手动交接。");
            if (!arguments.Any(a => a is "-y" or "--dangerously-skip-permissions" or "--permission-mode"))
                yield return new("host.permission", "fail", "缺少非交互权限参数（-y 或 --permission-mode acceptEdits）：无人值守时 Agent 无法写回执。");
        }
        if (format == "dsh-jsonl")
            yield return new("host.parity.dsh", "warn", "DSH 命令行（headless）与桌面的工具集不同：它不能续接桌面建的会话；Relay 新建的会话在桌面继续时，第一轮会整段重新计费，之后再命中缓存。改用 dsh-desktop 调用方案，可由运行中的 DSH 桌面执行回合，两边请求一致。");
        if (format == "acp" && Path.GetFileNameWithoutExtension(recipe.Create.Executable)
                .Equals("codex-acp", StringComparison.OrdinalIgnoreCase))
        {
            yield return RelayProcess.ResolveExecutable("codex") is null
                ? new("host.codex.cli", "fail", "Codex ACP 适配器还需要 Codex CLI 本体。请先安装 Codex，再重新检查。")
                : new("host.codex.cli", "pass", "已找到 Codex CLI。");
            yield return RelayProcess.ResolveExecutable("codex-acp") is null
                ? new("host.codex.adapter", "fail", "缺少 codex-acp 适配器；请安装 @agentclientprotocol/codex-acp 后再检查。")
                : new("host.codex.adapter", "pass", "已找到 codex-acp 适配器。");
        }
        if (format == "dsh-desktop")
        {
            var dshHome = recipe.Create.Environment?.GetValueOrDefault("DSH_HOME");
            yield return dshHome is null
                ? new("host.desktop.auth", "fail", "调用方案缺少 DSH_HOME 环境变量，Relay 找不到 DSH 的数据目录。")
                : DshDesktopClient.SecretProblem(dshHome) is { } problem
                ? new("host.desktop.auth", "fail", problem)
                : new("host.desktop.auth", "pass", "DSH 桌面的本机签名材料可读取；这不代表接口认证已经通过，认证结果见连接自检。密钥只在内存中使用，不保存、不显示。");
            yield return DshDesktopClient.DesktopRunning(recipe.Create.Executable)
                ? new("host.desktop.running", "pass", "DSH 桌面正在运行。")
                : new("host.desktop.running", "warn", "DSH 桌面没有运行。Relay 通过运行中的桌面执行回合，发送时需要它开着。");
        }
        if (format is "zcode-json" or "zcode-jsonl")
        {
            foreach (var (name, command) in new[] { ("create", recipe.Create), ("resume", CliRecipes.ResumeCommand(recipe)) })
            {
                if (command is null) continue;
                var index = Array.IndexOf(command.Arguments, "--toolsearch-mode");
                if (index < 0 || index + 1 >= command.Arguments.Length || command.Arguments[index + 1] != "local")
                    yield return new($"host.toolsearch.{name}", "fail", "Zcode 命令行没有声明 --toolsearch-mode local：桌面按需加载工具，命令行默认把全部工具定义随每次请求发送，大型 MCP 会让每次请求多出约 26 万 token。请重新导入调用方案（Relay 会自动补上）。");
            }
            var config = recipe.Create.Environment?.GetValueOrDefault("ZCODE_PERSONAL_PROVIDER_CONFIG_FILE")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode", "v2", "provider_config.json");
            // The desktop app registers account plans itself; a standalone CLI needs the identity record that `zcode login` writes.
            // A default model on a provider with its own API key needs no account plan.
            yield return ZcodeDefaultProviderName(config) is { } own
                ? new("host.account", "pass", "默认模型使用自带 key 的供应商（" + own + "），不需要账号套餐。")
                : HasZcodeAccountProvider(Path.Combine(Path.GetDirectoryName(config)!, "credentials.json"))
                ? new("host.account", "pass", "命令行已登记 Z.AI 账号套餐。")
                : new("host.account", "fail", "Zcode 命令行没有登记账号套餐，可选模型为空，新建会话会报“Select a model”。桌面已登录不等于命令行可用：请在终端运行一次 zcode login zai（并设置 ZCODE_BUILTIN_PROVIDER_CONFIG_FILE 指向安装目录下 resources/config/provider/zcode-builtin.json），或续接已选过模型的会话。");
            yield return HasZcodeDefaultModel(config)
                ? new("host.model", "pass", "已设置命令行默认模型（" + config + "）。")
                : new("host.model", "warn", "Zcode 命令行没有默认模型；登录后会自动写入，在桌面切换模型不会写入这一项。");
        }
    }

    /// <summary>Launch settings whose difference from the desktop makes a continued conversation miss the prompt cache.</summary>
    private IEnumerable<CliHealthCheck> ZcodeParityChecks(CliRecipe recipe)
    {
        const string Reimport = "请重新导入调用方案，Relay 会按桌面设置补齐。";
        var desktopWorkflow = CliRecipes.ZcodeDesktopSetting(ZcodeUserDirectory, "dynamicWorkflowMode");
        foreach (var (name, command) in new[] { ("create", recipe.Create), ("resume", CliRecipes.ResumeCommand(recipe)) })
        {
            if (command is null) continue;
            if (desktopWorkflow is not null && CliRecipes.ZcodeArgument(command, "--workflow-mode") != desktopWorkflow)
                yield return new($"host.parity.workflow.{name}", "warn", $"桌面的动态工作流设置是 {desktopWorkflow}，命令行没有按它启动：两边的技能列表不同，续接桌面会话时缓存会整段失效。" + Reimport);
            var genUi = CliRecipes.ZcodeVisualizationsDirectory(command, ZcodeUserDirectory);
            if (CliRecipes.ZcodeArgument(command, "--surface") != "desktop" ||
                command.Environment?.GetValueOrDefault("ZCODE_GEN_UI_OUTPUT_ROOT") is not { Length: > 0 } root || !SamePath(Path.GetFullPath(root), Path.GetFullPath(genUi)))
                yield return new($"host.parity.genui.{name}", "warn", $"命令行没有用 --surface desktop 和 ZCODE_GEN_UI_OUTPUT_ROOT={genUi} 启动：系统提示里的生成界面说明与桌面不同，续接桌面会话时缓存会整段失效。" + Reimport);
        }
        if (CliRecipes.ZcodeDesktopDataDirectory(ZcodeUserDirectory) is { } data)
        {
            var desktop = Path.Combine(data, "provider_config.json");
            var used = recipe.Create.Environment?.GetValueOrDefault("ZCODE_PERSONAL_PROVIDER_CONFIG_FILE") ?? Path.Combine(ZcodeUserDirectory, "v2", "provider_config.json");
            if (!SamePath(Path.GetFullPath(used), Path.GetFullPath(desktop)))
                yield return new("host.parity.provider", "warn", $"命令行读的供应商配置是 {used}，桌面用的是 {desktop}：两边可选的模型与供应商不同。请把 ZCODE_PERSONAL_PROVIDER_CONFIG_FILE 设为桌面这份。");
        }
        // The desktop sends memory.enabled=false when its memory is off; when on, it falls back to this shared setting.
        var desktopMemory = CliRecipes.ZcodeDesktopSetting(ZcodeUserDirectory, "memoryEnabled") == "true";
        var cliConfig = Path.Combine(ZcodeUserDirectory, "cli", "config.json");
        var cliMemory = ZcodeCliMemory(cliConfig);
        if (desktopMemory == cliMemory)
            yield return new("host.parity.memory", "pass", desktopMemory ? "桌面与命令行都开启了记忆。" : "桌面与命令行都关闭了记忆。");
        else if (!desktopMemory)
            yield return new("host.parity.memory", "warn", $"桌面关闭了记忆，命令行仍会在系统提示里加入记忆说明，续接桌面会话时缓存会整段失效。请在 {cliConfig} 里设置 \"features\": {{ \"memory\": false }}；以后若在桌面开启记忆，要同时删掉这一项，否则桌面的记忆开关不起作用。");
        else
            yield return new("host.parity.memory", "warn", $"桌面开启了记忆，但 {cliConfig} 里的 features.memory 为 false，这一项会让桌面的记忆也不起作用。请删掉它。");
    }

    private static bool ZcodeCliMemory(string path)
    {
        if (!File.Exists(path)) return true;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return !(document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("features", out var features) &&
                features.ValueKind == JsonValueKind.Object && features.TryGetProperty("memory", out var memory) && memory.ValueKind == JsonValueKind.False);
        }
        catch (Exception exception) when (exception is JsonException or IOException) { return true; }
    }

    // Reads key names only; credential values are never loaded into Relay.
    private static bool HasZcodeAccountProvider(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject()
                .Any(p => p.Name.StartsWith("account-provider:", StringComparison.Ordinal) && p.Name.EndsWith(":identity", StringComparison.Ordinal));
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The name of the default model's provider when that provider carries its own API key; key values are not read.</summary>
    private static string? ZcodeDefaultProviderName(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("config", out var config) ||
                !config.TryGetProperty("defaultModelSelection", out var selection) || selection.ValueKind != JsonValueKind.Object ||
                !selection.TryGetProperty("providerId", out var id) ||
                !config.TryGetProperty("providerConfigRules", out var rules) || rules.ValueKind != JsonValueKind.Object ||
                !rules.TryGetProperty("providerRules", out var providers) || providers.ValueKind != JsonValueKind.Array) return null;
            foreach (var provider in providers.EnumerateArray())
            {
                if (!provider.TryGetProperty("providerId", out var candidate) || candidate.GetString() != id.GetString()) continue;
                if (provider.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False) return null;
                return provider.TryGetProperty("config", out var settings) && settings.TryGetProperty("access", out var access) &&
                    access.ValueKind == JsonValueKind.Object && access.TryGetProperty("apiKey", out _)
                    ? provider.TryGetProperty("providerName", out var name) && name.GetString() is { Length: > 0 } text ? text : id.GetString()
                    : null;
            }
            return null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    private static bool HasZcodeDefaultModel(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("config", out var config) &&
                config.TryGetProperty("defaultModelSelection", out var selection) && selection.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; } // An unreadable host file means the CLI cannot use it either.
    }

    private static string? ResolveExecutable(string executable) => RelayProcess.ResolveExecutable(executable);
}
