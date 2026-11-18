namespace TaskBaton.Core;

internal static class ZcodeSessions
{
    internal static IReadOnlyList<SessionRecord> Query(string root, string? workingDirectory, string? sessionId, CancellationToken cancellationToken)
    {
        var database = Path.Combine(root, "cli", "db", "db.sqlite");
        using var connection = SessionProviders.OpenReadOnly(database);
        using var transaction = connection.BeginTransaction(deferred: true);
        var indexes = new HashSet<string>(StringComparer.Ordinal);
        var indexPaths = new[] { Path.Combine(root, "v2", "tasks-index.sqlite"),
            workingDirectory is null ? null : Path.Combine(workingDirectory, ".zcode", "v2", "tasks-index.sqlite") }
            .Where(p => p is not null && File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var index in indexPaths)
        {
            using var indexConnection = SessionProviders.OpenReadOnly(index!);
            using var indexCommand = indexConnection.CreateCommand();
            indexCommand.CommandText = "SELECT task_id FROM tasks";
            using var indexReader = indexCommand.ExecuteReader();
            while (indexReader.Read()) { cancellationToken.ThrowIfCancellationRequested(); indexes.Add(indexReader.GetString(0)); }
        }
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id,title,directory,time_updated FROM session WHERE time_archived IS NULL AND time_deleted IS NULL" +
            (sessionId is null ? "" : " AND id=$id") + " ORDER BY time_updated DESC";
        if (sessionId is not null) command.Parameters.AddWithValue("$id", sessionId);
        using var reader = command.ExecuteReader(); var results = new List<SessionRecord>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested(); var id = reader.GetString(0);
            var rollout = Path.Combine(root, "cli", "rollout", "model-io-" + id + ".jsonl");
            results.Add(new(id, SessionProviders.String(reader, 1), SessionProviders.String(reader, 2), null,
                File.Exists(rollout) ? rollout : database,
                indexes.Contains(id) ? "native-visible" : indexPaths.Length > 0 ? "persistent-but-not-indexed" : "unknown",
                SessionProviders.Unix(reader, 3, true), Status: "active"));
        }
        return results;
    }
}

internal static class HermesSessions
{
    internal static IReadOnlyList<SessionRecord> Query(string root, string? sessionId, CancellationToken cancellationToken)
    {
        var database = Path.Combine(root, "state.db");
        using var connection = SessionProviders.OpenReadOnly(database);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id,title,cwd,profile_name,last_activity_at,model,source,hidden FROM sessions WHERE archived=0" +
            (sessionId is null ? "" : " AND id=$id") + " ORDER BY last_activity_at DESC";
        if (sessionId is not null) command.Parameters.AddWithValue("$id", sessionId);
        using var reader = command.ExecuteReader(); var results = new List<SessionRecord>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested(); var hidden = !reader.IsDBNull(7) && reader.GetInt64(7) != 0;
            results.Add(new(reader.GetString(0), SessionProviders.String(reader, 1), SessionProviders.String(reader, 2),
                SessionProviders.String(reader, 3), database, hidden ? "persistent-but-not-indexed" : "unknown",
                SessionProviders.Unix(reader, 4, false), SessionProviders.String(reader, 5), hidden ? "hidden" : "active"));
        }
        return results;
    }
}

internal static class WorkBuddySessions
{
    internal static IReadOnlyList<SessionRecord> Query(string root, string? sessionId, CancellationToken cancellationToken)
    {
        var database = Path.Combine(root, "workbuddy.db");
        using var connection = SessionProviders.OpenReadOnly(database);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id,custom_title,title,cwd,updated_at,model,visibility,status FROM sessions WHERE deleted_at IS NULL AND status!='archived'" +
            (sessionId is null ? "" : " AND id=$id") + " ORDER BY updated_at DESC";
        if (sessionId is not null) command.Parameters.AddWithValue("$id", sessionId);
        using var reader = command.ExecuteReader(); var results = new List<SessionRecord>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(new(reader.GetString(0), SessionProviders.String(reader, 1) ?? SessionProviders.String(reader, 2),
                SessionProviders.String(reader, 3), null, database,
                SessionProviders.String(reader, 6) == "hidden" ? "persistent-but-not-indexed" : "unknown",
                SessionProviders.Unix(reader, 4, true), SessionProviders.String(reader, 5), SessionProviders.String(reader, 7)));
        }
        return results;
    }
}
