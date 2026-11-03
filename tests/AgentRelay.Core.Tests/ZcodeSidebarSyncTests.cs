using System.Text.Json;
using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class ZcodeSidebarSyncTests
{
    private string root = null!;
    private string zcode = null!;
    private string sessionDb = null!;
    private string index = null!;
    private RelayService service = null!;
    private CliRecipe recipe = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString("N"));
        zcode = Path.Combine(root, "zcode");
        sessionDb = Path.Combine(zcode, "cli", "db", "db.sqlite");
        index = Path.Combine(zcode, "v2", "tasks-index.sqlite");
        Execute(sessionDb, """
            CREATE TABLE session(id TEXT PRIMARY KEY,title TEXT,directory TEXT,time_created INTEGER,time_updated INTEGER,
              last_outcome TEXT,task_type TEXT,time_deleted INTEGER,time_archived INTEGER);
            INSERT INTO session VALUES('sess_fixture','Original',$directory,100,200,'completed','interactive',NULL,NULL);
            """, ("$directory", root));
        CreateIndex(index);
        File.WriteAllText(Path.Combine(zcode, "v2", "setting.json"), "{}");
        service = new RelayService(Path.Combine(root, "relay")) { ZcodeUserDirectory = zcode };
        service.Initialize();
        service.RegisterManual("a", "fixture", "A");
        service.RegisterManual("b", "ZCode Desktop", "B");
        service.ConfigureSessionDiscovery("b", "zcode", zcode);

        var script = Path.Combine(root, "fake-zcode.ps1");
        File.WriteAllText(script, """
            param([string]$Program,[string]$Session,[string]$Prompt)
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $marker=[regex]::Match($Prompt,'RELAY-[A-Z0-9]+').Value
            $file=[regex]::Match($Prompt,'Create the file (.+?) containing').Groups[1].Value
            if($file){[IO.File]::WriteAllText($file,$marker)}
            if($env:AR_ZCODE_SYNC_TEST_STARTED){
              [IO.File]::WriteAllText($env:AR_ZCODE_SYNC_TEST_STARTED,'started')
              while(!(Test-Path -LiteralPath $env:AR_ZCODE_SYNC_TEST_RELEASE)){Start-Sleep -Milliseconds 25}
            }
            $id=if($Session -eq 'create'){'sess_fixture'}else{$Session}
            @{session_id=$id;reply=$(if($marker){$marker}else{'accepted'});accepted=$true}|ConvertTo-Json -Compress
            """);
        CliCommand Command(string session) => new("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-File", script,
            "zcode.cjs", session, "{prompt}"], null, root);
        recipe = new(1, "Fake Zcode", "ZCode Desktop", Command("create"), Command("{session_id}"), [], "", "tests", "MIT", "fixture");
        service.ImportCliRecipe("b", RelayJson.Write(recipe));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private static void CreateIndex(string path) => Execute(path, """
        CREATE TABLE tasks(workspace_key TEXT,workspace_path TEXT,workspace_identity TEXT,task_id TEXT,title TEXT,task_status TEXT,
          provider TEXT,mode TEXT,model TEXT,created_at INTEGER,updated_at INTEGER,unread_at INTEGER,last_unread_at INTEGER,
          pinned INTEGER,archived INTEGER,deleted INTEGER,title_overridden INTEGER,searchable_text TEXT,meta_json TEXT,
          host_id TEXT,root_path TEXT,project_id TEXT,cli_title TEXT,cli_title_source TEXT,cli_created_at INTEGER,
          cli_updated_at INTEGER,cli_last_outcome TEXT,cli_deleted INTEGER DEFAULT 0,PRIMARY KEY(workspace_key,task_id));
        CREATE TABLE projects(project_id TEXT PRIMARY KEY,deleted INTEGER);
        CREATE TABLE project_roots(project_id TEXT,root_path TEXT,is_primary INTEGER);
        CREATE TABLE task_group_admissions(task_id TEXT);
        CREATE TABLE task_group_view_node_orders(node_type TEXT,node_key TEXT);
        """);

    private static SqliteConnection Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(string path, string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string path, string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command.ExecuteScalar();
    }

    private CliSidebarSyncResult Sync(string session = "sess_fixture") =>
        ZcodeTaskIndexVisibility.Sync(service.CliExtension("b")!, session, zcode, service.Home)!;

    [TestMethod]
    public void InsertsAndUpdatesTenTurnsUsingOnePreSyncBackup()
    {
        Execute(index, "INSERT INTO projects VALUES('project',0); INSERT INTO project_roots VALUES('project',$root,1);", ("$root", root));
        var first = Sync();
        Assert.AreEqual("synced", first.Status, first.Message);
        Assert.IsNotNull(first.BackupPath);
        Assert.AreEqual(0L, Scalar(first.BackupPath, "SELECT COUNT(*) FROM tasks"));
        for (var turn = 1; turn <= 10; turn++)
        {
            Execute(sessionDb, "UPDATE session SET title=$title,time_updated=$time,last_outcome='error' WHERE id='sess_fixture'",
                ("$title", "Turn " + turn), ("$time", 200 + turn));
            var result = Sync();
            Assert.AreEqual("synced", result.Status, result.Message);
            Assert.AreEqual(first.BackupPath, result.BackupPath);
        }
        Assert.AreEqual(1L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
        Assert.AreEqual("Turn 10", Scalar(index, "SELECT cli_title FROM tasks"));
        Assert.AreEqual("error", Scalar(index, "SELECT task_status FROM tasks"));
        Assert.AreEqual("project", Scalar(index, "SELECT project_id FROM tasks"));
        Assert.AreEqual("relay-cli-sync", Scalar(index, "SELECT json_extract(meta_json,'$.syncSource') FROM tasks"));
        Assert.AreEqual("ok", Scalar(index, "PRAGMA integrity_check"));
        Assert.HasCount(1, Directory.GetFiles(Path.Combine(service.Home, ".zcode-sidebar-backups"), "*.sqlite"));
    }

    [TestMethod]
    public void UpdatingRelayRowsPreservesUserChoicesAndUnrelatedMetadata()
    {
        Assert.AreEqual("synced", Sync().Status);
        Execute(index, """
            UPDATE tasks SET title='Custom',title_overridden=1,cli_title='Custom CLI',cli_title_source='custom',
              pinned=1,archived=1,meta_json=json_set(meta_json,'$.other','preserved');
            """);
        Execute(sessionDb, "UPDATE session SET title='New CLI title',time_updated=900,last_outcome='error'");
        Assert.AreEqual("synced", Sync().Status);
        Assert.AreEqual("Custom", Scalar(index, "SELECT title FROM tasks"));
        Assert.AreEqual("Custom CLI", Scalar(index, "SELECT cli_title FROM tasks"));
        Assert.AreEqual("custom", Scalar(index, "SELECT cli_title_source FROM tasks"));
        Assert.AreEqual(1L, Scalar(index, "SELECT pinned FROM tasks"));
        Assert.AreEqual(1L, Scalar(index, "SELECT archived FROM tasks"));
        Assert.AreEqual("Custom", Scalar(index, "SELECT json_extract(meta_json,'$.title') FROM tasks"));
        Assert.AreEqual("preserved", Scalar(index, "SELECT json_extract(meta_json,'$.other') FROM tasks"));
        Assert.AreEqual(900L, Scalar(index, "SELECT cli_updated_at FROM tasks"));
    }

    [TestMethod]
    public void DesktopOwnedAndDeletedRowsAreNotChanged()
    {
        Assert.AreEqual("synced", Sync().Status);
        Execute(index, "UPDATE tasks SET meta_json='{\"desktop\":true}',title='Desktop title',pinned=1");
        Execute(sessionDb, "UPDATE session SET title='CLI title',time_updated=900");
        Assert.AreEqual("skipped", Sync().Status);
        Assert.AreEqual("Desktop title", Scalar(index, "SELECT title FROM tasks"));
        Assert.AreEqual("{\"desktop\":true}", Scalar(index, "SELECT meta_json FROM tasks"));
        Execute(index, "UPDATE tasks SET meta_json='{\"syncSource\":\"relay-cli-sync\"}',deleted=1");
        Assert.AreEqual("skipped", Sync().Status);
        Assert.AreEqual(1L, Scalar(index, "SELECT deleted FROM tasks"));
        Execute(index, "UPDATE tasks SET deleted=0,cli_deleted=1");
        Assert.AreEqual("skipped", Sync().Status);
        Assert.AreEqual(1L, Scalar(index, "SELECT cli_deleted FROM tasks"));
    }

    [TestMethod]
    public void DeletedArchivedAndNonSidebarSessionsAreSkipped()
    {
        Execute(sessionDb, "UPDATE session SET time_deleted=1");
        Assert.AreEqual("skipped", Sync().Status);
        Execute(sessionDb, "UPDATE session SET time_deleted=NULL,time_archived=1");
        Assert.AreEqual("skipped", Sync().Status);
        Execute(sessionDb, "UPDATE session SET time_archived=NULL,task_type='selection_side_chat'");
        Assert.AreEqual("skipped", Sync().Status);
        Execute(sessionDb, "UPDATE session SET task_type='subagent_child'");
        Assert.AreEqual("skipped", Sync().Status);
        Execute(sessionDb, "UPDATE session SET id='sess_subagent_fixture',task_type='interactive'");
        Assert.AreEqual("skipped", Sync("sess_subagent_fixture").Status);
        Assert.AreEqual(0L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
        Assert.IsFalse(Directory.Exists(Path.Combine(service.Home, ".zcode-sidebar-backups")));
    }

    [TestMethod]
    public void MovedIndexUsesSharedSettingsResolutionAndGetsItsOwnBackup()
    {
        var original = Sync();
        Assert.AreEqual("synced", original.Status);
        var moved = Path.Combine(root, "moved");
        var movedIndex = Path.Combine(moved, ".zcode", "v2", "tasks-index.sqlite");
        CreateIndex(movedIndex);
        File.WriteAllText(Path.Combine(zcode, "v2", "setting.json"), JsonSerializer.Serialize(new { dataBaseDir = moved }));
        var next = Sync();
        Assert.AreEqual("synced", next.Status, next.Message);
        Assert.AreEqual(movedIndex, next.IndexPath);
        Assert.AreNotEqual(original.BackupPath, next.BackupPath);
        Assert.AreEqual(1L, Scalar(movedIndex, "SELECT COUNT(*) FROM tasks"));
        Assert.AreEqual(0L, Scalar(next.BackupPath!, "SELECT COUNT(*) FROM tasks"));
    }

    [TestMethod]
    public async Task CleanupFindsTheMovedIndexUsedForProbeSync()
    {
        var moved = Path.Combine(root, "moved");
        var movedIndex = Path.Combine(moved, ".zcode", "v2", "tasks-index.sqlite");
        CreateIndex(movedIndex);
        File.WriteAllText(Path.Combine(zcode, "v2", "setting.json"), JsonSerializer.Serialize(new { dataBaseDir = moved }));
        Assert.AreEqual("synced", Sync().Status);
        var extension = service.CliExtension("b")!;
        var probe = new CliProbe("probe", "create", "RELAY-AAAAAAAAAA", "sess_fixture", true, true, null, DateTimeOffset.UtcNow, FileWritten: true);
        var artifact = new ProbeArtifact(probe.ProbeId, "create", probe.Marker, probe.SessionId,
            new SessionDiscoverySource("zcode", zcode), true, root, recipe.Create, true);
        new RelayStore(service.Home).Write(s => s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id='b'",
            ("$json", RelayJson.Write(extension with { CreateProbe = probe, ProbeArtifacts = [artifact] }))));
        var cleanup = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(cleanup.Items.Any(i => i.Kind == "index" && i.Status == "synced" && i.Path == movedIndex));
        Assert.AreEqual(1L, Scalar(movedIndex, "SELECT deleted FROM tasks WHERE task_id='sess_fixture'"));
        Assert.AreEqual(1L, Scalar(movedIndex, "SELECT archived FROM tasks WHERE task_id='sess_fixture'"));
        Assert.AreEqual(0L, Scalar(sessionDb, "SELECT COUNT(*) FROM session WHERE id='sess_fixture'"));
    }

    [TestMethod]
    public void InvalidSettingsAndSchemaAreReportedWithoutWriting()
    {
        File.WriteAllText(Path.Combine(zcode, "v2", "setting.json"), "invalid json");
        Assert.AreEqual("failed", Sync().Status);
        Assert.AreEqual(0L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
        File.WriteAllText(Path.Combine(zcode, "v2", "setting.json"), "{}");
        Execute(index, "DROP TABLE project_roots");
        var failed = Sync();
        Assert.AreEqual("failed", failed.Status);
        StringAssert.Contains(failed.Message!, "project_roots");
        Assert.AreEqual(0L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
    }

    [TestMethod]
    public async Task ProbeShowsSyncOutcomeAndPreferenceSurvivesReimport()
    {
        service.SetCliSidebarSync("b", false, false);
        service.ImportCliRecipe("b", RelayJson.Write(recipe));
        Assert.IsFalse(service.CliExtension("b")!.SidebarSync!.Enabled);
        var disabled = await service.RunCliProbeAsync("b", "create");
        Assert.IsTrue(disabled.MachinePassed, disabled.Error);
        Assert.AreEqual("disabled", disabled.SidebarSync!.Status);
        Assert.AreEqual(0L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
        service.SetCliSidebarSync("b", true, false);
        var enabled = await service.RunCliProbeAsync("b", "create");
        Assert.IsTrue(enabled.MachinePassed, enabled.Error);
        Assert.AreEqual("synced", enabled.SidebarSync!.Status, enabled.SidebarSync.Message);
        service.ConfirmCliProbe("b", enabled.ProbeId, true);
        var resumed = await service.RunCliProbeAsync("b", "resume");
        Assert.IsTrue(resumed.MachinePassed, resumed.Error);
        Assert.AreEqual("synced", resumed.SidebarSync!.Status, resumed.SidebarSync.Message);
    }

    [TestMethod]
    public async Task FileCreateAndResumeSyncAndReportFailureWithoutChangingSubmission()
    {
        service.SetPermission("a", "b", "allow");
        var source = Path.Combine(root, "request.md"); File.WriteAllText(source, "request");
        var create = service.CreateFileHandoff(new("a", "b", source, "Review")).Task;
        var sent = await service.SendFileHandoffAsync(create.TaskId, "Read this request");
        Assert.AreEqual("submitted", sent.Task.CliFileStatus);
        Assert.AreEqual(1L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
        Execute(sessionDb, "UPDATE session SET title='After resume',time_updated=500");
        var resume = service.CreateFileHandoff(new("a", "b", source, "Review another request")).Task;
        service.BindManualTaskSession(resume.TaskId, "b", "sess_fixture", root);
        var resumed = await service.SendFileHandoffAsync(resume.TaskId, "Continue in this conversation");
        Assert.AreEqual("submitted", resumed.Task.CliFileStatus);
        Assert.AreEqual("After resume", Scalar(index, "SELECT cli_title FROM tasks"));

        Execute(index, "DROP TABLE project_roots");
        var failure = service.CreateFileHandoff(new("a", "b", source, "Review a third request")).Task;
        var submitted = await service.SendFileHandoffAsync(failure.TaskId, "Read this request");
        Assert.AreEqual("submitted", submitted.Task.CliFileStatus);
        Assert.IsTrue(service.Audit(failure.TaskId).Any(e => e.Event == "zcode.sidebar_sync_failed" && e.Detail.Contains("project_roots")));
    }

    [TestMethod]
    public async Task TransportAcceptanceIsIndependentOfIndexFailure()
    {
        var source = Path.Combine(root, "request.md"); File.WriteAllText(source, "request");
        var task = service.CreateFileHandoff(new("a", "b", source, "Review")).Task;
        var extension = service.CliExtension("b")! with { CreateEvidence = new("task", 0, "sess_fixture", DateTimeOffset.UtcNow) };
        var adapter = new CliRecipeDeliveryAdapter(extension, zcode);
        Execute(index, "DROP TABLE project_roots");
        var result = await adapter.DeliverAsync(task, service.Home, TimeSpan.FromSeconds(20), CancellationToken.None);
        Assert.AreEqual("accepted", result.Status);
        Assert.AreEqual("failed", result.SidebarSync!.Status);
    }

    [TestMethod]
    public async Task DisablingDuringAnInFlightTurnStopsSyncForAllReceiptPaths()
    {
        var started = Path.Combine(root, "started");
        var release = Path.Combine(root, "release");
        var environment = new Dictionary<string, string>
        {
            ["AR_ZCODE_SYNC_TEST_STARTED"] = started, ["AR_ZCODE_SYNC_TEST_RELEASE"] = release
        };
        service.ImportCliRecipe("b", RelayJson.Write(recipe with { Create = recipe.Create with { Environment = environment } }));

        async Task DisableWhileRunning(Task operation)
        {
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (!File.Exists(started) && DateTime.UtcNow < deadline) await Task.Delay(25);
                Assert.IsTrue(File.Exists(started), "The fixture CLI did not start.");
                service.SetCliSidebarSync("b", false, false);
            }
            finally { File.WriteAllText(release, "release"); }
            await operation;
        }
        void Reset()
        {
            File.Delete(started); File.Delete(release);
            service.SetCliSidebarSync("b", true, false);
        }

        var probe = service.RunCliProbeAsync("b", "create");
        await DisableWhileRunning(probe);
        Assert.AreEqual("disabled", (await probe).SidebarSync!.Status);
        Assert.IsTrue((await probe).MachinePassed);

        Reset();
        service.SetPermission("a", "b", "allow");
        var source = Path.Combine(root, "request.md"); File.WriteAllText(source, "request");
        var task = service.CreateFileHandoff(new("a", "b", source, "Review")).Task;
        var fileSend = service.SendFileHandoffAsync(task.TaskId, "Read the request");
        await DisableWhileRunning(fileSend);
        Assert.AreEqual("submitted", (await fileSend).Task.CliFileStatus);

        Reset();
        var extension = service.CliExtension("b")! with { CreateEvidence = new("task", 0, "sess_fixture", DateTimeOffset.UtcNow) };
        var transport = new CliRecipeDeliveryAdapter(extension, zcode).DeliverAsync(task, service.Home, TimeSpan.FromSeconds(20), CancellationToken.None);
        await DisableWhileRunning(transport);
        Assert.AreEqual("accepted", (await transport).Status);
        Assert.AreEqual("disabled", (await transport).SidebarSync!.Status);
        Assert.AreEqual(0L, Scalar(index, "SELECT COUNT(*) FROM tasks"));
    }
}
