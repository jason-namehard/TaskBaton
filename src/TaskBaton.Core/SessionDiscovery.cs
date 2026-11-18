using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace TaskBaton.Core;

public sealed record SessionRecord(string SessionId, string? Title, string? Cwd, string? Profile,
    string? SessionPath, string Visibility, DateTimeOffset? UpdatedAt, string? Model = null, string? Status = null);
public sealed record SessionDiscoveryResult(string AgentId, string Provider, bool Supported,
    IReadOnlyList<SessionRecord> Sessions, DateTimeOffset ScannedAt, bool IsCached, string? Message = null);
public sealed record SessionValidationResult(string Status, SessionRecord? Session = null, string? Message = null);
public sealed record SessionDiscoverySource(string Provider, string StorageRoot);

public sealed partial class RelayService
{
    public bool SessionDiscoverySupported(string agentId) => DiscoverySource(agentId) is not null;

    public SessionDiscoverySource? DiscoverySource(string agentId)
    {
        var agent = Agents().SingleOrDefault(a => a.AgentId == agentId)
            ?? throw new RelayException("agent_not_found", "未找到 Agent：" + agentId);
        var configured = store.Read(s => s.Json<SessionDiscoverySource>(
            "SELECT json FROM session_discovery_sources WHERE agent_id=$id", ("$id", agentId)));
        if (configured is not null) return configured;
        var recipe = CliExtension(agentId)?.Recipe;
        var provider = SessionProviders.Identify(agent, recipe);
        if (provider is null) return null;
        string? EnvironmentPath(string key) => recipe?.Create.Environment?.GetValueOrDefault(key);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = provider switch
        {
            "zcode" => EnvironmentPath("ZCODE_HOME") ?? Path.Combine(user, ".zcode"),
            "hermes" => EnvironmentPath("HERMES_HOME") ?? Path.Combine(user, ".hermes"),
            "workbuddy" => EnvironmentPath("WORKBUDDY_CONFIG_DIR") ?? EnvironmentPath("CODEBUDDY_CONFIG_DIR") ?? Path.Combine(user, ".workbuddy"),
            // DSH's CLI data root varies with its desktop installation; require its explicit environment or a configured source.
            "dsh" => EnvironmentPath("DSH_HOME"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        return root is null ? null : new(provider, Path.GetFullPath(root));
    }

    public SessionDiscoverySource ConfigureSessionDiscovery(string agentId, string provider, string storageRoot)
    {
        _ = Agents().SingleOrDefault(a => a.AgentId == agentId)
            ?? throw new RelayException("agent_not_found", "未找到 Agent：" + agentId);
        if (!SessionProviders.Providers.Contains(provider) || !Path.IsPathFullyQualified(storageRoot))
            throw new RelayException("session_discovery_source", "请选择受支持的会话提供器，并填写完整数据目录。");
        var source = new SessionDiscoverySource(provider, Path.GetFullPath(storageRoot));
        return store.Write(s =>
        {
            s.Execute("INSERT INTO session_discovery_sources VALUES($id,$json) ON CONFLICT(agent_id) DO UPDATE SET json=excluded.json",
                ("$id", agentId), ("$json", RelayJson.Write(source)));
            s.Execute("DELETE FROM session_discovery_cache WHERE agent_id=$id", ("$id", agentId));
            return source;
        });
    }

    public SessionDiscoveryResult? CachedSessions(string agentId) => store.Read(s =>
        s.Json<SessionDiscoveryResult>("SELECT json FROM session_discovery_cache WHERE agent_id=$id", ("$id", agentId))) is { } cached
            ? cached with { IsCached = true } : null;

    public async Task<SessionDiscoveryResult> QuerySessionsAsync(string agentId, bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!refresh && CachedSessions(agentId) is { } cached) return cached;
        var source = DiscoverySource(agentId);
        if (source is null) return new(agentId, "unsupported", false, [], Now, false,
            "尚未配置此 Agent 的会话发现来源；可手动输入准确会话 ID，或配置会话数据目录。");
        try
        {
            var recipe = CliExtension(agentId)?.Recipe;
            var sessions = await Task.Run(() => SessionProviders.Query(source, recipe, null, cancellationToken), cancellationToken);
            var result = new SessionDiscoveryResult(agentId, source.Provider, true, sessions, Now, false);
            store.Write(s => s.Execute("INSERT INTO session_discovery_cache VALUES($id,$json) ON CONFLICT(agent_id) DO UPDATE SET json=excluded.json",
                ("$id", agentId), ("$json", RelayJson.Write(result))));
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or JsonException or RelayException)
        {
            // Host stores are external files: leave prior display cache intact and report a failed refresh explicitly.
            return new(agentId, source.Provider, true, [], Now, false, "会话列表刷新失败：" + error.Message);
        }
    }

    public async Task<SessionValidationResult> ValidateDiscoveredSessionAsync(string agentId, string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return new("missing", Message: "请填写准确会话 ID。");
        var source = DiscoverySource(agentId);
        if (source is null) return new("unsupported", Message: "此 Agent 尚无可验证的会话发现来源。");
        try
        {
            // Validation queries the actual host store, never the Relay display cache.
            var found = await Task.Run(() => SessionProviders.Query(source, CliExtension(agentId)?.Recipe, sessionId, cancellationToken), cancellationToken);
            return found.SingleOrDefault() is { } session
                ? new("exists", session) : new("missing", Message: "该会话已不存在、已删除或已归档，请重新选择。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or JsonException or RelayException)
        {
            return new("unavailable", Message: "无法核实此会话，请检查宿主数据目录：" + error.Message);
        }
    }
}

internal static class SessionProviders
{
    internal static readonly string[] Providers = ["zcode", "hermes", "workbuddy", "dsh"];

    internal static string? Identify(AgentRecord agent, CliRecipe? recipe)
    {
        var format = recipe is null ? null : CliRecipes.OutputFormatFor(recipe);
        if (format is "zcode-json" or "zcode-jsonl") return "zcode";
        if (format == "hermes-jsonl") return "hermes";
        if (format is "dsh-jsonl" or "dsh-desktop") return "dsh";
        if (agent.Host.Equals("WorkBuddy", StringComparison.OrdinalIgnoreCase)) return "workbuddy";
        if (agent.Host.StartsWith("ZCode", StringComparison.OrdinalIgnoreCase)) return "zcode";
        if (agent.Host.StartsWith("Hermes", StringComparison.OrdinalIgnoreCase)) return "hermes";
        if (agent.Host.Equals("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) || agent.Host.Equals("dsh", StringComparison.OrdinalIgnoreCase)) return "dsh";
        return null;
    }

    internal static IReadOnlyList<SessionRecord> Query(SessionDiscoverySource source, CliRecipe? recipe,
        string? sessionId, CancellationToken cancellationToken) => source.Provider switch
    {
        "zcode" => ZcodeSessions.Query(source.StorageRoot, recipe?.Create.WorkingDirectory, sessionId, cancellationToken),
        "hermes" => HermesSessions.Query(source.StorageRoot, sessionId, cancellationToken),
        "workbuddy" => WorkBuddySessions.Query(source.StorageRoot, sessionId, cancellationToken),
        "dsh" => DshSessions.Query(source.StorageRoot, sessionId, cancellationToken),
        _ => throw new RelayException("session_discovery_provider", "未知的会话提供器：" + source.Provider)
    };

    internal static SqliteConnection OpenReadOnly(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("会话数据库不存在。", path);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal static string? String(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    internal static DateTimeOffset? Unix(SqliteDataReader reader, int index, bool milliseconds) => reader.IsDBNull(index)
        ? null : milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(index))
            : DateTimeOffset.FromUnixTimeMilliseconds(checked((long)(reader.GetDouble(index) * 1000)));
}
