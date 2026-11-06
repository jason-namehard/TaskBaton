using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRelay.Core;

public sealed record CliCommand(string Executable, string[] Arguments, string? Input, string? WorkingDirectory,
    Dictionary<string, string>? Environment = null);
public sealed record CliRecipe(int Version, string Name, string Host, CliCommand Create, CliCommand? Resume,
    string[] Dependencies, string SupportedVersions, string Source, string License, string Limitations,
    string OutputFormat = "relay-json", string? Login = null);
public sealed record CliProbe(string ProbeId, string Operation, string Marker, string? SessionId,
    bool MachinePassed, bool UserConfirmed, string? Error, DateTimeOffset TestedAt, string? Diagnostic = null,
    bool? FileWritten = null, bool SessionCleaned = false, string? CleanupWarning = null,
    CliSidebarSyncResult? SidebarSync = null);
public sealed record ProbeArtifact(string ProbeId, string Operation, string Marker, string? SessionId,
    SessionDiscoverySource? Source, bool OwnedSession, string? SessionDirectory = null, CliCommand? CreateCommand = null,
    bool Completed = false, string? OwnershipWarning = null, string[]? DshAttachmentIds = null,
    string? OutputFormat = null);
/// <summary>A real handoff turn sent through this recipe whose Agent then wrote the turn's receipt.</summary>
public sealed record CliDeliveryEvidence(string TaskId, int Turn, string? SessionId, DateTimeOffset VerifiedAt);
/// <summary>A host condition that makes further unattended calls pointless until the user resolves it.</summary>
public sealed record CliBlock(string Kind, string Message, DateTimeOffset At);
public sealed record CliSidebarSync(bool Enabled = true, bool ActivateWindow = false);
public sealed record CliSidebarSyncResult(string Status, string? IndexPath = null, string? Message = null,
    string? BackupPath = null, bool? WindowActivated = null);
public sealed record CliExtensionRecord(string AgentId, string Revision, CliRecipe Recipe,
    CliProbe? CreateProbe, CliProbe? ResumeProbe, CliDeliveryEvidence? CreateEvidence = null,
    CliDeliveryEvidence? ResumeEvidence = null, CliBlock? Block = null, ProbeArtifact[]? ProbeArtifacts = null,
    CliSidebarSync? SidebarSync = null)
{
    [JsonIgnore] private bool CreateProbed => CreateProbe is { MachinePassed: true, UserConfirmed: true, FileWritten: true };
    [JsonIgnore] public bool CanCreate => CreateProbed || CreateEvidence is not null;
    [JsonIgnore] public bool CanResume => CliRecipes.ResumeCommand(Recipe) is not null && (ResumeEvidence is not null || CreateProbed &&
        ResumeProbe is { MachinePassed: true, UserConfirmed: true, FileWritten: true } &&
        !string.IsNullOrWhiteSpace(CreateProbe!.SessionId) && ResumeProbe.SessionId == CreateProbe.SessionId);
}

public static class CliRecipes
{
    // Protocol transports receive the session ID in their request, so creation and continuation share an entry point.
    public static CliCommand? ResumeCommand(CliRecipe recipe) => recipe.Resume ??
        (OutputFormatFor(recipe) is "acp" or "dsh-desktop" ? recipe.Create : null);
    public static readonly string[] OutputFormats = ["relay-json", "dsh-jsonl", "zcode-json", "zcode-jsonl", "hermes-jsonl", "claude-json", "text", "acp", "dsh-desktop"];

