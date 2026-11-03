using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Core.Tests;

[TestClass]
public sealed class ProbeCleanupTests
{
    private string root = null!;
    private string zcode = null!;
    private string project = null!;
    private string database = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString("N"));
        zcode = Path.Combine(root, "zcode"); project = Path.Combine(root, "project");
        service = new RelayService(Path.Combine(root, "relay")) { ZcodeUserDirectory = Path.Combine(root, "user-zcode") };
        service.Initialize();
        foreach (var id in new[] { "a", "b", "c" }) service.RegisterManual(id, "fixture", id);
        database = Path.Combine(zcode, "cli", "db", "db.sqlite");
        Execute(database, """
            CREATE TABLE session(id TEXT PRIMARY KEY, directory TEXT);
            CREATE TABLE message(id TEXT PRIMARY KEY, session_id TEXT);
            CREATE TABLE part(id TEXT PRIMARY KEY, session_id TEXT);
            CREATE TABLE links(child_session_id TEXT, parent_session_id TEXT);
            """);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private static SqliteConnection Open(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open(); return connection;
    }

    private static void Execute(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open(path); using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open(path); using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void AddSession(string id) => Execute(database, """
        INSERT INTO session VALUES($id,$project);
        INSERT INTO message VALUES($id,$id);
        INSERT INTO part VALUES($id,$id);
        """, ("$id", id), ("$project", project));

    private string Index(string folder)
    {
        var path = Path.Combine(folder, "v2", "tasks-index.sqlite");
        Execute(path, """
            CREATE TABLE tasks(task_id TEXT PRIMARY KEY, deleted INTEGER, archived INTEGER);
            CREATE TABLE task_group_admissions(task_id TEXT);
            CREATE TABLE task_group_view_node_orders(node_type TEXT, node_key TEXT);
            INSERT INTO tasks VALUES('probe-session',0,0),('another-session',0,0);
            INSERT INTO task_group_admissions VALUES('probe-session'),('another-session');
            INSERT INTO task_group_view_node_orders VALUES('task','["probe-session"]'),('task','["another-session"]'),('group','["probe-session"]');
            """);
        return path;
    }

    private CliCommand FixtureCommand(string session = "probe-session")
    {
        var code = "[Console]::InputEncoding=[Text.UTF8Encoding]::new($false);[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);" +
            "$p=[Console]::In.ReadToEnd();$m=[regex]::Match($p,'RELAY-[A-Z0-9]+').Value;" +
            "$f=[regex]::Match($p,'Create the file (.+?) containing').Groups[1].Value;[IO.File]::WriteAllText($f,$m);" +
            "[IO.File]::WriteAllText($env:RELAY_FIXTURE_STARTED,'yes');" +
            "while(!(Test-Path -LiteralPath $env:RELAY_FIXTURE_RELEASE)){Start-Sleep -Milliseconds 25};" +
            "@{session_id=$env:RELAY_FIXTURE_SESSION;reply=$m;accepted=$true}|ConvertTo-Json -Compress";
        return new("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", code], "{prompt}", root,
            new Dictionary<string, string> { ["RELAY_FIXTURE_STARTED"] = Path.Combine(root, "started"),
                ["RELAY_FIXTURE_RELEASE"] = Path.Combine(root, "release"), ["RELAY_FIXTURE_SESSION"] = session });
    }

    private void Import(string agent, CliCommand command)
    {
        var recipe = new CliRecipe(1, "Fixture", "fixture", command, command with { Input = "{session_id}\n{prompt}" },
            [], "fixture", "tests", "MIT", "Isolated fixture.");
        service.ImportCliRecipe(agent, RelayJson.Write(recipe));
    }

    private void Seed(string agent, string id, SessionDiscoverySource source, CliCommand? command = null)
    {
        command ??= FixtureCommand(id); Import(agent, command);
        var current = service.CliExtension(agent)!;
        var create = new CliProbe("create-" + agent, "create", "RELAY-AAAAAAAAAA", id, true, true, null, DateTimeOffset.UtcNow, FileWritten: true);
        var resume = new CliProbe("resume-" + agent, "resume", "RELAY-BBBBBBBBBB", id, true, true, null, DateTimeOffset.UtcNow, FileWritten: true);
        var artifact = new ProbeArtifact(create.ProbeId, "create", create.Marker, id, source, true, project, command, true);
        var record = current with { CreateProbe = create, ResumeProbe = resume, ProbeArtifacts = [artifact] };
        new RelayStore(service.Home).Write(s => s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id=$id",
            ("$json", RelayJson.Write(record)), ("$id", agent)));
    }

    private async Task<CliProbe> Probe(string session, bool newSession)
    {
        Import("b", FixtureCommand(session));
        service.ConfigureSessionDiscovery("b", "zcode", zcode);
        var pending = service.RunCliProbeAsync("b", "create");
        try
        {
            await WaitForFile(Path.Combine(root, "started"));
            if (newSession) AddSession(session);
        }
        finally { File.WriteAllText(Path.Combine(root, "release"), "yes"); }
        return await pending;
    }

    private static async Task WaitForFile(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) await Task.Delay(25);
        Assert.IsTrue(File.Exists(path), "Fixture process did not start.");
    }

    [TestMethod]
    public async Task NewProbeIsOwnedButExistingHostSessionIsNeverClaimed()
    {
        AddSession("existing-session");
        var probe = await Probe("existing-session", false);
        Assert.IsTrue(probe.MachinePassed, probe.Error);
        StringAssert.Contains(probe.CleanupWarning!, "探针前已存在");
        var result = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i => i.SessionId == "existing-session" && i.Status == "unverified"));
        Assert.AreEqual(1L, Scalar(database, "SELECT COUNT(*) FROM session WHERE id='existing-session'"));
    }

    [TestMethod]
    public async Task UnreadablePreProbeStoreReportsWhyOwnershipCannotBeEstablished()
    {
        Execute(database, "DROP TABLE session");
        Import("b", FixtureCommand()); service.ConfigureSessionDiscovery("b", "zcode", zcode);
        var pending = service.RunCliProbeAsync("b", "create");
        try
        {
            await WaitForFile(Path.Combine(root, "started"));
            Execute(database, "CREATE TABLE session(id TEXT PRIMARY KEY, directory TEXT)");
            AddSession("probe-session");
        }
        finally { File.WriteAllText(Path.Combine(root, "release"), "yes"); }
        var probe = await pending;
        Assert.IsTrue(probe.MachinePassed, probe.Error);
        StringAssert.Contains(probe.CleanupWarning!, "无法读取探针前");
        var result = await service.CleanupCliProbeAsync("b");
        var item = result.Items.Single(i => i.Kind == "session");
        Assert.AreEqual("unverified", item.Status); StringAssert.Contains(item.Message, "无法读取探针前");
        Assert.AreEqual(1L, Scalar(database, "SELECT COUNT(*) FROM session WHERE id='probe-session'"));
    }

    [TestMethod]
    public async Task LockedMarkerAfterCompletedProbeCanBeRetried()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing is required for this fixture.");
        Import("b", FixtureCommand()); service.ConfigureSessionDiscovery("b", "zcode", zcode);
        var pending = service.RunCliProbeAsync("b", "create");
        await WaitForFile(Path.Combine(root, "started")); AddSession("probe-session");
        var path = Directory.GetFiles(Path.Combine(service.Home, ".cli-probes"), "RELAY-*.txt").Single();
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            File.WriteAllText(Path.Combine(root, "release"), "yes");
            var probe = await pending;
            Assert.IsTrue(probe.MachinePassed, probe.Error);
            StringAssert.Contains(probe.CleanupWarning!, "探针文件未能自动清理");
            Assert.IsTrue(service.CliExtension("b")!.ProbeArtifacts!.Single().Completed);
            var first = await service.CleanupCliProbeAsync("b");
            Assert.IsTrue(first.Items.Any(i => i.Kind == "file" && i.Status == "failed" && i.Path == path));
        }
        var retry = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(retry.Items.Any(i => i.Kind == "file" && i.Status == "removed" && i.Path == path));
        Assert.IsEmpty((await service.CleanupCliProbeAsync("b")).Items);
    }

    [TestMethod]
    public async Task CleanupRemovesOnlyOwnedSessionAndPreservesVerification()
    {
        AddSession("another-session");
        var probe = await Probe("probe-session", true);
        service.ConfirmCliProbe("b", probe.ProbeId, true);
        var current = service.CliExtension("b")!;
        Assert.IsTrue(current.ProbeArtifacts!.Single().OwnedSession);
        var resume = new CliProbe("resume", "resume", "RELAY-CCCCCCCCCC", probe.SessionId, true, true, null, DateTimeOffset.UtcNow, FileWritten: true);
        new RelayStore(service.Home).Write(s => s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id='b'",
            ("$json", RelayJson.Write(current with { ResumeProbe = resume }))));

        var dir = Path.Combine(zcode, "cli", "artifacts", "probe-session"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "output.txt"), "probe");
        var rollout = Path.Combine(zcode, "cli", "rollout"); Directory.CreateDirectory(rollout);
        File.WriteAllText(Path.Combine(rollout, "model-io-probe-session.jsonl"), "probe");
        File.WriteAllText(Path.Combine(rollout, "model-io-another-probe-session.jsonl"), "other");
        var result = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i => i.SessionId == "probe-session" && i.Kind == "session" && i.Status == "removed"));
        Assert.IsTrue(result.Items.Any(i => i.Kind == "file" && i.Status == "unverified"));
        Assert.AreEqual(0L, Scalar(database, "SELECT COUNT(*) FROM session WHERE id='probe-session'"));
        Assert.AreEqual(1L, Scalar(database, "SELECT COUNT(*) FROM session WHERE id='another-session'"));
        Assert.AreEqual(0L, Scalar(database, "SELECT COUNT(*) FROM part WHERE session_id='probe-session'"));
        Assert.IsFalse(Directory.Exists(dir));
        Assert.IsFalse(File.Exists(Path.Combine(rollout, "model-io-probe-session.jsonl")));
        Assert.IsTrue(File.Exists(Path.Combine(rollout, "model-io-another-probe-session.jsonl")));
        var after = new RelayService(service.Home).CliExtension("b")!;
        Assert.IsTrue(after.CanCreate); Assert.IsTrue(after.CanResume); Assert.IsTrue(after.CreateProbe!.SessionCleaned);
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.RunCliProbeAsync("b", "resume"));
        Assert.AreEqual("cli_probe_cleaned", error.Code);
    }

    [TestMethod]
    public async Task BoundTaskAndOtherAgentsProbeAreNotRemoved()
    {
        AddSession("probe-session"); AddSession("other-probe");
        var source = new SessionDiscoverySource("zcode", zcode);
        Seed("b", "probe-session", source); Seed("c", "other-probe", source);
        var file = Path.Combine(root, "request.txt"); File.WriteAllText(file, "Review");
        var task = service.CreateFileHandoff(new("a", "b", file)).Task;
        service.BindManualTaskSession(task.TaskId, "b", "probe-session");
        var result = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i => i.SessionId == "probe-session" && i.Status == "in_use"));
        Assert.AreEqual(2L, Scalar(database, "SELECT COUNT(*) FROM session"));
        Assert.IsFalse(service.CliExtension("b")!.CreateProbe!.SessionCleaned);
        Assert.HasCount(1, service.CliExtension("c")!.ProbeArtifacts!);
    }

    [TestMethod]
    public async Task CleaningAnEarlierProbeAlsoBlocksCurrentProbeUsingTheSameHostSession()
    {
        AddSession("probe-session"); Seed("b", "probe-session", new("zcode", zcode));
        var current = service.CliExtension("b")!;
        var later = current.CreateProbe! with { ProbeId = "later", Marker = "RELAY-DDDDDDDDDD" };
        var unowned = current.ProbeArtifacts!.Single() with { ProbeId = later.ProbeId, Marker = later.Marker, OwnedSession = false };
        new RelayStore(service.Home).Write(s => s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id='b'",
            ("$json", RelayJson.Write(current with { CreateProbe = later, ProbeArtifacts = [..current.ProbeArtifacts!, unowned] }))));
        var result = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i => i.Kind == "session" && i.Status == "removed"));
        Assert.IsTrue(service.CliExtension("b")!.CreateProbe!.SessionCleaned);
        var error = await Assert.ThrowsExactlyAsync<RelayException>(() => service.RunCliProbeAsync("b", "resume"));
        Assert.AreEqual("cli_probe_cleaned", error.Code);
    }

    [TestMethod]
    public async Task ZcodeIndexesAreBackedUpAndSyncedWithoutChangingOtherTasks()
    {
        AddSession("probe-session");
        var globalIndex = Index(zcode); var projectIndex = Index(Path.Combine(project, ".zcode"));
        Seed("b", "probe-session", new("zcode", zcode));
        var result = await service.CleanupCliProbeAsync("b");
        foreach (var path in new[] { globalIndex, projectIndex })
        {
            var item = result.Items.Single(i => i.Kind == "index" && i.Path == path);
            Assert.AreEqual("synced", item.Status); Assert.IsTrue(File.Exists(item.BackupPath));
            Assert.AreEqual(1L, Scalar(item.BackupPath!, "SELECT COUNT(*) FROM tasks WHERE task_id='probe-session' AND deleted=0 AND archived=0"));
            Assert.AreEqual(1L, Scalar(path, "SELECT COUNT(*) FROM tasks WHERE task_id='probe-session' AND deleted=1 AND archived=1"));
            Assert.AreEqual(1L, Scalar(path, "SELECT COUNT(*) FROM tasks WHERE task_id='another-session' AND deleted=0 AND archived=0"));
            Assert.AreEqual(0L, Scalar(path, "SELECT COUNT(*) FROM task_group_admissions WHERE task_id='probe-session'"));
            Assert.AreEqual(1L, Scalar(path, "SELECT COUNT(*) FROM task_group_view_node_orders WHERE node_type='group'"));
            Assert.AreEqual(0L, Scalar(path, "SELECT COUNT(*) FROM task_group_view_node_orders WHERE node_type='task' AND node_key LIKE '%probe-session%'"));
            Assert.AreEqual(1L, Scalar(path, "SELECT COUNT(*) FROM pragma_integrity_check WHERE integrity_check='ok'"));
        }
        Assert.IsEmpty((await service.CleanupCliProbeAsync("b")).Items);
    }

    [TestMethod]
    public async Task LockedIndexIsReportedAndRetriedAfterSessionWasRemoved()
    {
        AddSession("probe-session"); var index = Index(zcode);
        Seed("b", "probe-session", new("zcode", zcode));
        using (var connection = Open(index))
        using (var transaction = connection.BeginTransaction())
        {
            var result = await service.CleanupCliProbeAsync("b");
            Assert.IsTrue(result.Items.Any(i => i.Kind == "session" && i.Status == "removed"));
            Assert.IsTrue(result.Items.Any(i => i.Kind == "index" && i.Status == "failed" && i.Path == index));
            Assert.HasCount(1, service.CliExtension("b")!.ProbeArtifacts!);
            transaction.Rollback();
        }
        var retry = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(retry.Items.Any(i => i.Kind == "session" && i.Status == "absent"));
        Assert.IsTrue(retry.Items.Any(i => i.Kind == "index" && i.Status == "synced" && i.Path == index));
        Assert.IsEmpty((await service.CleanupCliProbeAsync("b")).Items);
    }

    [TestMethod]
    public void AbsentIndexEntryIsAReadOnlyNoOp()
    {
        var index = Index(zcode); var backups = Path.Combine(root, "backups");
        var result = ZcodeProbeCleanup.SyncIndex(index, "not-indexed", backups);
        Assert.AreEqual("absent", result.Status); Assert.IsNull(result.BackupPath); Assert.IsFalse(Directory.Exists(backups));
        Assert.AreEqual(2L, Scalar(index, "SELECT COUNT(*) FROM tasks WHERE deleted=0 AND archived=0"));
    }

    private (string Root, string Database, CliCommand Command) HermesFixture()
    {
        var hermes = Path.Combine(root, "hermes-data"); var path = Path.Combine(hermes, "state.db");
        Execute(path, """
            CREATE TABLE sessions(id TEXT PRIMARY KEY, parent_session_id TEXT, chat_id TEXT, model_config TEXT);
            CREATE TABLE state_meta(key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO sessions(id) VALUES('probe-session');
            """);
        var script = Path.Combine(root, "fake-hermes.ps1");
        File.WriteAllText(script, "[IO.File]::WriteAllText($env:RELAY_DELETE_STARTED,'yes');" +
            "while(!(Test-Path -LiteralPath $env:RELAY_DELETE_RELEASE)){Start-Sleep -Milliseconds 25}");
        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "hermes.cmd" : "hermes");
        if (OperatingSystem.IsWindows())
            File.WriteAllText(executable, "@echo off\r\npwsh -NoLogo -NoProfile -NonInteractive -File \"%~dp0fake-hermes.ps1\" %*\r\n");
        else
        {
            File.WriteAllText(executable, "#!/bin/sh\nexec pwsh -NoLogo -NoProfile -NonInteractive -File \"$(dirname \"$0\")/fake-hermes.ps1\" \"$@\"\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        var command = new CliCommand(executable, [], "{prompt}", root,
            new Dictionary<string, string> { ["RELAY_DELETE_STARTED"] = Path.Combine(root, "delete-started"),
                ["RELAY_DELETE_RELEASE"] = Path.Combine(root, "delete-release") });
        return (hermes, path, command);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HermesUsesItsDeleteCommandForAnUnrelatedProbeSession(bool useAcpEntry)
    {
        var fixture = HermesFixture();
        var files = Path.Combine(fixture.Root, "sessions"); Directory.CreateDirectory(files);
        foreach (var name in new[] { "probe-session.json", "probe-session.jsonl", "session_probe-session.json",
            "request_dump_probe-session_20261103_101112_123456.json", "other-session.json" })
            File.WriteAllText(Path.Combine(files, name), "fixture");
        var command = useAcpEntry ? fixture.Command with
        {
            Executable = Path.Combine(root, OperatingSystem.IsWindows() ? "hermes-acp.cmd" : "hermes-acp")
        } : fixture.Command;
        Seed("b", "probe-session", new("hermes", fixture.Root), command);
        var cleanup = service.CleanupCliProbeAsync("b");
        try
        {
            await WaitForFile(Path.Combine(root, "delete-started"));
            Execute(fixture.Database, "DELETE FROM sessions WHERE id='probe-session'");
        }
        finally { File.WriteAllText(Path.Combine(root, "delete-release"), "yes"); }
        var result = await cleanup;
        Assert.IsTrue(result.Items.Any(i => i.Kind == "session" && i.Status == "removed"));
        Assert.IsTrue(service.CliExtension("b")!.CreateProbe!.SessionCleaned);
        CollectionAssert.AreEquivalent(new[] { "other-session.json" }, Directory.GetFiles(files).Select(Path.GetFileName).ToArray());
        Assert.HasCount(4, result.Items.Where(i => i.Kind == "file" && i.Status == "removed"));
    }

    [TestMethod]
    [DataRow("INSERT INTO sessions(id,parent_session_id) VALUES('other','probe-session')")]
    [DataRow("INSERT INTO sessions(id,model_config) VALUES('other','{\"_delegate_from\":\"probe-session\"}')")]
    [DataRow("INSERT INTO state_meta VALUES('gateway.local_policy.v1:probe-session','{\"lineage\":[\"probe-session\",\"other\"]}')")]
    public async Task HermesRelatedSessionsPreventPotentialCascade(string relation)
    {
        var fixture = HermesFixture(); Execute(fixture.Database, relation);
        Seed("b", "probe-session", new("hermes", fixture.Root), fixture.Command);
        var result = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i => i.Kind == "session" && i.Status == "unverified"));
        Assert.IsFalse(File.Exists(Path.Combine(root, "delete-started")));
        Assert.AreEqual(1L, Scalar(fixture.Database, "SELECT COUNT(*) FROM sessions WHERE id='probe-session'"));
    }

    [TestMethod]
    public async Task HermesPreservesUnverifiedDumpBeforeCallingItsBroadDelete()
    {
        var fixture = HermesFixture(); var files = Path.Combine(fixture.Root, "sessions"); Directory.CreateDirectory(files);
        var unrelated = Path.Combine(files, "request_dump_probe-session_other_20261103_101112_123456.json");
        File.WriteAllText(unrelated, "other");
        Seed("b", "probe-session", new("hermes", fixture.Root), fixture.Command);
        var result = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(result.Items.Any(i => i.Kind == "session" && i.Status == "unverified"));
        Assert.IsTrue(File.Exists(unrelated)); Assert.IsFalse(File.Exists(Path.Combine(root, "delete-started")));
        Assert.AreEqual(1L, Scalar(fixture.Database, "SELECT COUNT(*) FROM sessions WHERE id='probe-session'"));
    }

    [TestMethod]
    public async Task HermesRetriesFilesWhenHostDeletedTheRowButLeftTheFileLocked()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file sharing is required for this fixture.");
        var fixture = HermesFixture(); var files = Path.Combine(fixture.Root, "sessions"); Directory.CreateDirectory(files);
        var path = Path.Combine(files, "probe-session.jsonl"); File.WriteAllText(path, "probe");
        Execute(fixture.Database, "DELETE FROM sessions WHERE id='probe-session'");
        Seed("b", "probe-session", new("hermes", fixture.Root), fixture.Command);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var first = await service.CleanupCliProbeAsync("b");
            Assert.IsTrue(first.Items.Any(i => i.Kind == "session" && i.Status == "absent"));
            Assert.IsTrue(first.Items.Any(i => i.Kind == "file" && i.Status == "failed" && i.Path == path));
            Assert.HasCount(1, service.CliExtension("b")!.ProbeArtifacts!);
        }
        var retry = await service.CleanupCliProbeAsync("b");
        Assert.IsTrue(retry.Items.Any(i => i.Kind == "file" && i.Status == "removed" && i.Path == path));
        Assert.IsFalse(File.Exists(path)); Assert.IsEmpty((await service.CleanupCliProbeAsync("b")).Items);
    }
}
