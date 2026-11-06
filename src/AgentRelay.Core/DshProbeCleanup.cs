using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ZstdSharp;

namespace AgentRelay.Core;

internal static class DshProbeCleanup
{
    internal sealed record Session(string? Directory, string? Parent, HashSet<string> Attachments,
        HashSet<string> ProbeMarkers, bool HasOtherPrompts = false);
    private static readonly Regex ProbePrompt = new(@"\ACreate the file (.+) containing exactly (RELAY-[A-Z0-9]{10}), then reply with exactly \2\. Do nothing else\.\z",
        RegexOptions.CultureInvariant);
    private const string LogPattern = @"\Asession\.v4\.jsonl(?:\.zstd)?\z";
    private const string AttachmentPattern = @"\Asha256:[0-9a-f]{64}\z";

    internal static IEnumerable<string> SessionDirectories(string root, string? cleanupId = null)
    {
        foreach (var name in new[] { "sessions", "sessions-archive", "sessions-trash" })
        {
            var bucket = Path.Combine(root, name);
            if (!Directory.Exists(bucket)) continue;
            RequireLocalPath(root, bucket);
            foreach (var path in Directory.EnumerateDirectories(bucket, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("DSH 会话目录包含链接，无法确认清理范围：" + path);
                if (Path.GetFileName(path) == cleanupId || LogFiles(path).Any()) yield return path;
            }
        }
    }

    private static IEnumerable<string> LogFiles(string directory) => Directory.EnumerateFiles(directory, "session.v*")
        .Where(path => path.EndsWith(".jsonl", StringComparison.Ordinal) || path.EndsWith(".jsonl.zstd", StringComparison.Ordinal));

    // Session headers and attachment references are external host data. Unknown or incomplete logs cannot prove ownership.
    internal static Session ReadSession(string directory, CancellationToken token, bool readProbeMarkers = false)
    {
        var paths = LogFiles(directory).ToArray();
        if (paths.Length == 0 || paths.Any(p => !Regex.IsMatch(Path.GetFileName(p), LogPattern)))
            throw new InvalidDataException("DSH 会话格式无法确认，已保留：" + directory);
        Session? result = null;
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Stream decoded = path.EndsWith(".zstd", StringComparison.Ordinal)
                    ? new DecompressionStream(file) : file;
                using var reader = new StreamReader(decoded, new UTF8Encoding(false, true));
                var line = reader.ReadLine() ?? throw new InvalidDataException("DSH 会话缺少头记录：" + path);
                using var header = JsonDocument.Parse(line);
                var h = header.RootElement;
                if (h.GetProperty("type").GetString() != "session" || h.GetProperty("version").GetInt32() != 4 ||
                    h.GetProperty("id").GetString() != Path.GetFileName(directory))
                    throw new InvalidDataException("DSH 会话标识或版本不匹配：" + path);
                var cwd = h.TryGetProperty("cwd", out var working) ? working.GetString() : null;
                var parent = h.TryGetProperty("parentSession", out var related) ? related.GetString() : null;
                if (result is not null && (result.Directory != cwd || result.Parent != parent))
                    throw new InvalidDataException("DSH 会话的多个日志头不一致：" + directory);
                result ??= new(cwd, parent, new(StringComparer.Ordinal), new(StringComparer.Ordinal));
                while ((line = reader.ReadLine()) is not null)
                {
                    token.ThrowIfCancellationRequested();
                    using var record = JsonDocument.Parse(line);
                    var type = record.RootElement.GetProperty("type").GetString()
                        ?? throw new InvalidDataException("DSH 会话包含无效的事件类型：" + path);
                    if (readProbeMarkers && type == "user/message")
                    {
                        var message = record.RootElement.GetProperty("data");
                        // DSH distinguishes real user prompts from its own injected user/message records by source.kind.
                        if (message.GetProperty("source").GetProperty("kind").GetString() == "user")
                        {
                            var text = string.Concat(message.GetProperty("content").EnumerateArray()
                                .Where(part => part.GetProperty("type").GetString() == "text")
                                .Select(part => part.GetProperty("text").GetString()));
                            var match = ProbePrompt.Match(text);
                            if (match.Success && Path.GetFileName(match.Groups[1].Value) == match.Groups[2].Value + ".txt" &&
                                Path.GetFileName(Path.GetDirectoryName(match.Groups[1].Value)) == ".cli-probes")
                                result.ProbeMarkers.Add(match.Groups[2].Value);
                            else result = result with { HasOtherPrompts = true };
                        }
                    }
                    ReadAttachments(record.RootElement, result.Attachments, imagesOnly: readProbeMarkers);
                }
            }
            catch (Exception error) when (error is ZstdException or JsonException or KeyNotFoundException or
                InvalidOperationException or DecoderFallbackException)
            { throw new InvalidDataException("无法读取 DSH 会话记录：" + path, error); }
        }
        return result!;
    }

    private static void ReadAttachments(JsonElement value, HashSet<string> ids, bool imagesOnly = false)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (imagesOnly && value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                type.GetString() == "image" && value.TryGetProperty("attachment", out var attachment))
            {
                ReadAttachments(attachment, ids);
                return;
            }
            foreach (var field in value.EnumerateObject())
            {
                if (!imagesOnly && field.Name == "attachmentId")
                {
                    if (field.Value.ValueKind != JsonValueKind.String || field.Value.GetString() is not { } id ||
                        !Regex.IsMatch(id, AttachmentPattern, RegexOptions.CultureInvariant))
                        throw new InvalidDataException("DSH 图片附件标识无法确认。");
                    ids.Add(id);
                }
                else ReadAttachments(field.Value, ids, imagesOnly);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) ReadAttachments(child, ids, imagesOnly);
    }

    internal static IReadOnlyList<ProbeCleanupItem> Cleanup(ProbeArtifact artifact, Action<string[]> rememberAttachments, CancellationToken token)
    {
        var root = artifact.Source!.StorageRoot;
        var id = artifact.SessionId!;
        var command = artifact.CreateCommand;
        if (command is null || (artifact.OutputFormat != "dsh-desktop" &&
            !Path.GetFileNameWithoutExtension(command.Executable).Equals("DeepSeek Harness", StringComparison.OrdinalIgnoreCase)))
            return [new(id, "session", "unsupported", "此 DSH 调用方式无法确认宿主是否已退出，已保留探针数据。")];
        ProbeCleanupItem Pending() => new(id, "session", "pending", "请先保存工作并从托盘正常退出 DSH，再点击清理测试探针。");
        if (DshDesktopClient.DesktopRunning(command.Executable)) return [Pending()];

        var items = new List<ProbeCleanupItem>();
        try
        {
            var directories = SessionDirectories(root, id).ToArray();
            var owned = directories.Where(p => Path.GetFileName(p) == id).ToArray();
            var withLogs = owned.Where(p => LogFiles(p).Any()).ToArray();
            var target = withLogs.Select(p => ReadSession(p, token, readProbeMarkers: true)).ToArray();
            if (target.Any(s => s.Parent is not null || artifact.SessionDirectory is not null &&
                !string.Equals(s.Directory, artifact.SessionDirectory, StringComparison.OrdinalIgnoreCase)))
                return [new(id, "session", "unverified", "DSH 会话的目录或父会话与探针归属不一致，已保留。")];
            if (target.Any(s => s.HasOtherPrompts || !s.ProbeMarkers.Contains(artifact.Marker)))
                return [new(id, "session", "unverified", "DSH 会话缺少原探针标记或包含非探针输入，已保留。")];
            var attachmentsKnown = target.Length > 0 || artifact.DshAttachmentIds is not null;

            var attachments = (artifact.DshAttachmentIds ?? []).Concat(target.SelectMany(s => s.Attachments))
                .ToHashSet(StringComparer.Ordinal);
            if (attachments.Any(a => !Regex.IsMatch(a, AttachmentPattern)))
                throw new InvalidDataException("已记录的 DSH 图片附件清单无效。");
            var shared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in directories.Except(owned))
            {
                var other = ReadSession(path, token);
                if (other.Parent == id)
                    return [new(id, "session", "in_use", "其它 DSH 会话引用此探针作为父会话，已保留。")];
                shared.UnionWith(other.Attachments);
            }
            var projections = Path.Combine(root, "storages", "session_projcache", "sessions");
            var targetProjection = Path.Combine(projections, id + ".json");
            if (Directory.Exists(projections))
                foreach (var path in Directory.EnumerateFiles(projections, "*.json"))
                {
                    token.ThrowIfCancellationRequested();
                    if (path == targetProjection) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    ReadAttachments(doc.RootElement, shared);
                }

            var indexPath = Path.Combine(root, "storages", "workspace.json");
            RequireLocalPath(root, indexPath);
            RequireLocalPath(root, targetProjection);
            var index = PrepareIndex(indexPath, id);
            var images = attachments.Order(StringComparer.Ordinal).Select(attachment =>
            {
                var digest = attachment[7..];
                return (Id: attachment, Path: Path.Combine(root, "attachments", "v1", "objects", digest[..2], digest));
            }).ToArray();
            foreach (var image in images) RequireLocalPath(root, image.Path);
            // Reading a large archive takes time. The user may have reopened DSH during the scan.
            if (DshDesktopClient.DesktopRunning(command.Executable)) return [Pending()];
            // Persist the exact list before deleting logs, so locked files and later retries cannot orphan images.
            if (attachmentsKnown) rememberAttachments(attachments.Order(StringComparer.Ordinal).ToArray());
            foreach (var path in artifact.DshAttachmentIds is null ? withLogs : owned)
            {
                token.ThrowIfCancellationRequested();
                Directory.Delete(RequireLocalPath(root, path), recursive: true);
                items.Add(new(id, "session", "removed", "已删除 DSH 探针会话日志及会话目录。", path));
            }
            if (!items.Any(i => i.Kind == "session")) items.Add(new(id, "session", "absent", "DSH 探针会话日志已不存在。"));
            DeleteFile(items, id, targetProjection, "已清理 DSH 探针会话缓存。");
            if (index is not null)
            {
                var temporary = indexPath + ".relay-" + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllText(temporary, index.ToJsonString(new() { WriteIndented = true })); File.Move(temporary, indexPath, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            items.Add(new(id, "index", index is null ? "absent" : "synced", index is null
                ? "DSH 侧栏索引中没有此会话。" : "已清除 DSH 侧栏分组、置顶及归档中的探针记录。", indexPath));
            if (!attachmentsKnown)
                items.Add(new(id, "file", "unverified", "DSH 日志已不存在，无法从原记录确认图片附件归属。"));
            foreach (var image in images)
            {
                token.ThrowIfCancellationRequested();
                if (shared.Contains(image.Id))
                    items.Add(new(id, "file", "shared", "图片仍被其它 DSH 会话或缓存引用，已保留共享附件。", image.Path));
                else DeleteFile(items, id, image.Path, "已删除仅由探针引用的 DSH 图片附件。");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { items.Add(new(id, "file", "failed", "DSH 探针清理未完成，已保留清理记录以便重试：" + error.Message)); }
        return items;
    }

    private static JsonNode? PrepareIndex(string path, string id)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        if (JsonNode.Parse(text) is not JsonObject state || state["unit"] is not JsonObject unit ||
            unit["name"] is not JsonValue name || !name.TryGetValue<string>(out var domain) || domain != "workspace" ||
            unit["version"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 2 ||
            state["global"] is not JsonObject global || state["tables"] is not JsonObject tables || tables["workspaces"] is not JsonObject workspaces)
            throw new InvalidDataException("无法确认 DSH 侧栏索引的版本。");
        var changed = false;
        void Remove(JsonNode? list)
        {
            if (list is not JsonArray array) throw new InvalidDataException("DSH 侧栏索引缺少会话列表。");
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is not JsonValue value || !value.TryGetValue<string>(out var sessionId))
                    throw new InvalidDataException("DSH 侧栏索引包含无效的会话标识。");
                if (sessionId == id) { array.RemoveAt(i); changed = true; }
            }
        }
        if (global.TryGetPropertyValue("archivedSessionIds", out var archived)) Remove(archived);
        if (global.TryGetPropertyValue("pinnedSessionIds", out var pinned)) Remove(pinned);
        foreach (var workspace in workspaces) Remove((workspace.Value as JsonObject)?["sessionIds"]);
        return changed ? state : null;
    }

    private static void DeleteFile(List<ProbeCleanupItem> items, string id, string path, string message)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new InvalidDataException("DSH 探针文件路径指向目录：" + path);
            File.Delete(path);
            items.Add(new(id, "file", "removed", message, path));
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { items.Add(new(id, "file", "absent", "探针文件已不存在。", path)); }
    }

    private static string RequireLocalPath(string root, string path)
    {
        var absolute = Path.GetFullPath(path);
        var basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!absolute.StartsWith(Path.EndsInDirectorySeparator(basePath) ? basePath : basePath + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("探针路径不在已登记的 DSH 数据目录内。");
        // A moved host store can contain junctions. Never write or delete through one.
        for (var current = absolute; current is not null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("DSH 探针路径经过链接，已保留：" + current);
            if (string.Equals(current, basePath, comparison)) break;
        }
        return absolute;
    }
}
