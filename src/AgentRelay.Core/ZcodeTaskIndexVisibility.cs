using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Core;

internal static class ZcodeTaskIndexVisibility
{
    private const string Marker = "relay-cli-sync";
    private static readonly string[] VisibleTaskTypes = ["interactive", "fork", "workflow_parent"];

    internal static CliSidebarSyncResult? Sync(CliExtensionRecord extension, string? sessionId, string zcodeDirectory, string home)
    {
        if (!CliRecipes.IsZcode(extension.Recipe)) return null;
        var options = extension.SidebarSync ?? new CliSidebarSync();
        if (!options.Enabled) return new("disabled", Message: "Sidebar synchronization is disabled for this agent.");
        if (string.IsNullOrWhiteSpace(sessionId)) return new("skipped", Message: "The receipt has no session ID.");
        if (sessionId.StartsWith("sess_subagent_", StringComparison.Ordinal))
            return new("skipped", Message: "Subagent sessions are not shown in the sidebar.");

        string? indexPath = null;
        string? backupPath = null;
        try
        {
            var sessionPath = Path.Combine(zcodeDirectory, "cli", "db", "db.sqlite");
            if (!File.Exists(sessionPath)) return new("failed", Message: "Zcode CLI session database was not found: " + sessionPath);
            var dataDirectory = CliRecipes.ZcodeDesktopDataDirectory(zcodeDirectory, strict: true) ?? Path.Combine(zcodeDirectory, "v2");
            if (!Path.IsPathFullyQualified(dataDirectory)) throw new InvalidDataException("Zcode's desktop data directory must be an absolute path.");
            indexPath = Path.Combine(dataDirectory, "tasks-index.sqlite");
            if (!File.Exists(indexPath)) return new("failed", indexPath, "Zcode sidebar index was not found: " + indexPath);

            var facts = ReadFacts(sessionPath, sessionId);
            if (facts is null) return new("failed", indexPath, "The session was not found in Zcode's CLI database.");
            if (facts.Deleted || facts.Archived) return new("skipped", indexPath, "The session is deleted or archived.");
            if (!VisibleTaskTypes.Contains(facts.TaskType))
                return new("skipped", indexPath, "This session type is not shown in the sidebar.");

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = indexPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 10
            }.ToString());
            connection.Open();
            if (ExistingRowIsUnavailable(connection, facts))
                return new("skipped", indexPath, "The desktop owns or deleted this sidebar entry; Relay left it unchanged.");

            // Keep a single pre-sync online snapshot for each index path, rather than copying the whole index every turn.
            backupPath = FindOrCreateBackup(connection, indexPath, home);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertSql;
            Bind(command, facts, ReadProjectId(connection, transaction, facts.Directory));
            if (command.ExecuteNonQuery() == 0)
                return new("skipped", indexPath, "The desktop owns or deleted this sidebar entry; Relay left it unchanged.", backupPath);