    public static bool IsZcode(CliRecipe recipe) => recipe.Create.Arguments.Prepend(recipe.Create.Executable)
        .Select(value => value.Replace('\\', '/').Split('/').Last())
        .Any(p => p.Equals("zcode.cjs", StringComparison.OrdinalIgnoreCase) || p.Equals("zcode", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("zcode.exe", StringComparison.OrdinalIgnoreCase) || p.Equals("zcode.cmd", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Zcode builds the tool set and system prompt at every launch instead of storing them with the conversation, and the
    /// provider reuses its prompt cache only for an identical request prefix. A command-line turn that continues a desktop
    /// conversation must therefore launch the way the desktop does; otherwise that turn, and the desktop's next one, pay for
    /// the whole history again. Three launch differences are fixed here:
    /// - Tool search. The desktop loads MCP and skill tool schemas on demand (local). The command line defaults to off and
    ///   sends every schema with every request, about 260k input tokens on a machine with a large MCP server. The mode is
    ///   always local, not auto, because auto depends on whether the provider advertises native tool search.
    /// - Dynamic workflows. The desktop setting dynamicWorkflowMode decides whether the dynamic-workflows skill is listed.
    ///   The command line defaults to disabled.
    /// - Generated UI. The desktop always runs as surface desktop, and its system prompt names its visualizations folder. The
    ///   command line defaults to surface terminal, and even as desktop adds that section only when ZCODE_GEN_UI_OUTPUT_ROOT
    ///   is set. The surface affects nothing else in the request.
    /// The fourth difference, memory, is a command-line config setting the desktop also reads, so doctor reports it instead.
    /// </summary>
    public static CliRecipe WithZcodeDesktopParity(CliRecipe recipe, string zcodeUserDirectory)
    {
        if (!IsZcode(recipe)) return recipe;
        var workflowMode = ZcodeDesktopSetting(zcodeUserDirectory, "dynamicWorkflowMode");
        CliCommand Ensure(CliCommand command)
        {
            var arguments = command.Arguments.ToList();
            if (!arguments.Contains("--surface")) arguments.AddRange(["--surface", "desktop"]);
            if (!arguments.Contains("--toolsearch-mode")) arguments.AddRange(["--toolsearch-mode", "local"]);
            if (workflowMode is not null && !arguments.Contains("--workflow-mode")) arguments.AddRange(["--workflow-mode", workflowMode]);
            var launch = command with { Arguments = [.. arguments] };
            if (ZcodeArgument(launch, "--surface") != "desktop") return launch;
            var environment = launch.Environment is null ? new Dictionary<string, string>() : new(launch.Environment);
            environment["ZCODE_GEN_UI_OUTPUT_ROOT"] = ZcodeVisualizationsDirectory(launch, zcodeUserDirectory);
            return launch with { Environment = environment };
        }
        return recipe with { Create = Ensure(recipe.Create), Resume = recipe.Resume is null ? null : Ensure(recipe.Resume) };
    }

    public static string? ZcodeArgument(CliCommand command, string name)
    {
        var index = Array.IndexOf(command.Arguments, name);
        return index >= 0 && index + 1 < command.Arguments.Length ? command.Arguments[index + 1] : null;
    }

    /// <summary>
    /// The desktop's data folder: &lt;dataBaseDir&gt;/.zcode/v2 when the user moved it, else ~/.zcode/v2. Its provider file and
    /// its generated UI (visualizations) live there; its settings always stay in ~/.zcode/v2.
    /// </summary>
    public static string? ZcodeDesktopDataDirectory(string zcodeUserDirectory, bool strict = false) =>
        ZcodeDesktopSetting(zcodeUserDirectory, "dataBaseDir", strict) is { Length: > 0 } moved ? Path.Combine(moved, ".zcode", "v2")
        : File.Exists(Path.Combine(zcodeUserDirectory, "v2", "setting.json")) ? Path.Combine(zcodeUserDirectory, "v2") : null;

    public static string ZcodeVisualizationsDirectory(CliCommand command, string zcodeUserDirectory) => Path.Combine(
        ZcodeDesktopDataDirectory(zcodeUserDirectory) ?? Path.GetDirectoryName(command.Environment?.GetValueOrDefault("ZCODE_PERSONAL_PROVIDER_CONFIG_FILE")
            ?? Path.Combine(zcodeUserDirectory, "v2", "provider_config.json"))!, "visualizations");

    /// <summary>Reads one top-level value from the desktop's settings (~/.zcode/v2/setting.json); null when absent or unreadable.</summary>
    public static string? ZcodeDesktopSetting(string zcodeUserDirectory, string name, bool strict = false)
    {
        var path = Path.Combine(zcodeUserDirectory, "v2", "setting.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (strict && document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("Zcode desktop settings must be an object.");
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(name, out var value)) return null;
            if (strict && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new JsonException($"Zcode desktop setting {name} must be a string or null.");
            return value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };
        }
        catch (Exception exception) when (!strict && exception is (JsonException or IOException)) { return null; }
    }

    public static string OutputFormatFor(CliRecipe recipe)
    {
        if (recipe.OutputFormat != "relay-json") return recipe.OutputFormat;
        // Earlier native recipes did not have output_format. Recognize only the installed CLI's documented flags.
        var command = recipe.Create;
        var programs = command.Arguments.Prepend(command.Executable)
            .Select(value => value.Replace('\\', '/').Split('/').Last()).ToArray();
        if (programs.Contains("zcode.cjs", StringComparer.OrdinalIgnoreCase) && command.Arguments.Contains("--json"))
            return "zcode-json";
        if (programs.Any(p => p is "hermes" or "hermes.exe" or "hermes.cmd") && command.Arguments.Contains("stream-json"))
            return "hermes-jsonl";
        if (programs.Any(p => p is "dsh" or "dsh.cmd" or "dsh.exe" or "cli.js") &&
            command.Arguments.Contains("headless") && command.Arguments.Contains("--json")) return "dsh-jsonl";
        if (programs.Any(p => p is "codebuddy" or "codebuddy.exe" or "codebuddy-lite-wb.mjs") &&
            command.Arguments.Contains("--output-format") && command.Arguments.Contains("json")) return "claude-json";
        return "relay-json";
    }

    public static string ImageDeliveryMode(CliRecipe recipe) => OutputFormatFor(recipe) switch
    {
        "acp" => "acp",
        "dsh-desktop" => "dsh-desktop",
        _ => "file-path"
    };
    public const string Notice = "CLI 接口扩充用于接入经验证的第三方命令行或桥接方案。可用性取决于目标软件开放的接口、版本、运行环境及会话管理能力，并非所有 Agent 均支持自动调用或原会话续接。扩充方案须完成检查与测试后方可启用；未接通时仍可使用手动交接。";

    public static CliRecipe Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024)
            throw new RelayException("cli_recipe_invalid", "调用方案不能超过 64 KB。");
        CliRecipe recipe;
        try
        {
            recipe = JsonSerializer.Deserialize<CliRecipe>(json, new JsonSerializerOptions(RelayJson.Options)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                RespectRequiredConstructorParameters = true
            }) ?? throw new JsonException("Expected an object.");
        }
        catch (JsonException ex)
        {
            throw new RelayException("cli_recipe_invalid", "调用方案 JSON 格式不正确：" + ex.Message);
        }
        // Imported recipes are external input. Validate the invocation contract before storing it.
        if (recipe.Version != 1 || string.IsNullOrWhiteSpace(recipe.Name) || string.IsNullOrWhiteSpace(recipe.Host) ||
            recipe.Create is null || recipe.Dependencies is null || recipe.Dependencies.Any(x => x is null) ||
            recipe.SupportedVersions is null || recipe.Source is null || recipe.License is null || recipe.Limitations is null)
            throw new RelayException("cli_recipe_invalid", "方案需要 version=1、名称、宿主、create、dependencies 和来源/版本/许可证/限制说明。未知说明请填空字符串。");
        if (!OutputFormats.Contains(recipe.OutputFormat))
            throw new RelayException("cli_recipe_invalid", "不支持的输出格式。可选：" + string.Join(", ", OutputFormats));
        // An ACP command only starts the agent, and a DSH desktop command only names the running app; Relay sends the
        // prompt and session over the protocol.
        var acp = recipe.OutputFormat is "acp" or "dsh-desktop";
        ValidateCommand(recipe.Create, false, acp);
        if (recipe.Resume is not null) ValidateCommand(recipe.Resume, true, acp);
        return recipe;
    }

    private static void ValidateCommand(CliCommand command, bool resume, bool acp)
    {
        if (string.IsNullOrWhiteSpace(command.Executable) || command.Arguments is null || command.Arguments.Any(a => a is null) ||
            (command.WorkingDirectory is not null && !Path.IsPathFullyQualified(command.WorkingDirectory)))
            throw new RelayException("cli_recipe_invalid", "命令需要 executable、arguments 字符串数组；working_directory 应为绝对路径或 null。");
        if (command.Environment is not null && command.Environment.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null))
            throw new RelayException("cli_recipe_invalid", "environment 必须是非空变量名到字符串值的对象。只配置非敏感运行环境，不导入密钥。");
        var templates = command.Arguments.Append(command.Input ?? "").ToArray();
        if (acp)
        {
            if (command.Input is not null || templates.Any(t => t.Contains("{prompt}", StringComparison.Ordinal) || t.Contains("{session_id}", StringComparison.Ordinal)))
                throw new RelayException("cli_recipe_invalid", "acp 与 dsh-desktop 方案只写宿主程序，input 为 null；提示词和会话由 Relay 通过协议传递，不使用 {prompt} 或 {session_id}。");
            return;
        }
        if (!templates.Any(t => t.Contains("{prompt}", StringComparison.Ordinal)))
            throw new RelayException("cli_recipe_invalid", "每条命令必须在 arguments 或 input 中包含 {prompt}。");
        if (resume && !templates.Any(t => t.Contains("{session_id}", StringComparison.Ordinal)))
            throw new RelayException("cli_recipe_invalid", "续接命令必须包含 {session_id}，不能以创建新会话代替续接。");
        if (!resume && templates.Any(t => t.Contains("{session_id}", StringComparison.Ordinal)))
            throw new RelayException("cli_recipe_invalid", "新会话命令不能引用 {session_id}。");
    }

    public static string CreatePrompt(string host) => $$"""
        请为宿主 {{host}} 提供你已核实可用的 CLI 或本机桥接调用方案，仅返回下列 JSON 格式。
        不执行命令，不安装桥接，不修改软件，不读取或返回密钥。无法确认可行方案时请直接说明不能提供，不编造命令。
        executable 是本机可执行文件或 PATH 中的命令；arguments 每项是一个参数，不拼接 shell 命令。
        {prompt} 由 Relay 替换为文本；{session_id} 仅用于 resume；{relay_home} 可用于参数或 input。
        input 为标准输入模板或 null；working_directory 为绝对路径或 null。
        调用必须将请求交给真实 Agent 会话。Relay 已支持这些 output_format：
        relay-json：独立桥接器返回 {"session_id":"真实标识","reply":"真实回复","accepted":true}；
        dsh-jsonl：DSH --profile headless --json；zcode-json：Zcode -p {prompt} --json --surface desktop --toolsearch-mode local（命令行默认不按需加载工具，必须声明；Relay 导入时会按桌面设置补上 --workflow-mode 和 ZCODE_GEN_UI_OUTPUT_ROOT，让续接桌面会话时命中缓存）；
        hermes-jsonl：Hermes chat -q {prompt} --format stream-json；claude-json：CodeBuddy/WorkBuddy -p --output-format json；
        text：只输出最终回复的普通文本命令，可测试回复，但无会话 ID 时不能测试同会话续接。
        acp：宿主支持 Agent Client Protocol（如 hermes acp、codebuddy --acp）时优先使用；命令只启动 ACP 服务，不含 {prompt}/{session_id}，input 为 null，resume 填与 create 相同的命令；Relay 通过协议新建或续接会话。
        dsh-desktop：DSH 优先使用。由运行中的 DSH 桌面执行回合，请求与桌面完全一致；executable 为 DeepSeek Harness.exe 的完整路径，arguments 为空数组，environment 设 DSH_HOME，working_directory 为新会话的工作区，resume 与 create 相同。Relay 用 DSH 桌面自己的签名密钥登录它的本机接口，须先向用户说明并取得同意。
        原生输出由 Relay 转换，不要求目标软件改成 relay-json，也不需要为已支持的格式额外编写桥接。
        探针要求真实回复准确返回短标识；不得由脚本自行生成回复。退出成功表示本次调用结束，不表示文件任务完成，任务仍以回执为准。
        environment 可提供非敏感环境变量，如宿主数据根目录；不要输出或导入认证信息。
        create 必须创建一个用户可查看的会话。resume 必须续接传入的原会话，无法续接时填 null。
        dependencies、supported_versions、source、license、limitations 如实说明；不要假定所有宿主都可自动调用。
        {
          "version":1,"name":"方案名称","host":{{JsonSerializer.Serialize(host)}},"output_format":"按实际输出选择",
          "create":{"executable":"本机程序路径","arguments":["{prompt}"],"input":null,"working_directory":null},
          "resume":null,"dependencies":[],"supported_versions":"","source":"","license":"","limitations":""
        }
        """;

    internal static async Task<ProcessResult> InvokeAsync(CliCommand command, string format, string prompt, string? sessionId,
        string home, TimeSpan timeout, CancellationToken cancellationToken, IReadOnlyList<TaskImage>? images = null) =>
        await InvokePreparedAsync(command, format, prompt, sessionId, home, timeout, cancellationToken,
            await ImageDelivery.PrepareAsync(images, cancellationToken));

    internal static Task<ProcessResult> InvokePreparedAsync(CliCommand command, string format, string prompt, string? sessionId,
        string home, TimeSpan timeout, CancellationToken cancellationToken, IReadOnlyList<PreparedTaskImage> images)
    {
        if (format == "acp")
            return AcpClient.RunAsync(command, prompt, sessionId, command.WorkingDirectory ?? home, timeout, cancellationToken, images);
        if (format == "dsh-desktop")
            return DshDesktopClient.RunAsync(command, prompt, sessionId, command.WorkingDirectory ?? home, timeout, cancellationToken, images);
        prompt = ImageDelivery.AddFileInstructions(prompt, images);
        string Expand(string template) => template.Replace("{session_id}", sessionId ?? "", StringComparison.Ordinal)
            .Replace("{relay_home}", home, StringComparison.Ordinal).Replace("{prompt}", prompt, StringComparison.Ordinal);
        return RelayProcess.RunAsync(command.Executable, command.Arguments.Select(Expand),
            command.Input is null ? null : Expand(command.Input), timeout, cancellationToken,
            environment: command.Environment, workingDirectory: command.WorkingDirectory);
    }

    internal static (string? SessionId, string Reply)? Receipt(ProcessResult process, string outputFormat = "relay-json") =>
        CliOutput.Read(process, outputFormat);

}

