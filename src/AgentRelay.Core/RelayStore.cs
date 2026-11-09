using Microsoft.Data.Sqlite;

namespace AgentRelay.Core;

public sealed class RelayStore
{
    public string Home { get; }
    public string DatabasePath => Path.Combine(Home, "relay.db");

    public RelayStore(string home)
    {
        Home = Path.GetFullPath(home);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Home);
        using var connection = Open(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS agents (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS adapters (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS permissions (sender TEXT NOT NULL, recipient TEXT NOT NULL,
                decision TEXT NOT NULL, PRIMARY KEY(sender,recipient));
            CREATE TABLE IF NOT EXISTS tasks (id TEXT PRIMARY KEY, requester TEXT NOT NULL,
                idempotency_key TEXT NOT NULL, input_json TEXT NOT NULL, json TEXT NOT NULL,
                UNIQUE(requester,idempotency_key));
            CREATE TABLE IF NOT EXISTS callbacks (task_id TEXT NOT NULL, attempt_id TEXT NOT NULL,
                sender TEXT NOT NULL, key TEXT NOT NULL, input_json TEXT NOT NULL,
                result_json TEXT NOT NULL, PRIMARY KEY(task_id,attempt_id,sender,key));
            CREATE TABLE IF NOT EXISTS artifacts (id TEXT PRIMARY KEY, task_id TEXT NOT NULL,
                attempt_id TEXT NOT NULL, path TEXT NOT NULL UNIQUE, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS events (seq INTEGER PRIMARY KEY AUTOINCREMENT,
                event_id TEXT NOT NULL UNIQUE, task_id TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS outbox (task_id TEXT PRIMARY KEY, attempt_id TEXT NOT NULL,
                state TEXT NOT NULL, submissions INTEGER NOT NULL, lease_until TEXT, receipt TEXT);
            CREATE TABLE IF NOT EXISTS notifications (id INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id TEXT NOT NULL, event TEXT NOT NULL, message TEXT NOT NULL,
                created_at TEXT NOT NULL, is_read INTEGER NOT NULL DEFAULT 0,
                UNIQUE(task_id,event));
            CREATE TABLE IF NOT EXISTS executors (agent_id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS executions (task_id TEXT NOT NULL, attempt_id TEXT NOT NULL,
                json TEXT NOT NULL, PRIMARY KEY(task_id,attempt_id));
            CREATE TABLE IF NOT EXISTS resumers (agent_id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS cli_extensions (agent_id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS task_session_bindings (task_id TEXT NOT NULL, attempt_id TEXT NOT NULL,
                turn INTEGER NOT NULL, agent_id TEXT NOT NULL, json TEXT NOT NULL,
                PRIMARY KEY(task_id,attempt_id,turn,agent_id));
            CREATE TABLE IF NOT EXISTS session_discovery_sources (agent_id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS session_discovery_cache (agent_id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS desktop (id INTEGER PRIMARY KEY CHECK(id=1), executable TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS continuations (task_id TEXT NOT NULL, attempt_id TEXT NOT NULL,
                event TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(task_id,attempt_id,event));
            CREATE TABLE IF NOT EXISTS adapter_sources (id TEXT NOT NULL, version TEXT NOT NULL,
                json TEXT NOT NULL, PRIMARY KEY(id,version));
            CREATE TABLE IF NOT EXISTS migrations (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS migration_events (seq INTEGER PRIMARY KEY AUTOINCREMENT,
                migration_id TEXT NOT NULL, json TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT OR IGNORE INTO settings VALUES(1,$json)";
        insert.Parameters.AddWithValue("$json", RelayJson.Write(new RelaySettings()));
        insert.ExecuteNonQuery();
    }

    public SqliteConnection Open(bool readOnly = false)
    {
        RequireCurrentLocation();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        connection.Open();
        return connection;
    }

    public T Read<T>(Func<StoreSession, T> operation)
    {
        if (!File.Exists(DatabasePath))
            throw new RelayException("not_initialized", "请先运行 relay init。", 3);
        using var connection = Open(true);
        using var transaction = connection.BeginTransaction(deferred: true);
        return operation(new StoreSession(connection, transaction));
    }

    public T Write<T>(Func<StoreSession, T> operation)
    {
        if (!File.Exists(DatabasePath))
            throw new RelayException("not_initialized", "请先运行 relay init。", 3);
        try
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            RequireCurrentLocation();
            var result = operation(new StoreSession(connection, transaction));
            transaction.Commit();
            WindowsLedgerSignal.Publish();
            return result;
        }
        // Microsoft.Data.Sqlite already retries busy and locked operations until DefaultTimeout.
        catch (SqliteException error) when (error.SqliteErrorCode is SQLitePCL.raw.SQLITE_BUSY or SQLitePCL.raw.SQLITE_LOCKED)
        {
            var failure = new RelayException("database_busy", "中转站账本正被其他操作占用，请稍后重试。", 5);
            failure.Data["technical_detail"] = error.Message;
            throw failure;
        }
    }

    private void RequireCurrentLocation()
    {
        var moved = Path.Combine(Home, RelayService.RelocationMovedFile);
        if (File.Exists(moved))
            throw new RelayException("relocation_moved", $"中转站已迁移，请使用新目录：{RelayJson.Read<RelayRelocationRequest>(File.ReadAllText(moved)).Target}", 3);
        if (File.Exists(Path.Combine(Home, RelayService.RelocationIncompleteFile)))
            throw new RelayException("relocation_incomplete", "此目录的迁移尚未完成，请回到原中转站运行迁移校验。", 3);
    }
}

public sealed class StoreSession(SqliteConnection connection, SqliteTransaction transaction)
{
    public int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        return command.ExecuteNonQuery();
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Command(sql, parameters);
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read()) results.Add(map(reader));
        return results;
    }

    public T? Json<T>(string sql, params (string Name, object? Value)[] parameters) where T : class =>
        Query(sql, r => RelayJson.Read<T>(r.GetString(0)), parameters).SingleOrDefault();

    public TaskRecord Task(string id) => Json<TaskRecord>("SELECT json FROM tasks WHERE id=$id", ("$id", id))
        ?? throw new RelayException("task_not_found", $"未找到任务 {id}。", 3);

    public void Save(TaskRecord task)
    {
        task.Revision++;
        Execute("UPDATE tasks SET json=$json WHERE id=$id", ("$json", RelayJson.Write(task)), ("$id", task.TaskId));
    }

    public void Event(TaskRecord task, string name, string sender, DateTimeOffset now, string detail = "")
    {
        var entry = new AuditEvent(0, Guid.NewGuid().ToString(), task.TaskId, task.AttemptId, name, sender, now, detail);
        Execute("INSERT INTO events(event_id,task_id,json) VALUES($event,$task,$json)",
            ("$event", entry.EventId), ("$task", task.TaskId), ("$json", RelayJson.Write(entry)));
    }

    public void Notify(TaskRecord task, string name, string message, DateTimeOffset now) =>
        Execute("INSERT OR IGNORE INTO notifications(task_id,event,message,created_at) VALUES($task,$event,$message,$now)",
            ("$task", task.TaskId), ("$event", $"{task.AttemptId}:{name}"), ("$message", message), ("$now", now.ToString("O")));

    private SqliteCommand Command(string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }
}
