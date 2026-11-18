using TaskBaton.Core;
using Microsoft.Data.Sqlite;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class SessionDiscoveryTests
{
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "fixture", "甲"); service.RegisterManual("agent-b", "fixture", "乙");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private string Db(string directory, string name, string sql)
    {
        var path = Path.Combine(root, directory, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
        return path;
    }

    private string Hermes(string directory, string id) => Db(directory, "state.db", """
        CREATE TABLE sessions(id TEXT,title TEXT,cwd TEXT,profile_name TEXT,last_activity_at REAL,model TEXT,source TEXT,hidden INTEGER,archived INTEGER);
        """ + $"INSERT INTO sessions VALUES('{id}','Review','/project','default',1700000000.5,'fixture-model','desktop',0,0);");

    [TestMethod]
    public async Task DiscoveryReadsOnlyTheRequestedAgentAndPreservesOtherCaches()
    {
        _ = Hermes("a", "a-session"); _ = Hermes("b", "b-session");
        service.ConfigureSessionDiscovery("agent-a", "hermes", Path.Combine(root, "a"));
        service.ConfigureSessionDiscovery("agent-b", "hermes", Path.Combine(root, "b"));
        var first = await service.QuerySessionsAsync("agent-a", true);
        Assert.IsTrue(first.Supported); Assert.IsFalse(first.IsCached); Assert.IsNull(first.Message);
        Assert.HasCount(1, first.Sessions); Assert.AreEqual("a-session", first.Sessions[0].SessionId);
        Assert.AreEqual("fixture-model", first.Sessions[0].Model);
        Assert.IsNull(service.CachedSessions("agent-b"));
        var cached = await service.QuerySessionsAsync("agent-a");
        Assert.IsTrue(cached.IsCached); Assert.AreEqual(first.ScannedAt, cached.ScannedAt);
    }

    [TestMethod]
    public async Task FreshValidationNeverTrustsAnExistingDisplayCache()
    {
        var db = Hermes("a", "stale-session");
        service.ConfigureSessionDiscovery("agent-a", "hermes", Path.Combine(root, "a"));
        _ = await service.QuerySessionsAsync("agent-a", true);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM sessions"; command.ExecuteNonQuery();
        }
        Assert.HasCount(1, service.CachedSessions("agent-a")!.Sessions);
        Assert.AreEqual("missing", (await service.ValidateDiscoveredSessionAsync("agent-a", "stale-session")).Status);
        Assert.AreEqual("missing", (await service.ValidateDiscoveredSessionAsync("agent-a", "latest")).Status);
    }

    [TestMethod]
    public async Task UnsupportedDiscoveryAndUnavailableHostStoreAreExplicit()
    {
        var unsupported = await service.QuerySessionsAsync("agent-a", true);
        Assert.IsFalse(unsupported.Supported); Assert.IsEmpty(unsupported.Sessions);
        Assert.AreEqual("unsupported", (await service.ValidateDiscoveredSessionAsync("agent-a", "known-id")).Status);
        service.ConfigureSessionDiscovery("agent-a", "hermes", Path.Combine(root, "missing"));
        var failed = await service.QuerySessionsAsync("agent-a", true);
        Assert.IsTrue(failed.Supported); Assert.IsNotNull(failed.Message);
        Assert.AreEqual("unavailable", (await service.ValidateDiscoveredSessionAsync("agent-a", "known-id")).Status);
        Assert.IsNull(service.CachedSessions("agent-a"));
    }

    [TestMethod]
    public async Task ZcodeSessionsRespectExactIdAndDesktopIndexWithoutReadingMessages()
    {
        _ = Db("zcode/cli/db", "db.sqlite", """
            CREATE TABLE session(id TEXT,title TEXT,directory TEXT,time_updated INTEGER,time_archived INTEGER,time_deleted INTEGER);
            INSERT INTO session VALUES('indexed','Review','/project',1700000000000,NULL,NULL);
            INSERT INTO session VALUES('hidden','CLI','/project',1700000001000,NULL,NULL);
            INSERT INTO session VALUES('archived','Old','/project',1700000002000,1700000002000,NULL);
            """);
        _ = Db("zcode/v2", "tasks-index.sqlite", "CREATE TABLE tasks(task_id TEXT); INSERT INTO tasks VALUES('indexed');");
        service.ConfigureSessionDiscovery("agent-a", "zcode", Path.Combine(root, "zcode"));
        var result = await service.QuerySessionsAsync("agent-a", true);
        Assert.HasCount(2, result.Sessions);
        Assert.AreEqual("native-visible", result.Sessions.Single(s => s.SessionId == "indexed").Visibility);
        Assert.AreEqual("persistent-but-not-indexed", result.Sessions.Single(s => s.SessionId == "hidden").Visibility);
        var exact = await service.ValidateDiscoveredSessionAsync("agent-a", "indexed");
        Assert.AreEqual("exists", exact.Status); Assert.AreEqual("indexed", exact.Session!.SessionId);
        Assert.AreEqual("missing", (await service.ValidateDiscoveredSessionAsync("agent-a", "archived")).Status);
    }

    [TestMethod]
    public async Task WorkBuddyDeletedAndArchivedSessionsAreNotResumeCandidates()
    {
        _ = Db("workbuddy", "workbuddy.db", """
            CREATE TABLE sessions(id TEXT,custom_title TEXT,title TEXT,cwd TEXT,updated_at INTEGER,model TEXT,visibility TEXT,status TEXT,deleted_at INTEGER);
            INSERT INTO sessions VALUES('alive','Custom','Base','/project',1700000000000,'model',NULL,'completed',NULL);
            INSERT INTO sessions VALUES('deleted',NULL,'Deleted','/project',1700000000000,'model',NULL,'completed',1);
            INSERT INTO sessions VALUES('archived',NULL,'Archived','/project',1700000000000,'model',NULL,'archived',NULL);
            """);
        service.ConfigureSessionDiscovery("agent-a", "workbuddy", Path.Combine(root, "workbuddy"));
        var result = await service.QuerySessionsAsync("agent-a", true);
        Assert.HasCount(1, result.Sessions); Assert.AreEqual("Custom", result.Sessions[0].Title);
        Assert.AreEqual("unknown", result.Sessions[0].Visibility);
    }

    [TestMethod]
    public async Task DshProjectionCannotInventADeletedPersistedSession()
    {
        var dsh = Path.Combine(root, "dsh"); var path = Path.Combine(dsh, "sessions", "project", "session-known", "session.v4.jsonl.zstd");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "Fixture only: discovery does not decompress transcript bodies.");
        var projection = Path.Combine(dsh, "storages", "session_projcache", "sessions", "session-known.json");
        Directory.CreateDirectory(Path.GetDirectoryName(projection)!);
        File.WriteAllText(projection, """
            {"version":7,"record":{"identity":{"cwd":"/project"},"rows":{"title":{"val":"Session review"},"sessionListMetadata":{"val":{"lastPromptAt":1700000000000}},"modelSelection":{"val":{"lastUsed":{"model":"fixture"}}}}}}
            """);
        service.ConfigureSessionDiscovery("agent-a", "dsh", dsh);
        var result = await service.QuerySessionsAsync("agent-a", true);
        Assert.HasCount(1, result.Sessions); Assert.AreEqual("Session review", result.Sessions[0].Title);
        Assert.AreEqual("fixture", result.Sessions[0].Model);
        File.Delete(path);
        Assert.AreEqual("missing", (await service.ValidateDiscoveredSessionAsync("agent-a", "session-known")).Status);
        Assert.HasCount(1, service.CachedSessions("agent-a")!.Sessions);
    }
}