public sealed partial class RelayService
{
    public CliExtensionRecord? CliExtension(string agentId) => store.Read(s => ReadCliExtension(s, agentId));
    public List<CliExtensionRecord> CliExtensions() => store.Read(s => s.Query(
        "SELECT json FROM cli_extensions ORDER BY agent_id", r => RelayJson.Read<CliExtensionRecord>(r.GetString(0))));
    public string CliRecipePrompt(string agentId) => CliRecipes.CreatePrompt(store.Read(s => Agent(s, agentId).Host));
    public string PreferredDeliveryAdapter(string recipientId) => CliExtension(recipientId) is { CanCreate: true }
        ? "cli:" + recipientId : "amq-cli";

    /// <summary>Zcode's per-user folder (~/.zcode) holding the desktop settings and the command line's own config.</summary>
    public string ZcodeUserDirectory { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode");

    public CliExtensionRecord ImportCliRecipe(string agentId, string json)
    {
        var recipe = CliRecipes.WithZcodeDesktopParity(CliRecipes.Parse(json), ZcodeUserDirectory);
        return store.Write(s =>
        {
            var agent = Agent(s, agentId);
            if (!string.Equals(recipe.Host, agent.Host, StringComparison.OrdinalIgnoreCase))
                throw new RelayException("cli_recipe_host_mismatch", "调用方案的 host 与选中的 Agent 宿主不一致。");
            var previous = ReadCliExtension(s, agentId);
            var record = new CliExtensionRecord(agentId, Guid.NewGuid().ToString(), recipe, null, null,
                ProbeArtifacts: previous?.ProbeArtifacts, SidebarSync: previous?.SidebarSync);
            SaveCliExtension(s, record);
            return record;
        });
    }

    public void RemoveCliExtension(string agentId) => store.Write(s =>
        s.Execute("DELETE FROM cli_extensions WHERE agent_id=$id", ("$id", agentId)));

    public void InvalidateCliExtension(string agentId) => store.Write(s =>
    {
        InvalidateCliExtension(s, agentId);
        return true;
    });

    private static void InvalidateCliExtension(StoreSession s, string agentId)
    {
        if (ReadCliExtension(s, agentId) is { } record)
            SaveCliExtension(s, record with { Revision = Guid.NewGuid().ToString(), CreateProbe = null, ResumeProbe = null,
                CreateEvidence = null, ResumeEvidence = null, Block = null });
    }

    public CliExtensionRecord ClearCliBlock(string agentId) => store.Write(s =>
    {
        var record = ReadCliExtension(s, agentId) ?? throw new RelayException("cli_recipe_missing", "未找到调用方案。");
        SaveCliExtension(s, record with { Block = null });
        return record with { Block = null };
    });

    public CliExtensionRecord SetCliSidebarSync(string agentId, bool enabled, bool activateWindow) => store.Write(s =>
    {
        var record = ReadCliExtension(s, agentId) ?? throw new RelayException("cli_recipe_missing", "未找到调用方案。");
        var next = record with { SidebarSync = new CliSidebarSync(enabled, activateWindow) };
        SaveCliExtension(s, next);
        return next;
    });

    // A probe starts the host, makes one tool call and replies; the 30-second transport timeout is far too short for that.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(3);

    public async Task<CliProbe> RunCliProbeAsync(string agentId, string operation, CancellationToken cancellationToken = default)
    {
        if (operation is not ("create" or "resume")) throw new RelayException("cli_probe_operation", "探针类型应为 create 或 resume。");
        var record = CliExtension(agentId) ?? throw new RelayException("cli_recipe_missing", "请先导入 CLI 调用方案。");
        if (operation == "resume" && (record.CreateProbe is not { MachinePassed: true, UserConfirmed: true } || CliRecipes.ResumeCommand(record.Recipe) is null || string.IsNullOrWhiteSpace(record.CreateProbe.SessionId)))
            throw new RelayException("cli_probe_not_ready", "请先完成新会话探针并确认可见；续接还需要真实会话 ID 和续接命令。");
        if (operation == "resume" && record.CreateProbe!.SessionCleaned)
            throw new RelayException("cli_probe_cleaned", "探针会话已清理；如需重新测试，请先运行新会话探针。");
        var marker = "RELAY-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var probeId = Guid.NewGuid().ToString();
        var sessionId = operation == "resume" ? record.CreateProbe!.SessionId : null;
        var command = operation == "create" ? record.Recipe.Create : CliRecipes.ResumeCommand(record.Recipe)!;
        var snapshot = CaptureProbeSessions(agentId);
        var artifact = new ProbeArtifact(probeId, operation, marker, null, snapshot.Source, false, CreateCommand: command,
            OutputFormat: CliRecipes.OutputFormatFor(record.Recipe));
        store.Write(s =>
        {
            var current = ReadCliExtension(s, agentId);
            if (current is null || current.Revision != record.Revision || current.CreateProbe != record.CreateProbe || current.ResumeProbe != record.ResumeProbe)
                throw new RelayException("cli_recipe_changed", "调用方案或探针已变化，请重新测试。");
            SaveCliExtension(s, current with { ProbeArtifacts = [.. (current.ProbeArtifacts ?? []), artifact] });
            return true;
        });
        // Handoffs are completed by a receipt file, so a probe must show the unattended Agent can write one.
        var probes = Path.Combine(Home, ".cli-probes"); Directory.CreateDirectory(probes);
        var probeFile = Path.Combine(probes, marker + ".txt");
        var prompt = "Create the file " + probeFile + " containing exactly " + marker + ", then reply with exactly " + marker + ". Do nothing else.";
        bool passed;
        bool? written = null;
        string? error;
        string? diagnostic = null;
        string? fileCleanupWarning = null;
        CliSidebarSyncResult? sidebarSync = null;
        CliFailure? failure = null;
        try
        {
            var process = await CliRecipes.InvokeAsync(command, CliRecipes.OutputFormatFor(record.Recipe), prompt, sessionId, Home,
                ProbeTimeout, cancellationToken);
            var output = $"exit_code: {process.ExitCode}\ntimed_out: {process.TimedOut}\nstdout:\n{process.Output}\nstderr:\n{process.Error}";
            diagnostic = output.Length <= 16 * 1024 ? output : output[^(16 * 1024)..];
            var receipt = CliRecipes.Receipt(process, CliRecipes.OutputFormatFor(record.Recipe));
            written = File.Exists(probeFile) && File.ReadAllText(probeFile).Trim() == marker;
            var replied = receipt is { } r && r.Reply.Trim() == marker && (operation == "create" || r.SessionId == sessionId);
            passed = replied && written == true;
            if (receipt is { } received)
            {
                sessionId = received.SessionId;
                var current = CliExtension(agentId);
                if (current is not null && current.Revision == record.Revision)
                    sidebarSync = ZcodeTaskIndexVisibility.Sync(current, sessionId, ZcodeUserDirectory, Home);
            }
            failure = passed ? null : replied ? CliFailureDiagnostics.PermissionRequired(record.Recipe) : CliFailureDiagnostics.Classify(process, record.Recipe);
            error = failure?.Message;
        }
        catch (Win32Exception ex)
        {
            passed = false;
            error = "无法启动 CLI：" + ex.Message;
        }
        finally
        {
            try { File.Delete(probeFile); }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            { fileCleanupWarning = "探针文件未能自动清理，可稍后重试：" + cleanupError.Message; }
        }
        var createdSession = operation == "create" || sessionId != record.CreateProbe!.SessionId ? sessionId : null;
        var ownership = createdSession is not null ? IdentifyProbeSession(snapshot, createdSession) : null;
        artifact = artifact with { SessionId = createdSession,
            OwnedSession = ownership?.Owned == true,
            SessionDirectory = ownership?.Owned == true && artifact.OutputFormat == "dsh-desktop"
                ? Path.GetFullPath(command.WorkingDirectory ?? Home) : ownership?.Directory,
            Completed = true,
            OwnershipWarning = ownership?.Warning };
        var cleanupWarning = ownership?.Warning;
        if (fileCleanupWarning is not null)
            cleanupWarning = cleanupWarning is null ? fileCleanupWarning : cleanupWarning + "\n" + fileCleanupWarning;
        var probe = new CliProbe(probeId, operation, marker, sessionId, passed, false, error, Now, diagnostic, written,
            CleanupWarning: cleanupWarning, SidebarSync: sidebarSync);
        return store.Write(s =>
        {
            var current = ReadCliExtension(s, agentId);
            if (current is null || current.Revision != record.Revision || current.CreateProbe != record.CreateProbe || current.ResumeProbe != record.ResumeProbe)
                throw new RelayException("cli_recipe_changed", "调用方案或探针已变化，本次结果不能用于启用。请重新测试。");
            var block = passed ? null : failure is { Blocking: true } ? new CliBlock(failure.Kind, failure.Message, Now) : current.Block;
            SaveCliExtension(s, (operation == "create" ? current with { CreateProbe = probe, ResumeProbe = null } : current with { ResumeProbe = probe }) with
            {
                Block = block, ProbeArtifacts = [.. (current.ProbeArtifacts ?? []).Select(p => p.ProbeId == probeId ? artifact : p)]
            });
            return probe;
        });
    }

    public CliExtensionRecord ConfirmCliProbe(string agentId, string probeId, bool visible) => store.Write(s =>
    {
        var record = ReadCliExtension(s, agentId) ?? throw new RelayException("cli_recipe_missing", "未找到调用方案。");
        var probe = record.CreateProbe?.ProbeId == probeId ? record.CreateProbe : record.ResumeProbe?.ProbeId == probeId ? record.ResumeProbe :
            throw new RelayException("cli_probe_missing", "未找到当前探针，请重新测试。");
        if (visible && !probe.MachinePassed) throw new RelayException("cli_probe_failed", "机器回执未通过，不能启用此能力。");
        var updated = probe with { UserConfirmed = visible };
        record = probe.Operation == "create" ? record with { CreateProbe = updated, ResumeProbe = visible ? record.ResumeProbe : null } : record with { ResumeProbe = updated };
        SaveCliExtension(s, record);
        return record;
    });

    private static CliExtensionRecord? ReadCliExtension(StoreSession s, string agentId) =>
        s.Json<CliExtensionRecord>("SELECT json FROM cli_extensions WHERE agent_id=$id", ("$id", agentId));
    private static void SaveCliExtension(StoreSession s, CliExtensionRecord record) => s.Execute(
        "INSERT INTO cli_extensions VALUES($id,$json) ON CONFLICT(agent_id) DO UPDATE SET json=excluded.json",
        ("$id", record.AgentId), ("$json", RelayJson.Write(record)));
}

public sealed class CliRecipeDeliveryAdapter(CliExtensionRecord extension, string? zcodeUserDirectory = null) : IDeliveryAdapter
{
    public string Id => "cli:" + extension.AgentId;

