using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskBaton.Core;

/// <param name="Usable">False for entries the host lists but its command line cannot run, such as an account plan only the desktop signed in to.</param>
public sealed record AgentModelOption(string ProviderId, string ProviderName, string ModelId, string[] ReasoningLevels, bool Usable = true);
public sealed record AgentModelSelection(string ProviderId, string ModelId, string? ReasoningLevel);
/// <param name="Source">The host file Relay reads and writes for new conversations.</param>
public sealed record AgentModelCatalog(string AgentId, bool Supported, string? Source, AgentModelSelection? Current,
    IReadOnlyList<AgentModelOption> Options, string? Message = null);

/// <summary>
/// The model and reasoning level a host uses when Relay starts a new conversation. Continuing a conversation keeps that
/// conversation's own model: Zcode ignores its configured default on --resume and DSH applies its default only to
/// Agents without a session-specific selection, so Relay never passes a model on the command line.
/// </summary>
public sealed partial class RelayService
{
    private const string DshPatchHeader = "# TaskBaton bridge patch: applied after the desktop profile's patch layer.";

    public async Task<AgentModelCatalog> AgentModelsAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _ = Agents().SingleOrDefault(a => a.AgentId == agentId) ?? throw new RelayException("agent_not_found", "未找到 Agent：" + agentId);
        var recipe = CliExtension(agentId)?.Recipe;
        if (recipe is null) return new(agentId, false, null, null, [], "这个 Agent 没有桥接，新会话由你在它自己的窗口里建立，模型也在那里选。");
        return CliRecipes.OutputFormatFor(recipe) switch
        {
            "zcode-json" or "zcode-jsonl" => ZcodeModels(agentId, recipe),
            "dsh-jsonl" when DshRelayPatch(recipe) is { } patch => await DshModelsAsync(agentId, recipe, patch, cancellationToken),
            "dsh-jsonl" => new(agentId, false, null, null, [], "DSH 调用方案里没有 Relay 管理的补丁层（--patch 中转站\\bridges\\…），无法设置新会话默认模型。"),
            // Sessions are created by the desktop itself, so they start on its own default, the same as the user's.
            "dsh-desktop" => new(agentId, false, null, null, [], "Relay 通过 DSH 桌面新建会话，用的是桌面自己的默认模型；请在 DSH 桌面的设置里修改。续接的会话沿用它自己的模型。"),
            _ => new(agentId, false, null, null, [], "这个宿主的默认模型暂不能由 Relay 设置，请在宿主自己的设置里选择；续接的会话沿用它自己的模型。")
        };
    }

    public async Task<AgentModelCatalog> SetAgentModelAsync(string agentId, AgentModelSelection selection, CancellationToken cancellationToken = default)
    {
        var catalog = await AgentModelsAsync(agentId, cancellationToken);
        if (!catalog.Supported) throw new RelayException("agent_model_unsupported", catalog.Message!);
        var option = catalog.Options.SingleOrDefault(o => o.ProviderId == selection.ProviderId && o.ModelId == selection.ModelId)
            ?? throw new RelayException("agent_model_unknown", "宿主里没有这个供应商和模型。");
        if (!option.Usable) throw new RelayException("agent_model_unusable", "宿主的命令行不能使用这个套餐（只在桌面登录过），请换一个。");
        if (option.ReasoningLevels.Length > 0 && (selection.ReasoningLevel is null || !option.ReasoningLevels.Contains(selection.ReasoningLevel)))
            throw new RelayException("agent_model_reasoning", "思考强度只能是：" + string.Join(" / ", option.ReasoningLevels) + "。");
        var chosen = option.ReasoningLevels.Length == 0 ? selection with { ReasoningLevel = null } : selection;
        if (CliRecipes.OutputFormatFor(CliExtension(agentId)!.Recipe) is "dsh-jsonl") WriteDshDefault(catalog.Source!, chosen);
        else WriteZcodeDefault(catalog.Source!, chosen);
        return await AgentModelsAsync(agentId, cancellationToken);
    }

    /// <summary>The middle level of a model's scale: medium when offered, otherwise the centre entry.</summary>
    public static string? MiddleReasoningLevel(string[] levels) =>
        levels.Length == 0 ? null : levels.Contains("medium") ? "medium" : levels[levels.Length / 2];

    private static AgentModelCatalog ZcodeModels(string agentId, CliRecipe recipe)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = recipe.Create.Environment?.GetValueOrDefault("ZCODE_PERSONAL_PROVIDER_CONFIG_FILE")
            ?? Path.Combine(user, ".zcode", "v2", "provider_config.json");
        if (!File.Exists(path)) return new(agentId, false, path, null, [], "找不到 Zcode 的供应商配置 " + path + "，请先在 Zcode 桌面里配置一次模型。");
        var config = JsonNode.Parse(File.ReadAllText(path))!["config"]!;
        var builtinLevels = new Dictionary<(string, string), string[]>();
        if (recipe.Create.Environment?.GetValueOrDefault("ZCODE_BUILTIN_PROVIDER_CONFIG_FILE") is { } builtin && File.Exists(builtin))
            foreach (var rule in JsonNode.Parse(File.ReadAllText(builtin))!["config"]?["modelConfigRules"]?["builtinProviderModelRules"]?.AsArray() ?? [])
                if (Levels(rule?["config"]) is { Length: > 0 } levels)
                    builtinLevels[(rule!["providerId"]!.GetValue<string>(), rule["modelId"]!.GetValue<string>())] = levels;
        var modelRules = (config["modelConfigRules"]?["providerModelRules"]?.AsArray() ?? [])
            .ToDictionary(r => (r!["providerId"]!.GetValue<string>(), r["modelId"]!.GetValue<string>()), r => r!["config"]);
        // The command line can only use account plans it signed in to itself.
        var credentials = Path.Combine(user, ".zcode", "v2", "credentials.json");
        var signedIn = File.Exists(credentials)
            ? JsonNode.Parse(File.ReadAllText(credentials))!.AsObject().Select(p => p.Key)
                .Where(k => k.StartsWith("account-provider:", StringComparison.Ordinal) && k.EndsWith(":identity", StringComparison.Ordinal))
                .Select(k => k["account-provider:".Length..^":identity".Length]).ToHashSet(StringComparer.Ordinal)
            : [];
        var options = new List<AgentModelOption>();
        foreach (var rule in config["providerConfigRules"]?["providerRules"]?.AsArray() ?? [])
        {
            if (rule?["enabled"]?.GetValue<bool>() == false) continue;
            var provider = rule!["providerId"]!.GetValue<string>();
            var name = rule["providerName"]?.GetValue<string>() ?? provider;
            var models = rule["config"]?["modelOrder"]?.AsArray() ?? rule["config"]?["personalModelIds"]?.AsArray() ?? [];
            foreach (var model in models.Select(m => m!.GetValue<string>()))
            {
                var spec = modelRules.GetValueOrDefault((provider, model));
                if (spec?["enabled"]?.GetValue<bool>() == false) continue;
                options.Add(new(provider, name, model, Levels(spec) is { Length: > 0 } own ? own : builtinLevels.GetValueOrDefault((provider, model)) ?? [],
                    !provider.StartsWith("account:", StringComparison.Ordinal) || signedIn.Contains(provider)));
            }
        }
        var current = config["defaultModelSelection"] is JsonObject selected
            ? new AgentModelSelection(selected["providerId"]!.GetValue<string>(), selected["modelId"]!.GetValue<string>(),
                selected["options"]?["reasoningLevel"]?.GetValue<string>())
            : null;
        return new(agentId, true, path, current, options);

        static string[] Levels(JsonNode? spec) => spec?["optionSpecs"]?["reasoningLevel"]?["values"]?.AsArray()
            .Select(v => v!.GetValue<string>()).ToArray() ?? [];
    }

    private static void WriteZcodeDefault(string path, AgentModelSelection selection)
    {
        var raw = File.ReadAllText(path);
        File.Copy(path, path + ".bak-relay-model", true);
        var document = JsonNode.Parse(raw)!;
        var value = new JsonObject { ["providerId"] = selection.ProviderId, ["modelId"] = selection.ModelId };
        if (selection.ReasoningLevel is not null) value["options"] = new JsonObject { ["reasoningLevel"] = selection.ReasoningLevel };
        document["config"]!["defaultModelSelection"] = value;
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
            + (raw.EndsWith('\n') ? "\n" : ""), new UTF8Encoding(false));
    }

    private string? DshRelayPatch(CliRecipe recipe)
    {
        var bridges = Path.Combine(Home, "bridges") + Path.DirectorySeparatorChar;
        var arguments = recipe.Create.Arguments;
        for (var index = 0; index + 1 < arguments.Length; index++)
            if (arguments[index] == "--patch" && Path.GetFullPath(arguments[index + 1]).StartsWith(bridges, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(arguments[index + 1]);
        return null;
    }

    private static async Task<AgentModelCatalog> DshModelsAsync(string agentId, CliRecipe recipe, string patch, CancellationToken cancellationToken)
    {
        // The composed profile, printed without starting an Agent, lists every provider and model with its reasoning levels.
        var arguments = recipe.Create.Arguments.TakeWhile(a => a != "--json").Append("--dump-config");
        var dump = await RelayProcess.RunAsync(recipe.Create.Executable, arguments, null, TimeSpan.FromMinutes(1), cancellationToken,
            recipe.Create.Environment, recipe.Create.WorkingDirectory);
        if (dump.TimedOut || dump.ExitCode != 0)
            return new(agentId, false, patch, null, [], "读取 DSH 配置失败：" + dump.Error);
        var (options, current) = ParseDshConfig(dump.Output);
        return new(agentId, true, patch, current, options);
    }

    /// <summary>Reads the llm-pi-ai providers and agent-default-model entry from DSH's composed YAML.</summary>
    public static (List<AgentModelOption> Options, AgentModelSelection? Current) ParseDshConfig(string yaml)
    {
        var options = new List<AgentModelOption>();
        string? entry = null, provider = null, providerName = null, model = null, section = null;
        var levels = new List<string>();
        string? defaultProvider = null, defaultModel = null, defaultLevel = null;
        void Flush()
        {
            if (provider is not null && model is not null) options.Add(new(provider, providerName ?? provider, model, levels.ToArray()));
            model = null; levels = [];
        }
        foreach (var raw in yaml.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith('#') || line.Trim().Length == 0) continue;
            var indent = line.Length - line.TrimStart().Length;
            var text = line.Trim();
            if (indent == 0 && text.StartsWith("- id: ", StringComparison.Ordinal)) { Flush(); provider = null; entry = text[6..].Trim(); continue; }
            if (entry == "agent-default-model" && indent == 4)
            {
                var (key, value) = Pair(text);
                if (key == "provider") defaultProvider = value; else if (key == "model") defaultModel = value; else if (key == "reasoningEffort") defaultLevel = value;
                continue;
            }
            if (entry != "llm-pi-ai") continue;
            if (indent == 6 && text.EndsWith(':')) { Flush(); provider = text.TrimEnd(':').Trim('\''); providerName = null; continue; }
            if (indent == 8 && text.StartsWith("displayName:", StringComparison.Ordinal)) { providerName = Pair(text).Value; continue; }
            if (indent == 10 && text.StartsWith("- id: ", StringComparison.Ordinal)) { Flush(); model = text[6..].Trim().Trim('\''); section = null; continue; }
            if (indent == 12) section = text.EndsWith(':') ? text.TrimEnd(':') : null;
            if (indent == 14 && section == "reasoningEfforts" && model is not null)
            {
                var (key, value) = Pair(text);
                if (value is not (null or "null")) levels.Add(key.Trim('\''));
            }
        }
        Flush();
        var current = defaultProvider is null || defaultModel is null ? null : new AgentModelSelection(defaultProvider, defaultModel, defaultLevel);
        return (options, current);

        static (string Key, string? Value) Pair(string text)
        {
            var split = text.IndexOf(':');
            return split < 0 ? (text, null) : (text[..split].Trim(), split + 1 < text.Length ? text[(split + 1)..].Trim().Trim('\'') : null);
        }
    }

    private static void WriteDshDefault(string patch, AgentModelSelection selection)
    {
        // The patch layer is Relay's own file; it is rewritten whole so the default model entry is replaced, not duplicated.
        var lines = new List<string>
        {
            DshPatchHeader,
            "# It re-enables the credential store the desktop shares through DSH_HOME and sets the default model for new conversations.",
            "- id: credentials",
            "  disabled: false",
            "- id: agent-default-model",
            "  config:",
            "    provider: " + selection.ProviderId,
            "    model: " + selection.ModelId
        };
        if (selection.ReasoningLevel is not null) lines.Add("    reasoningEffort: " + selection.ReasoningLevel);
        Directory.CreateDirectory(Path.GetDirectoryName(patch)!);
        File.WriteAllText(patch, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }
}
