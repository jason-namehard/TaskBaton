using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Core;

public sealed record ProbeCleanupItem(string? SessionId, string Kind, string Status, string Message,
    string? Path = null, string? BackupPath = null);
public sealed record ProbeCleanupResult(string AgentId, IReadOnlyList<ProbeCleanupItem> Items)
{
    public bool Complete => Items.All(item => item.Status is "removed" or "absent" or "synced");
}

internal sealed record ProbeSessionSnapshot(SessionDiscoverySource? Source, HashSet<string>? SessionIds, string? Warning);
internal sealed record ProbeSessionOwnership(bool Owned, string? Directory, string? Warning);

public sealed partial class RelayService
{
    private ProbeSessionSnapshot CaptureProbeSessions(string agentId)
    {
        var source = DiscoverySource(agentId);
        if (source is null) return new(null, null, "此宿主没有可核实的会话来源，无法自动清理探针会话。");
        try { return new(source, ProbeHostSessions.Ids(source), null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            return new(source, null, "无法读取探针前的宿主会话，无法自动清理探针会话：" + error.Message);
        }
    }

    private static ProbeSessionOwnership IdentifyProbeSession(ProbeSessionSnapshot snapshot, string sessionId)
    {
        if (snapshot.Warning is not null) return new(false, null, snapshot.Warning);
        if (snapshot.SessionIds!.Contains(sessionId))
            return new(false, null, "此会话在探针前已存在，无法将它认定为探针新建会话。");
        try
        {
            var found = ProbeHostSessions.Find(snapshot.Source!, sessionId);
            return found.Exists ? new(true, found.Directory, null)
                : new(false, null, "宿主未持久化返回的会话 ID，无法确认其归属。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            return new(false, null, "无法核实探针后的宿主会话，无法自动清理：" + error.Message);
        }
    }

    public async Task<ProbeCleanupResult> CleanupCliProbeAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var extension = CliExtension(agentId) ?? throw new RelayException("cli_recipe_missing", "未找到调用方案。");
        var artifacts = extension.ProbeArtifacts ?? [];
        var items = new List<ProbeCleanupItem>();
        if (extension.CreateProbe is { SessionCleaned: false, SessionId: { Length: > 0 } legacy } &&
            !artifacts.Any(a => a.ProbeId == extension.CreateProbe.ProbeId))
            items.Add(new(legacy, "session", "unverified", "旧探针没有新建前的会话记录，无法确认归属，已保留。"));

        foreach (var artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = items.Count;
            if (!artifact.Completed)
            {
                items.Add(new(artifact.SessionId, "session", "pending", "探针未正常结束，无法确认是否仍在运行；已保留其产物。"));
                continue;
            }

            var markerPath = Path.Combine(Home, ".cli-probes", artifact.Marker + ".txt");
            try
            {
                if (!Regex.IsMatch(artifact.Marker, "^RELAY-[A-Z0-9]{10}$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("探针标识格式不正确。");
                var existed = File.Exists(markerPath);
                if (existed) File.Delete(markerPath);
                items.Add(new(artifact.SessionId, "file", existed ? "removed" : "absent", existed ? "已清理探针文件。" : "探针文件已不存在。", markerPath));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                items.Add(new(artifact.SessionId, "file", "failed", "探针文件清理失败：" + error.Message, markerPath));
            }

            if (artifact.Operation == "create" || artifact.SessionId is not null)
            {
                if (artifact.SessionId is not { Length: > 0 } id)
                    items.Add(new(null, "session", "unverified", "宿主未返回会话 ID，无法确认或清理会话。"));
                else if (!artifact.OwnedSession || artifact.Source is null)
                    items.Add(new(id, "session", artifact.Source is null ? "unsupported" : "unverified",
                        artifact.OwnershipWarning ?? "无法证明此会话由本次探针新建，已保留。"));
                else if (store.Read(s => s.Query("SELECT json FROM task_session_bindings", row => RelayJson.Read<TaskSessionBinding>(row.GetString(0)))
                    .Any(binding => binding.SessionId == id)))
                    items.Add(new(id, "session", "in_use", "会话已绑定实际任务，已保留。"));
                else
                {
                    var sessionItems = await CleanupOwnedSessionAsync(artifact, cancellationToken);
                    items.AddRange(sessionItems);
                    if (sessionItems.Any(i => i.Kind == "session" && i.Status is "removed" or "absent"))
                    {
                        store.Write(s =>
                        {
                            var current = ReadCliExtension(s, agentId);
                            if (current is null) return true;
                            var currentArtifact = (current.ProbeArtifacts ?? []).SingleOrDefault(p => p.ProbeId == current.CreateProbe?.ProbeId);
                            var isCurrentCreate = current.CreateProbe?.SessionId == id && currentArtifact?.Source == artifact.Source;
                            var create = isCurrentCreate ? current.CreateProbe! with { SessionCleaned = true } : current.CreateProbe;
                            var resume = isCurrentCreate && current.ResumeProbe?.SessionId == id ? current.ResumeProbe with { SessionCleaned = true } : current.ResumeProbe;
                            SaveCliExtension(s, current with { CreateProbe = create, ResumeProbe = resume });
                            s.Execute("DELETE FROM session_discovery_cache WHERE agent_id=$id", ("$id", agentId));
                            return true;
                        });
                    }
                }
            }

            if (items.Skip(start).All(item => item.Status is "removed" or "absent" or "synced"))
                store.Write(s =>
                {
                    var current = ReadCliExtension(s, agentId);
                    if (current is not null)
                        SaveCliExtension(s, current with { ProbeArtifacts = [.. (current.ProbeArtifacts ?? []).Where(p => p.ProbeId != artifact.ProbeId)] });
                    return true;
                });
        }
        return new(agentId, items);
    }

    private async Task<IReadOnlyList<ProbeCleanupItem>> CleanupOwnedSessionAsync(ProbeArtifact artifact, CancellationToken cancellationToken)
    {
        var source = artifact.Source!;
        var id = artifact.SessionId!;
        if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant))
            return [new(id, "session", "unverified", "宿主会话 ID 格式无法安全定位，已保留。")];

        if (source.Provider == "zcode")
        {
            var results = new List<ProbeCleanupItem>();
            string? directory;
            try
            {
                var deleted = ZcodeProbeCleanup.DeleteSession(source.StorageRoot, id);
                directory = deleted.Directory ?? artifact.SessionDirectory;
                results.Add(new(id, "session", deleted.Existed ? "removed" : "absent", deleted.Existed ? "已清理 Zcode 探针会话。" : "Zcode 探针会话已不存在。"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
            {
                return [new(id, "session", "failed", "Zcode 会话清理失败：" + error.Message)];
            }

            results.AddRange(ZcodeProbeCleanup.DeleteFiles(source.StorageRoot, id));
            var indexes = new List<string?> { Path.Combine(source.StorageRoot, "v2", "tasks-index.sqlite"),
                Path.Combine(ZcodeUserDirectory, "v2", "tasks-index.sqlite"),
                directory is null ? null : Path.Combine(directory, ".zcode", "v2", "tasks-index.sqlite") };
            try
            {
                if (CliRecipes.ZcodeDesktopDataDirectory(ZcodeUserDirectory, strict: true) is { } data)
                {
                    if (!Path.IsPathFullyQualified(data)) throw new InvalidDataException("Zcode 桌面数据目录必须是绝对路径。");
                    indexes.Add(Path.Combine(data, "tasks-index.sqlite"));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                results.Add(new(id, "index", "failed", "无法定位 Zcode 桌面侧栏索引：" + error.Message));
            }
            foreach (var index in indexes.Where(path => path is not null).Distinct(StringComparer.OrdinalIgnoreCase))
                results.Add(ZcodeProbeCleanup.SyncIndex(index!, id, Path.Combine(Home, ".cli-probes", "backups", artifact.ProbeId)));
            return results;
        }

        if (source.Provider == "hermes" && artifact.CreateCommand is { } command && HermesDeleteExecutable(command) is { } executable)
        {
            try
            {
                var existed = ProbeHostSessions.Find(source, id).Exists;
                if (existed)
                {
                    if (ProbeHostSessions.HermesHasRelatedSessions(source.StorageRoot, id))
                        return [new(id, "session", "unverified", "Hermes 会话存在关联会话或会话谱系，删除命令可能一并处理它们；已保留。")];
                    if (HermesProbeCleanup.HasUnverifiedDump(source.StorageRoot, id))
                        return [new(id, "session", "unverified", "Hermes 的删除命令会匹配无法确认归属的文件，已保留会话。")];
                    var environment = command.Environment is null ? new Dictionary<string, string>() : new(command.Environment);
                    environment["HERMES_HOME"] = source.StorageRoot;
                    var result = await RelayProcess.RunAsync(executable, ["sessions", "delete", id, "--yes"], null,
                        TimeSpan.FromSeconds(30), cancellationToken, environment, command.WorkingDirectory);
                    if (result.TimedOut || result.ExitCode != 0)
                        return [new(id, "session", "failed", "Hermes 会话清理失败：" + (result.TimedOut ? "操作超时。" : result.Error + result.Output))];
                    if (ProbeHostSessions.Find(source, id).Exists)
                        return [new(id, "session", "failed", "Hermes 命令已结束，但会话仍在；可能仍有宿主进程持有它。")];
                }
                return [new(id, "session", existed ? "removed" : "absent", existed
                    ? "已通过 Hermes 删除探针会话；若会话仍在宿主中打开，宿主可能重新保存它。" : "Hermes 探针会话已不存在。"),
                    .. HermesProbeCleanup.DeleteFiles(source.StorageRoot, id)];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or Win32Exception)
            {
                return [new(id, "session", "failed", "Hermes 会话清理失败：" + error.Message)];
            }
        }
        return [new(id, "session", "unsupported", "此宿主没有可确认的自动删除方式，探针会话已保留。")];
    }

    private static string? HermesDeleteExecutable(CliCommand command)
    {
        var executable = command.Executable;
        var name = Path.GetFileNameWithoutExtension(executable);
        if (name.Equals("hermes", StringComparison.OrdinalIgnoreCase)) return executable;
        if (!name.Equals("hermes-acp", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(executable)) return null;
        var sibling = Path.Combine(Path.GetDirectoryName(executable)!, "hermes" + Path.GetExtension(executable));
        return File.Exists(sibling) ? sibling : null;
    }
}

internal static class HermesProbeCleanup
{
    private static string DumpPattern(string id) => "^request_dump_" + Regex.Escape(id) + @"_\d{8}_\d{6}_\d{6}\.json$";

    internal static bool HasUnverifiedDump(string root, string id)
    {
        var directory = Path.Combine(root, "sessions");
        return Directory.Exists(directory) && Directory.EnumerateFiles(directory, "request_dump_" + id + "_*.json")
            .Any(path => !Regex.IsMatch(Path.GetFileName(path), DumpPattern(id), RegexOptions.CultureInvariant));
    }

    internal static IReadOnlyList<ProbeCleanupItem> DeleteFiles(string root, string id)
    {
        var directory = Path.Combine(root, "sessions");
        if (!Directory.Exists(directory)) return [];
        var items = new List<ProbeCleanupItem>();
        try
        {
            var paths = new[] { id + ".json", id + ".jsonl", "session_" + id + ".json" }
                .Select(name => Path.Combine(directory, name))
                .Concat(Directory.EnumerateFiles(directory, "request_dump_" + id + "_*.json"));
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                if (Path.GetFileName(path).StartsWith("request_dump_", StringComparison.Ordinal) &&
                    !Regex.IsMatch(Path.GetFileName(path), DumpPattern(id), RegexOptions.CultureInvariant))
                {
                    items.Add(new(id, "file", "unverified", "文件名含探针会话 ID，但无法确认文件归属；已保留。", path));
                    continue;
                }
                try { File.Delete(path); items.Add(new(id, "file", "removed", "已清理 Hermes 探针会话文件。", path)); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { items.Add(new(id, "file", "failed", "Hermes 探针会话文件清理失败：" + error.Message, path)); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            items.Add(new(id, "file", "failed", "无法列出 Hermes 探针会话文件：" + error.Message, directory));
        }
        return items;
    }
}

internal static class ProbeHostSessions
{
    internal static HashSet<string> Ids(SessionDiscoverySource source)
    {
        if (source.Provider == "dsh")
        {
            var sessions = Path.Combine(source.StorageRoot, "sessions");
            return !Directory.Exists(sessions) ? [] : Directory.EnumerateDirectories(sessions)
                .SelectMany(Directory.EnumerateDirectories).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        }
        var (database, table) = Database(source);
        if (!File.Exists(database)) return [];
        using var connection = SessionProviders.OpenReadOnly(database);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id FROM {table}";
        using var reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    internal static (bool Exists, string? Directory) Find(SessionDiscoverySource source, string id)
    {
        if (source.Provider == "dsh") return (Ids(source).Contains(id), null);
        var (database, table) = Database(source);
        if (!File.Exists(database)) return (false, null);
        using var connection = SessionProviders.OpenReadOnly(database);
        using var command = connection.CreateCommand();
        command.CommandText = source.Provider == "zcode" ? "SELECT directory FROM session WHERE id=$id" : $"SELECT id FROM {table} WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (true, source.Provider == "zcode" ? SessionProviders.String(reader, 0) : null) : (false, null);
    }

    internal static bool HermesHasRelatedSessions(string root, string id)
    {
        using var connection = SessionProviders.OpenReadOnly(Path.Combine(root, "state.db"));
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM sessions
                WHERE (id=$id AND parent_session_id IS NOT NULL)
                   OR (id<>$id AND (parent_session_id=$id OR chat_id=$id OR
                       CASE WHEN json_valid(model_config) THEN json_extract(model_config,'$._delegate_from') END=$id))
            ) OR EXISTS (
                SELECT 1 FROM state_meta AS policy
                WHERE policy.key LIKE 'gateway.local_policy.v1:%' AND json_valid(policy.value)
                  AND (substr(policy.key, length('gateway.local_policy.v1:')+1)=$id OR
                       EXISTS (SELECT 1 FROM json_each(policy.value,'$.lineage') WHERE value=$id))
                  AND (substr(policy.key, length('gateway.local_policy.v1:')+1)<>$id OR
                       EXISTS (SELECT 1 FROM json_each(policy.value,'$.lineage') WHERE value<>$id))
            )
            """;
        command.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

    private static (string Database, string Table) Database(SessionDiscoverySource source) => source.Provider switch
    {
        "zcode" => (Path.Combine(source.StorageRoot, "cli", "db", "db.sqlite"), "session"),
        "hermes" => (Path.Combine(source.StorageRoot, "state.db"), "sessions"),
        "workbuddy" => (Path.Combine(source.StorageRoot, "workbuddy.db"), "sessions"),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}

internal static class ZcodeProbeCleanup
{
    private static SqliteConnection OpenWrite(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    internal static (bool Existed, string? Directory) DeleteSession(string root, string id)
    {
        var database = Path.Combine(root, "cli", "db", "db.sqlite");
        if (!File.Exists(database)) return (false, null);
        using var connection = OpenWrite(database);
        using var transaction = connection.BeginTransaction();
        using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
        lookup.CommandText = "SELECT directory FROM session WHERE id=$id"; lookup.Parameters.AddWithValue("$id", id);
        using var reader = lookup.ExecuteReader();
        var existed = reader.Read();
        var directory = existed ? SessionProviders.String(reader, 0) : null;
        reader.Close();
        if (!existed) { transaction.Commit(); return (false, null); }

        using var tables = connection.CreateCommand(); tables.Transaction = transaction;
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> 'session'";
        using var tableReader = tables.ExecuteReader();
        var names = new List<string>(); while (tableReader.Read()) names.Add(tableReader.GetString(0));
        tableReader.Close();
        foreach (var name in names)
        {
            using var columns = connection.CreateCommand(); columns.Transaction = transaction;
            columns.CommandText = "SELECT name FROM pragma_table_info($table) WHERE name IN ('session_id','child_session_id','parent_session_id')";
            columns.Parameters.AddWithValue("$table", name);
            using var columnReader = columns.ExecuteReader();
            var references = new List<string>(); while (columnReader.Read()) references.Add(columnReader.GetString(0));
            columnReader.Close();
            if (references.Count == 0) continue;
            using var delete = connection.CreateCommand(); delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM " + Quote(name) + " WHERE " + string.Join(" OR ", references.Select(c => Quote(c) + "=$id"));
            delete.Parameters.AddWithValue("$id", id); delete.ExecuteNonQuery();
        }
        using var session = connection.CreateCommand(); session.Transaction = transaction;
        session.CommandText = "DELETE FROM session WHERE id=$id"; session.Parameters.AddWithValue("$id", id); session.ExecuteNonQuery();
        transaction.Commit();
        return (true, directory);
    }

    internal static IReadOnlyList<ProbeCleanupItem> DeleteFiles(string root, string id)
    {
        var items = new List<ProbeCleanupItem>();
        var cli = Path.Combine(root, "cli");
        foreach (var category in new[] { "agents", "exec", "artifacts", "image-cache" })
        {
            var path = Path.Combine(cli, category, id);
            if (!Directory.Exists(path)) continue;
            ProbeCleanupItem item;
            try { Directory.Delete(path, recursive: true); item = new(id, "file", "removed", "已清理探针会话目录。", path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { item = new(id, "file", "failed", "探针会话目录清理失败：" + error.Message, path); }
            items.Add(item);
        }
        var rollout = Path.Combine(cli, "rollout");
        if (!Directory.Exists(rollout)) return items;
        string[] files;
        try { files = Directory.GetFiles(rollout); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            items.Add(new(id, "file", "failed", "探针会话文件清理失败：" + error.Message, rollout));
            return items;
        }
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (name != "model-io-" + id + ".jsonl")
            {
                if (name.Contains(id, StringComparison.Ordinal))
                    items.Add(new(id, "file", "unverified", "文件名含探针会话 ID，但无法确认文件归属；已保留。", path));
                continue;
            }
            ProbeCleanupItem item;
            try { File.Delete(path); item = new(id, "file", "removed", "已清理探针会话文件。", path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { item = new(id, "file", "failed", "探针会话文件清理失败：" + error.Message, path); }
            items.Add(item);
        }
        return items;
    }

    internal static ProbeCleanupItem SyncIndex(string path, string id, string backupDirectory)
    {
        if (!File.Exists(path)) return new(id, "index", "absent", "Zcode 侧栏索引不存在。", path);
        string? backup = null;
        try
        {
            using var connection = OpenWrite(path);
            using (var lookup = connection.CreateCommand())
            {
                lookup.CommandText = "SELECT EXISTS(SELECT 1 FROM tasks WHERE task_id=$id)";
                lookup.Parameters.AddWithValue("$id", id);
                if (Convert.ToInt64(lookup.ExecuteScalar()) == 0)
                    return new(id, "index", "absent", "Zcode 侧栏索引中没有此会话。", path);
            }
            Directory.CreateDirectory(backupDirectory);
            backup = Path.Combine(backupDirectory, "tasks-index-" + Guid.NewGuid().ToString("N") + ".sqlite");
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = backup, Pooling = false }.ToString()))
            {
                destination.Open(); connection.BackupDatabase(destination);
            }
            using (var transaction = connection.BeginTransaction())
            {
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = """
                    UPDATE tasks SET deleted=1, archived=1 WHERE task_id=$id;
                    DELETE FROM task_group_admissions WHERE task_id=$id;
                    DELETE FROM task_group_view_node_orders WHERE node_type='task' AND instr(node_key,$quoted)>0;
                    """;
                command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$quoted", "\"" + id + "\"");
                command.ExecuteNonQuery(); transaction.Commit();
            }
            using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
            var integrity = (string)check.ExecuteScalar()!;
            if (integrity != "ok") return new(id, "index", "failed", "侧栏索引完整性检查失败：" + integrity, path, backup);
            return new(id, "index", "synced", "已同步 Zcode 侧栏索引（integrity_check=ok）。", path, backup);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            return new(id, "index", "failed", "侧栏索引同步失败：" + error.Message, path, backup);
        }
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