    public async Task<DeliveryResult> DeliverAsync(TaskRecord task, string home, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!extension.CanCreate || task.RecipientId != extension.AgentId)
            return new("rejected", null, "接收 Agent 的 CLI 新会话能力未验证。");
        var relay = new RelayService(home);
        var agents = relay.Agents();
        string Name(string id) => agents.SingleOrDefault(a => a.AgentId == id)?.DisplayName ?? id;
        var prompt = $"""
            Agent Relay handoff from {Name(task.RequesterId)} ({task.RequesterId}) to {Name(task.RecipientId)} ({task.RecipientId}).
            Relay home: {home}
            Task ID: {task.TaskId}
            Attempt ID: {task.AttemptId}
            Read the request file: {task.RequestPath}
            Write the reply file: {task.ResultPath}
            Report received/started/completed/failed explicitly through Relay CLI or MCP; changing files alone does not complete the handoff.
            CLI callbacks (relay is shipped with Agent Relay):
            relay --home "{home}" task ack --task {task.TaskId} --attempt {task.AttemptId} --sender {task.RecipientId} --idempotency-key received
            relay --home "{home}" task complete --task {task.TaskId} --attempt {task.AttemptId} --sender {task.RecipientId} --idempotency-key completed --status succeeded --result "{task.ResultPath}"
            If Relay CLI/MCP is unavailable, return the reply path to the operator for manual confirmation. Save permanent deliverables in the project, not the relay directory.
            """;
        ProcessResult process;
        try
        {
            relay.RequireTaskImages(task);
            process = await CliRecipes.InvokeAsync(extension.Recipe.Create, CliRecipes.OutputFormatFor(extension.Recipe), prompt, null, home, timeout, cancellationToken, task.Images);
        }
        catch (RelayException error) when (error.Code is "task_image_unavailable" or "cli_image_unsupported")
        {
            return new("rejected", null, error.Message);
        }
        var receipt = CliRecipes.Receipt(process, CliRecipes.OutputFormatFor(extension.Recipe));
        if (receipt is null) return new("unknown", null, CliFailureDiagnostics.Classify(process, extension.Recipe).Message);
        var directory = zcodeUserDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zcode");
        var current = relay.CliExtension(extension.AgentId);
        var sidebar = current is not null && current.Revision == extension.Revision
            ? ZcodeTaskIndexVisibility.Sync(current, receipt.Value.SessionId, directory, home) : null;
        return new("accepted", process.Output, null, SidebarSync: sidebar);
    }
}
