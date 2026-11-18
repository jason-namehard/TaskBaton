using System.Text.Json;

namespace TaskBaton.Core;

internal static class DshSessions
{
    internal static IReadOnlyList<SessionRecord> Query(string root, string? sessionId, CancellationToken cancellationToken)
    {
        var sessionsRoot = Path.Combine(root, "sessions");
        if (!Directory.Exists(sessionsRoot)) throw new DirectoryNotFoundException("DSH 会话目录不存在：" + sessionsRoot);
        var results = new List<SessionRecord>();
        // A projection supplies titles only. The actual persisted session file establishes existence.
        foreach (var directory in Directory.EnumerateDirectories(sessionsRoot))
        foreach (var sessionDirectory in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(sessionDirectory);
            if (sessionId is not null && id != sessionId) continue;
            var file = Directory.EnumerateFiles(sessionDirectory, "session.v*.jsonl.zstd")
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
            if (file is null) continue;
            string? title = null, cwd = null, model = null;
            DateTimeOffset? updated = File.GetLastWriteTimeUtc(file);
            var projection = Path.Combine(root, "storages", "session_projcache", "sessions", id + ".json");
            if (File.Exists(projection))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(projection));
                var record = Object(document.RootElement, "record");
                var identity = Object(record, "identity"); cwd = Text(identity, "cwd");
                var rows = Object(record, "rows");
                if (Value(rows, "title") is { } storedTitle && storedTitle.ValueKind == JsonValueKind.String) title = storedTitle.GetString();
                if (Value(rows, "sessionListMetadata") is { } metadata && metadata.ValueKind == JsonValueKind.Object &&
                    metadata.TryGetProperty("lastPromptAt", out var last) && last.ValueKind == JsonValueKind.Number)
                    updated = DateTimeOffset.FromUnixTimeMilliseconds(last.GetInt64());
                if (Value(rows, "modelSelection") is { } selection && selection.ValueKind == JsonValueKind.Object &&
                    selection.TryGetProperty("lastUsed", out var used) && used.ValueKind == JsonValueKind.Object)
                    model = Text(used, "model");
            }
            results.Add(new(id, title, cwd, null, file, "unknown", updated, model, "active"));
        }
        return results.OrderByDescending(s => s.UpdatedAt).ToArray();
    }

    private static JsonElement? Value(JsonElement rows, string key) => rows.TryGetProperty(key, out var row) &&
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty("val", out var value) ? value : null;
    private static JsonElement Object(JsonElement parent, string key) => parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object ? value
        : throw new JsonException("DSH 会话元数据缺少对象字段：" + key);
    private static string? Text(JsonElement element, string key) => element.TryGetProperty(key, out var field) &&
        field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