            using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "PRAGMA integrity_check";
                var integrity = (string)check.ExecuteScalar()!;
                if (integrity != "ok") return new("failed", indexPath, "Sidebar index integrity check failed: " + integrity, backupPath);
            }
            transaction.Commit();
            var activated = options.ActivateWindow ? ActivateWindow() : (bool?)null;
            return new("synced", indexPath, activated == false
                ? "Sidebar index synced, but the Zcode window could not be brought forward."
                : "Sidebar index synced.", backupPath, activated);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or JsonException or ArgumentException or InvalidCastException)
        {
            // The host files and settings are external; a failed visibility update must not change the completed CLI turn.
            return new("failed", indexPath, "Sidebar synchronization failed: " + error.Message, backupPath);
        }
    }

    private sealed record Facts(string SessionId, string Title, string Directory, long Created, long Updated,
        string? Outcome, string TaskType, bool Deleted, bool Archived);

    private static Facts? ReadFacts(string path, string sessionId)
    {
        using var connection = SessionProviders.OpenReadOnly(path);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title,directory,time_created,time_updated,last_outcome,task_type,time_deleted,time_archived
            FROM session WHERE id=$id
            """;
        command.Parameters.AddWithValue("$id", sessionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
            throw new InvalidDataException("The Zcode session is missing its title, directory, or timestamps.");
        return new(sessionId, reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
            SessionProviders.String(reader, 4), SessionProviders.String(reader, 5) ?? "interactive",
            !reader.IsDBNull(6), !reader.IsDBNull(7));
    }

    private static bool ExistingRowIsUnavailable(SqliteConnection connection, Facts facts)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT deleted, COALESCE(cli_deleted,0),
                CASE WHEN json_valid(meta_json) THEN json_extract(meta_json,'$.syncSource') ELSE NULL END
            FROM tasks WHERE workspace_key=$key AND task_id=$id
            """;
        command.Parameters.AddWithValue("$key", facts.Directory);
        command.Parameters.AddWithValue("$id", facts.SessionId);
        using var reader = command.ExecuteReader();
        return reader.Read() && (reader.GetInt64(0) != 0 || reader.GetInt64(1) != 0 ||
            reader.IsDBNull(2) || reader.GetString(2) != Marker);
    }

    private static string FindOrCreateBackup(SqliteConnection connection, string indexPath, string home)
    {
        var folder = Path.Combine(home, ".zcode-sidebar-backups");
        Directory.CreateDirectory(folder);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var sourcePath = Path.GetFullPath(indexPath);
        foreach (var manifest in Directory.EnumerateFiles(folder, "tasks-index-*.source"))
        {
            var backup = Path.ChangeExtension(manifest, ".sqlite");
            if (File.Exists(backup) && string.Equals(File.ReadAllText(manifest), sourcePath, comparison)) return backup;
        }

        var name = "tasks-index-" + Guid.NewGuid().ToString("N");
        var result = Path.Combine(folder, name + ".sqlite");
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = result, Pooling = false }.ToString()))
        {
            destination.Open();
            connection.BackupDatabase(destination);
        }
        var temporary = Path.Combine(folder, name + ".source.tmp");
        File.WriteAllText(temporary, sourcePath);
        File.Move(temporary, Path.Combine(folder, name + ".source"));
        return result;
    }

    private static string? ReadProjectId(SqliteConnection connection, SqliteTransaction transaction, string directory)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT r.project_id FROM project_roots r JOIN projects p ON p.project_id=r.project_id AND p.deleted=0
            WHERE r.root_path=$path ORDER BY r.is_primary DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$path", directory);
        return command.ExecuteScalar() as string;
    }

    private static void Bind(SqliteCommand command, Facts facts, string? projectId)
    {
        var status = facts.Outcome == "error" ? "error" : facts.Outcome is null ? null : "completed";
        var meta = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["taskId"] = facts.SessionId, ["title"] = facts.Title, ["workspacePath"] = facts.Directory,
            ["createdAt"] = facts.Created, ["updatedAt"] = facts.Updated, ["mode"] = "default",
            ["provider"] = "glm", ["syncSource"] = Marker, ["status"] = status
        });
        void Add(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        Add("$workspace_key", facts.Directory); Add("$task_id", facts.SessionId); Add("$title", facts.Title);
        Add("$task_status", status); Add("$created_at", facts.Created); Add("$updated_at", facts.Updated);
        Add("$meta_json", meta); Add("$project_id", projectId); Add("$cli_last_outcome", facts.Outcome);
    }

    private const string UpsertSql = """
        INSERT INTO tasks (workspace_key,workspace_path,workspace_identity,task_id,title,task_status,provider,mode,model,
          created_at,updated_at,unread_at,last_unread_at,pinned,archived,deleted,title_overridden,searchable_text,meta_json,
          host_id,root_path,project_id,cli_title,cli_title_source,cli_created_at,cli_updated_at,cli_last_outcome)
        VALUES ($workspace_key,$workspace_key,NULL,$task_id,$title,$task_status,'glm','default',NULL,
          $created_at,$updated_at,NULL,0,0,0,0,0,'',$meta_json,'local',$workspace_key,$project_id,$title,'generated',
          $created_at,$updated_at,$cli_last_outcome)
        ON CONFLICT(workspace_key,task_id) DO UPDATE SET
          title=CASE WHEN tasks.title_overridden=1 THEN tasks.title ELSE excluded.title END,
          cli_title=CASE WHEN tasks.cli_title_source='custom' THEN tasks.cli_title ELSE excluded.cli_title END,
          cli_title_source=CASE WHEN tasks.cli_title_source='custom' THEN tasks.cli_title_source ELSE excluded.cli_title_source END,
          cli_created_at=excluded.cli_created_at,cli_updated_at=excluded.cli_updated_at,
          cli_last_outcome=excluded.cli_last_outcome,task_status=excluded.task_status,
          created_at=excluded.created_at,updated_at=excluded.updated_at,
          meta_json=json_set(json_patch(tasks.meta_json,excluded.meta_json),'$.title',
            CASE WHEN tasks.title_overridden=1 THEN tasks.title WHEN tasks.cli_title_source='custom' THEN tasks.cli_title ELSE excluded.title END),
          root_path=excluded.root_path,project_id=COALESCE(tasks.project_id,excluded.project_id)
        WHERE tasks.deleted=0 AND COALESCE(tasks.cli_deleted,0)=0
          AND CASE WHEN json_valid(tasks.meta_json) THEN json_extract(tasks.meta_json,'$.syncSource')='relay-cli-sync' ELSE 0 END
        """;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string windowName);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static bool ActivateWindow()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var window = FindWindowW(null, "ZCode");
        if (window == IntPtr.Zero) return false;
        ShowWindow(window, 9);
        return SetForegroundWindow(window);
    }
}
