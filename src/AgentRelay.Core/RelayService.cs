using System.Text.RegularExpressions;

namespace AgentRelay.Core;

public sealed partial class RelayService
{
    private readonly RelayStore store;
    private readonly ManagedWorkspace workspace;
    private readonly TimeProvider clock;
    private readonly IReadOnlyDictionary<string, IDeliveryAdapter> adapters;
    private DateTimeOffset Now => clock.GetUtcNow();

    public RelayService(string home, TimeProvider? clock = null, IEnumerable<IDeliveryAdapter>? adapters = null)
    {
        store = new RelayStore(home);
        workspace = new ManagedWorkspace(store.Home);
        this.clock = clock ?? TimeProvider.System;
        this.adapters = (adapters ?? []).ToDictionary(a => a.Id);
    }

    public string Home => store.Home;
    public void Initialize() => store.Initialize();
    public RelaySettings Settings() => store.Read(s => s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!);

    public RelaySettings Configure(RelaySettings settings)
    {
        if (settings.ArtifactTtlDays is < 1 or > 365 || settings.TaskTimeoutMinutes is < 1 or > 43200 ||
            settings.DeliveryTimeoutSeconds is < 1 or > 300 || settings.MaxDeliveryAttempts is < 1 or > 3)
            throw new RelayException("invalid_settings", "TTL 为 1–365 天，任务时限为 1–43200 分钟，调用时限为 1–300 秒，运输尝试为 1–3 次。");
        return store.Write(s =>
        {
            s.Execute("UPDATE settings SET json=$json WHERE id=1", ("$json", RelayJson.Write(settings)));
            return settings;
        });
    }

    public AgentRecord Register(string id, string host, string configPath, string? cliPath = null,
        string? hostVersion = null, string[]? instructionPaths = null, string? displayName = null)
    {
        ValidateId(id);
        if (string.IsNullOrWhiteSpace(host)) throw new RelayException("invalid_host", "host 不能为空。");
        var agent = new AgentRecord(id, host, Path.GetFullPath(configPath),
            cliPath is null ? null : Path.GetFullPath(cliPath), hostVersion,
            (instructionPaths ?? []).Select(Path.GetFullPath).ToArray(), "unverified", null, displayName);
        return store.Write(s =>
        {
            InvalidateCliExtension(s, id);
            s.Execute("INSERT INTO agents(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
                ("$id", id), ("$json", RelayJson.Write(agent)));
            return agent;
        });
    }

    public AgentRecord ValidateAgent(string id) => store.Write(s =>
    {
        var agent = Agent(s, id);
        var valid = File.Exists(agent.ConfigPath) &&
            (agent.CliPath is null || File.Exists(agent.CliPath)) && agent.InstructionPaths.All(File.Exists);
        if (valid)
        {
            using var file = File.OpenRead(agent.ConfigPath);
            valid = file.Length > 0;
        }
        var result = agent with { ValidationStatus = valid ? "path_verified" : "invalid", VerifiedAt = Now };
        s.Execute("UPDATE agents SET json=$json WHERE id=$id", ("$json", RelayJson.Write(result)), ("$id", id));
        return result;
    });

    public List<AgentRecord> Agents() => store.Read(s => s.Query("SELECT json FROM agents ORDER BY id",
        r => RelayJson.Read<AgentRecord>(r.GetString(0))));

