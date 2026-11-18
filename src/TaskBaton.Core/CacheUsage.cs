using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace TaskBaton.Core;

/// <param name="InputTokens">The whole prompt sent with the request, the cached part included.</param>
public sealed record CacheRequest(DateTimeOffset StartedAt, string? Model, long InputTokens, long CacheReadTokens)
{
    public double HitRate => InputTokens == 0 ? 0 : (double)CacheReadTokens / InputTokens;
}

/// <param name="Findings">Requests whose cache hit fell sharply, with the likely cause.</param>
public sealed record CacheReport(string AgentId, string SessionId, string Source, IReadOnlyList<CacheRequest> Requests, IReadOnlyList<string> Findings);

/// <summary>
/// Reads the host's own per-request usage for one session, so a desktop turn and a Relay turn can be compared.
/// Hosts build the tool set and system prompt at launch, and the provider reuses its prompt cache only for an identical
/// prefix: a request that hits little of the cache shortly after the previous one means the two launches built different prefixes.
/// </summary>
public sealed partial class RelayService
{
    // A short gap helps compare launches; provider retention and eviction policies still vary.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public CacheReport CacheUsage(string agentId, string sessionId, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new RelayException("session_required", "请提供要检查的会话 ID。");
        var source = DiscoverySource(agentId) ?? throw new RelayException("cache_usage_unsupported",
            "尚未配置此 Agent 的会话数据目录，Relay 读不到它的用量记录。可按 relay-cache-parity 技能里的方法手动对照。");
        (string, List<CacheRequest>) read;
        try
        {
            read = source.Provider switch
            {
                "zcode" => ZcodeCacheUsage(source.StorageRoot, sessionId),
                "hermes" => HermesCacheUsage(source.StorageRoot, sessionId),
                "dsh" => DshCacheUsage(agentId, source.StorageRoot, sessionId),
                _ => throw new RelayException("cache_usage_unsupported",
                    "Relay 还不会读取 " + source.Provider + " 的逐次用量记录。可按 relay-cache-parity 技能里的方法手动对照。")
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            throw new RelayException("cache_usage_unreadable", "读不到宿主的用量记录：" + error.Message);
        }
        var (where, requests) = read;
        var recent = requests.OrderBy(r => r.StartedAt).TakeLast(Math.Clamp(limit, 1, 500)).ToArray();
        return new(agentId, sessionId, where, recent, CacheFindings(recent));
    }

    public static IReadOnlyList<string> CacheFindings(IReadOnlyList<CacheRequest> requests)
    {
        var findings = new List<string>();
        for (var i = 1; i < requests.Count; i++)
        {
            var (previous, current) = (requests[i - 1], requests[i]);
            if (previous.InputTokens < 1024 || current.HitRate >= 0.5) continue;
            var gap = current.StartedAt - previous.StartedAt;
            var at = current.StartedAt.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            findings.Add(gap <= CacheLifetime
                ? $"{at} 的请求距上一次只有 {gap.TotalMinutes:0.#} 分钟，却只命中 {current.HitRate:P0}：可能是两次请求的前缀不同，也可能是模型、供应商或缓存策略变化。请先对照启动方式、工具集与系统提示，不能只凭命中率确定原因。"
                : $"{at} 的请求距上一次 {gap.TotalMinutes:0} 分钟，只命中 {current.HitRate:P0}：可能是缓存过期，也可能是前缀不同（有的供应商缓存能保留数小时）。要下结论，请在两种启动方式之间相隔几分钟内各发一轮再检查。");
        }
        return findings;
    }

    // DSH keeps usage in compressed session logs; the running desktop serves the same events through its API.
    private (string, List<CacheRequest>) DshCacheUsage(string agentId, string dshHome, string sessionId)
    {
        var executable = CliExtension(agentId)?.Recipe.Create.Executable ?? "DeepSeek Harness.exe";
        var requests = Task.Run(() => DshDesktopClient.UsageAsync(executable, dshHome, sessionId, CancellationToken.None)).GetAwaiter().GetResult();
        return ("DSH 桌面接口 · session/page", requests);
    }

    private static (string, List<CacheRequest>) ZcodeCacheUsage(string root, string sessionId)
    {
        var database = Path.Combine(root, "cli", "db", "db.sqlite");
        using var connection = SessionProviders.OpenReadOnly(database);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT started_at,model_id,input_tokens,cache_read_input_tokens FROM model_usage " +
            "WHERE session_id=$id AND status='completed' ORDER BY started_at";
        command.Parameters.AddWithValue("$id", sessionId);
        var requests = new List<CacheRequest>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            requests.Add(new(DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)), SessionProviders.String(reader, 1),
                reader.GetInt64(2), reader.GetInt64(3)));
        return (database + " · model_usage", requests);
    }

    // A request that read nothing from the cache is logged without the cache field.
    [GeneratedRegex(@"^(?<at>\d{4}-\d\d-\d\d \d\d:\d\d:\d\d),\d+ \S+ \[(?<session>[^\]]+)\] .*?API call #\d+: model=(?<model>\S+).*? in=(?<input>\d+)(?:.*? cache=(?<read>\d+)/\d+)?")]
    private static partial Regex HermesApiCall();

    private static (string, List<CacheRequest>) HermesCacheUsage(string root, string sessionId)
    {
        var logs = Path.Combine(root, "logs");
        // The gateway writes one line per model call for every client; rotated files carry a numeric suffix.
        var files = Directory.Exists(logs) ? Directory.GetFiles(logs, "agent.log*")
            .OrderByDescending(f => int.TryParse(Path.GetExtension(f).TrimStart('.'), out var n) ? n : 0).ToArray() : [];
        if (files.Length == 0) throw new FileNotFoundException("找不到 Hermes 的 agent.log。", Path.Combine(logs, "agent.log"));
        var requests = new List<CacheRequest>();
        foreach (var file in files)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var text = new StreamReader(stream);
            while (text.ReadLine() is { } line)
            {
                if (!line.Contains(sessionId, StringComparison.Ordinal) || HermesApiCall().Match(line) is not { Success: true } match ||
                    match.Groups["session"].Value != sessionId) continue;
                var at = DateTime.ParseExact(match.Groups["at"].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                requests.Add(new(new DateTimeOffset(at), match.Groups["model"].Value,
                    long.Parse(match.Groups["input"].Value, CultureInfo.InvariantCulture),
                    match.Groups["read"].Success ? long.Parse(match.Groups["read"].Value, CultureInfo.InvariantCulture) : 0));
            }
        }
        return (logs + " · agent.log", requests);
    }
}