    public AgentRecord RegisterManual(string id, string host, string displayName, string? cliPath = null,
        string? hostVersion = null, string[]? instructionPaths = null)
    {
        ValidateId(id);
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(displayName))
            throw new RelayException("input_required", "Agent 名称和宿主不能为空。");
        var agent = new AgentRecord(id, host, "", cliPath is null ? null : Path.GetFullPath(cliPath), hostVersion,
            (instructionPaths ?? []).Select(Path.GetFullPath).ToArray(), "manual_ready", null, displayName.Trim());
        return store.Write(s =>
        {
            InvalidateCliExtension(s, id);
            s.Execute("INSERT INTO agents(id,json) VALUES($id,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
                ("$id", id), ("$json", RelayJson.Write(agent)));
            return agent;
        });
    }

    public void RemoveAgent(string id) => store.Write(s =>
    {
        Agent(s, id);
        if (s.Query("SELECT json FROM tasks", r=>RelayJson.Read<TaskRecord>(r.GetString(0)))
            .Any(t=>(t.RequesterId==id || t.RecipientId==id) && !Terminal(t.State)))
            throw new RelayException("agent_busy", "该 Agent 仍有进行中的交接，请先完成或取消交接。");
        s.Execute("DELETE FROM cli_extensions WHERE agent_id=$id", ("$id",id));
        s.Execute("DELETE FROM session_discovery_sources WHERE agent_id=$id", ("$id",id));
        s.Execute("DELETE FROM session_discovery_cache WHERE agent_id=$id", ("$id",id));
        s.Execute("DELETE FROM executors WHERE agent_id=$id", ("$id",id));
        s.Execute("DELETE FROM resumers WHERE agent_id=$id", ("$id",id));
        s.Execute("DELETE FROM permissions WHERE sender=$id OR recipient=$id", ("$id",id));
        s.Execute("DELETE FROM agents WHERE id=$id", ("$id",id));
        return true;
    });

    public object Discover()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codex = Environment.GetEnvironmentVariable("CODEX_HOME");
        var claude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return new
        {
            candidates = new[]
            {
                new { host = "codex", path = Path.Combine(codex ?? Path.Combine(user,".codex"), "config.toml"), source = codex is null ? "platform_candidate" : "CODEX_HOME" },
                new { host = "claude", path = Path.Combine(claude ?? Path.Combine(user,".claude"), "settings.json"), source = claude is null ? "platform_candidate" : "CLAUDE_CONFIG_DIR" }
            }.Select(c => new { c.host, c.path, c.source, exists = File.Exists(c.path), validation_status = "unverified" }),
            message = "候选仅供手工确认；未登记、未执行命令、未修改宿主配置。"
        };
    }

    public object SetPermission(string from, string to, string decision)
    {
        if (decision is not ("allow" or "deny")) throw new RelayException("invalid_decision", "decision 必须为 allow 或 deny。");
        return store.Write(s =>
        {
            Agent(s, from); Agent(s, to);
            s.Execute("INSERT INTO permissions VALUES($from,$to,$decision) ON CONFLICT(sender,recipient) DO UPDATE SET decision=excluded.decision",
                ("$from", from), ("$to", to), ("$decision", decision));
            return new { from, to, decision };
        });
    }

    public object Permissions() => ConnectionPermissions();

    private static AgentRecord Agent(StoreSession s, string id) =>
        s.Json<AgentRecord>("SELECT json FROM agents WHERE id=$id", ("$id", id))
        ?? throw new RelayException("agent_not_found", $"未登记 Agent {id}。", 3);

    private static void ValidateId(string id)
    {
        if (!Regex.IsMatch(id, "^[a-z][a-z0-9-]{0,63}$"))
            throw new RelayException("invalid_id", "Agent ID 使用小写字母开头的字母、数字或连字符，最多 64 字符。");
    }

    private static void RequirePaths(AgentRecord agent)
    {
        if (agent.ValidationStatus != "path_verified" || !File.Exists(agent.ConfigPath) ||
            (agent.CliPath is not null && !File.Exists(agent.CliPath)) || !agent.InstructionPaths.All(File.Exists))
            throw new RelayException("agent_invalid", $"Agent {agent.AgentId} 的路径未通过验证或已失效，请纠正后重新 validate。", 3);
        using var config = File.OpenRead(agent.ConfigPath);
        if (config.Length == 0) throw new RelayException("agent_invalid", $"Agent {agent.AgentId} 的配置文件为空。", 3);
    }

    private static bool Terminal(TaskState state) => state is TaskState.Succeeded or TaskState.Failed or TaskState.TimedOut or TaskState.Cancelled;

    private static void AddArtifact(StoreSession s, ArtifactRecord artifact) =>
        s.Execute("INSERT INTO artifacts VALUES($id,$task,$attempt,$path,$json)",
            ("$id", artifact.ArtifactId), ("$task", artifact.TaskId), ("$attempt", artifact.AttemptId),
            ("$path", artifact.Path), ("$json", RelayJson.Write(artifact)));

    private static void SaveArtifact(StoreSession s, ArtifactRecord artifact) =>
        s.Execute("UPDATE artifacts SET json=$json WHERE id=$id", ("$json", RelayJson.Write(artifact)), ("$id", artifact.ArtifactId));
}
