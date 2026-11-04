using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace AgentRelay.Core;

public sealed record RelayRelocationRequest(string Source, string Target);

public sealed partial class RelayService
{
    internal const string RelocationPendingFile = "relay.relocation.pending.json";
    internal const string RelocationMovedFile = "relay.relocation.moved.json";
    internal const string RelocationIncompleteFile = "relay.relocation.incomplete.json";

    public RelayService VerifyRelocation()
    {
        var pending = Path.Combine(Home, RelocationPendingFile);
        if (!File.Exists(pending))
            throw new RelayException("relocation_no_pending", $"没有待继续的目录迁移。迁移校验不会更换中转站；请先选择更换中转站文件夹。当前目录：{Home}");
        return Relocate(RelayJson.Read<RelayRelocationRequest>(File.ReadAllText(pending)).Target);
    }

    public RelayService Relocate(string target)
    {
        var request = ValidateRelocationTarget(target);
        target = request.Target;
        var source = request.Source;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var incomingMarker = Path.Combine(target, RelocationIncompleteFile);
        var switched = false;
        try
        {
            var files = new List<RelocationFileSnapshot>();
            var directories = new List<string>();
            var previousPlans = OutstandingRelocationCleanupPlans(source);
            using (var sourceConnection = store.Open())
            using (var sourceTransaction = sourceConnection.BeginTransaction())
            {
                var session = new StoreSession(sourceConnection, sourceTransaction);
                var tasks = session.Query("SELECT json FROM tasks", r => RelayJson.Read<TaskRecord>(r.GetString(0)));
                var active = tasks.FirstOrDefault(task => !Terminal(task.State));
                if (active is not null)
                    throw new RelayException("relocation_active_tasks", $"任务 {active.TaskId} 尚未结束，请完成或取消交接后再迁移。");
                var sending = tasks.Where(task => task.IsFileHandoff && task.CliFileStatus == "submitting").Any(task =>
                {
                    var submission = session.Query("SELECT json FROM events WHERE task_id=$task ORDER BY seq DESC",
                        r => RelayJson.Read<AuditEvent>(r.GetString(0)), ("$task", task.TaskId))
                        .FirstOrDefault(entry => entry.AttemptId == task.AttemptId && entry.Event == "file.cli_submitting");
                    return task.DeadlineAt > Now || submission is not null && submission.RecordedAt.Add(MinimumCliTurn) > Now;
                });
                var working = session.Query("SELECT json FROM executions", r => RelayJson.Read<ExecutionRecord>(r.GetString(0)))
                    .Any(entry => entry.State is "scheduled" or "receiving" or "running");
                var continuing = session.Query("SELECT json FROM continuations", r => RelayJson.Read<ContinuationRecord>(r.GetString(0)))
                    .Any(entry => entry.State == "in_flight" || entry.DesktopState == "in_flight");
                if (sending || working || continuing)
                    throw new RelayException("relocation_active_tasks", "仍有执行或通知正在进行，请等待结束后再迁移。");

                // Filesystem failures retain the source and a retry request; the destination cannot be opened until complete.
                WriteRelocationFile(Path.Combine(source, RelocationPendingFile), RelayJson.Write(request));
                Directory.CreateDirectory(target);
                File.WriteAllText(incomingMarker, RelayJson.Write(request));
                CopyRelocationDirectory(source, target, source, files, directories, true);
                using var snapshotSource = store.Open(true);
                using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(target, "relay.db"), Pooling = false
                }.ToString());
                destination.Open();
                snapshotSource.BackupDatabase(destination);
                using var transaction = destination.BeginTransaction();
                var destinationSession = new StoreSession(destination, transaction);
                RewriteRelocatedPaths(destinationSession, source, target, comparison);
                RewriteRelocatedContexts(destinationSession, target, source, comparison);
                transaction.Commit();
                using var check = destination.CreateCommand();
                check.CommandText = "PRAGMA quick_check";
                if ((string?)check.ExecuteScalar() != "ok")
                    throw new RelayException("relocation_database_invalid", "复制后的数据库校验未通过，原中转站仍可使用。");
                // SQLite holds byte-range locks on its shared-memory file until the connection closes.
                foreach (var name in new[] { "relay.db", "relay.db-wal" })
                    if (File.Exists(Path.Combine(source, name))) files.Add(SnapshotRelocationFile(source, Path.Combine(source, name)));
                AddRelocationCleanupPlan(target, request, files, directories, previousPlans);
                File.Delete(incomingMarker);
                // Keep the write lock until the moved marker stops new source writes.
                File.Move(Path.Combine(source, RelocationPendingFile), Path.Combine(source, RelocationMovedFile));
                switched = true;
            }
            // Closing the last SQLite connection can checkpoint or remove WAL files.
            FinalizeRelocationDatabaseSnapshot(target, request);
            return new RelayService(target, clock, adapters.Values);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException)
        {
            if (switched)
                throw new RelayException("relocation_snapshot_failed", $"中转站已迁移到 {target}，但旧账本清理记录尚未更新；未确认未变的文件会保留。{error.Message}");
            if (Directory.Exists(target) && !File.Exists(incomingMarker))
                File.WriteAllText(incomingMarker, RelayJson.Write(request));
            throw new RelayException("relocation_failed", $"中转站迁移未完成，原目录仍保留。请解除文件占用或修复访问权限，再运行迁移校验。{error.Message}");
        }
    }

    private RelayRelocationRequest ValidateRelocationTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new RelayException("relocation_target_invalid", "请选择新的中转站目录。");
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var source = Path.TrimEndingDirectorySeparator(Home);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(source, target, comparison))
            throw new RelayException("relocation_same_directory", "选择的目录就是当前中转站，未执行迁移。请为更换中转站选择另一个空目录。");
        if (string.Equals(source, Path.GetPathRoot(source), comparison) || string.Equals(target, Path.GetPathRoot(target), comparison))
            throw new RelayException("relocation_target_invalid", "中转站不能迁移到磁盘根目录，也不能从磁盘根目录迁移。");
        if (Within(source, target, comparison) || Within(target, source, comparison))
            throw new RelayException("relocation_target_invalid", "新旧中转站目录不能相互包含。");
        if (OutstandingRelocationCleanupPlans(source).Any(plan =>
            string.Equals(target, plan.Request.Source, comparison) || Within(target, plan.Request.Source, comparison) ||
            Within(plan.Request.Source, target, comparison)))
            throw new RelayException("relocation_target_invalid", "新目录不能与待清理的旧中转站相同或相互包含。");
        var request = new RelayRelocationRequest(source, target);
        var incomingMarker = Path.Combine(target, RelocationIncompleteFile);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            if (!File.Exists(incomingMarker) || RelayJson.Read<RelayRelocationRequest>(File.ReadAllText(incomingMarker)) != request)
                throw new RelayException("relocation_target_not_empty", "目标目录已有文件，请选择空目录；待完成迁移可使用迁移校验重试。");
        }
        return request;
    }

    private static bool Within(string path, string root, StringComparison comparison) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, comparison);

    private static void CopyRelocationDirectory(string source, string target, string root,
        List<RelocationFileSnapshot> files, List<string> directories, bool isRoot)
    {
        // A retry mirrors the current source, including files removed since the failed attempt.
        foreach (var entry in Directory.EnumerateFileSystemEntries(target))
        {
            var name = Path.GetFileName(entry);
            if (isRoot && (name is "relay.db" or "relay.db-wal" or "relay.db-shm" or RelocationIncompleteFile)) continue;
            if (Path.Exists(Path.Combine(source, name))) continue;
            if (Directory.Exists(entry)) Directory.Delete(entry, true);
            else File.Delete(entry);
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (isRoot && (name is "relay.db" or "relay.db-wal" or "relay.db-shm" or
                RelocationPendingFile or RelocationMovedFile or RelocationIncompleteFile)) continue;
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new RelayException("relocation_linked_path", $"中转站包含链接路径，无法按普通文件迁移：{entry}");
            var output = Path.Combine(target, name);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(output);
                directories.Add(Path.GetRelativePath(root, entry));
                CopyRelocationDirectory(entry, output, root, files, directories, false);
            }
            else
            {
                File.Copy(entry, output, true);
                if (new FileInfo(entry).Length != new FileInfo(output).Length)
                    throw new IOException($"复制文件长度不一致：{entry}");
                var snapshot = SnapshotRelocationFile(root, entry);
                if (snapshot.ContentHash != RelocationFileHash(output))
                    throw new IOException($"复制文件内容不一致：{entry}");
                files.Add(snapshot);
            }
        }
    }

    private static void RewriteRelocatedPaths(StoreSession session, string source, string target, StringComparison comparison)
    {
        string Move(string path) => string.Equals(path, source, comparison) ? target :
            Within(path, source, comparison) ? target + path[source.Length..] : path;
        TaskRecord MoveTask(TaskRecord task)
        {
            task.RequestPath = Move(task.RequestPath);
            task.ResultPath = Move(task.ResultPath);
            task.ManualCurrentFile = task.ManualCurrentFile is null ? null : Move(task.ManualCurrentFile);
            task.ManualSourceFile = task.ManualSourceFile is null ? null : Move(task.ManualSourceFile);
            task.Images = task.Images?.Select(image => image with { Path = Move(image.Path) }).ToArray();
            task.CliNewSessionCwd = task.CliNewSessionCwd is null ? null : Move(task.CliNewSessionCwd);
            if (task.OriginSession is { } origin) task.OriginSession = origin with { Cwd = Move(origin.Cwd) };
            if (task.ResumeAdapter is { } resumer) task.ResumeAdapter = resumer with
            {
                Executable = Move(resumer.Executable), ConfigPath = Move(resumer.ConfigPath)
            };
            if (task.Collaboration is { } collaboration)
            {
                collaboration.SubmissionPath = collaboration.SubmissionPath is null ? null : Move(collaboration.SubmissionPath);
                collaboration.ApprovedSubmissionPath = collaboration.ApprovedSubmissionPath is null ? null : Move(collaboration.ApprovedSubmissionPath);
                collaboration.History = collaboration.History.Select(step => step with
                {
                    ResultPath = step.ResultPath is null ? null : Move(step.ResultPath)
                }).ToList();
            }
            return task;
        }
        void Rewrite<T>(string table, string column, Func<T, T> transform) where T : class
        {
            var records = session.Query($"SELECT rowid,{column} FROM {table}", r => (Row: r.GetInt64(0), Value: RelayJson.Read<T>(r.GetString(1))));
            foreach (var record in records)
                session.Execute($"UPDATE {table} SET {column}=$json WHERE rowid=$row", ("$json", RelayJson.Write(transform(record.Value))), ("$row", record.Row));
        }
        Rewrite<TaskRecord>("tasks", "json", MoveTask);
        Rewrite<AgentRecord>("agents", "json", value => value with
        {
            ConfigPath = Move(value.ConfigPath), CliPath = value.CliPath is null ? null : Move(value.CliPath),
            InstructionPaths = value.InstructionPaths.Select(Move).ToArray()
        });
        Rewrite<ExecutorRecord>("executors", "json", value => value with
        {
            Executable = Move(value.Executable), ConfigDirectory = Move(value.ConfigDirectory)
        });
        Rewrite<ResumerRecord>("resumers", "json", value => value with
        {
            Executable = Move(value.Executable), ConfigPath = Move(value.ConfigPath)
        });
        Rewrite<AdapterRecord>("adapters", "json", value => value with { Executable = Move(value.Executable) });
        foreach (var executable in session.Query("SELECT executable FROM desktop WHERE id=1", r => r.GetString(0)))
            session.Execute("UPDATE desktop SET executable=$path WHERE id=1", ("$path", Move(executable)));
        CliCommand MoveCommand(CliCommand command)
        {
            var arguments = command.Arguments.ToArray();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (i > 0 && arguments[i - 1] == "--home") arguments[i] = Move(arguments[i]);
                else if (arguments[i].StartsWith("--home=", StringComparison.Ordinal))
                    arguments[i] = "--home=" + Move(arguments[i][7..]);
            }
            var environment = command.Environment is null ? null : new Dictionary<string, string>(command.Environment);
            if (environment is not null && environment.TryGetValue("RELAY_HOME", out var home)) environment["RELAY_HOME"] = Move(home);
            return command with
            {
                Executable = Move(command.Executable), Arguments = arguments, Environment = environment,
                WorkingDirectory = command.WorkingDirectory is null ? null : Move(command.WorkingDirectory)
            };
        }
        Rewrite<CliExtensionRecord>("cli_extensions", "json", value => value with
        {
            Recipe = value.Recipe with { Create = MoveCommand(value.Recipe.Create),
                Resume = value.Recipe.Resume is null ? null : MoveCommand(value.Recipe.Resume) },
            ProbeArtifacts = value.ProbeArtifacts?.Select(artifact => artifact with
            {
                Source = artifact.Source is null ? null : artifact.Source with { StorageRoot = Move(artifact.Source.StorageRoot) },
                SessionDirectory = artifact.SessionDirectory is null ? null : Move(artifact.SessionDirectory),
                CreateCommand = artifact.CreateCommand is null ? null : MoveCommand(artifact.CreateCommand)
            }).ToArray()
        });
        Rewrite<TaskSessionBinding>("task_session_bindings", "json", value => value with
        {
            Cwd = value.Cwd is null ? null : Move(value.Cwd),
            SessionPath = value.SessionPath is null ? null : Move(value.SessionPath)
        });
        Rewrite<SessionDiscoverySource>("session_discovery_sources", "json", value => value with { StorageRoot = Move(value.StorageRoot) });
        Rewrite<SessionDiscoveryResult>("session_discovery_cache", "json", value => value with
        {
            Sessions = value.Sessions.Select(item => item with
            {
                Cwd = item.Cwd is null ? null : Move(item.Cwd),
                SessionPath = item.SessionPath is null ? null : Move(item.SessionPath)
            }).ToArray()
        });
        Rewrite<ArtifactRecord>("artifacts", "json", value => value with { Path = Move(value.Path) });
        foreach (var artifact in session.Query("SELECT id,json FROM artifacts", r => (Id: r.GetString(0), Value: RelayJson.Read<ArtifactRecord>(r.GetString(1)))))
            session.Execute("UPDATE artifacts SET path=$path WHERE id=$id", ("$path", artifact.Value.Path), ("$id", artifact.Id));
        Rewrite<ContinuationRecord>("continuations", "json", value => value with { OutputPath = value.OutputPath is null ? null : Move(value.OutputPath) });
        Rewrite<MigrationPlan>("migrations", "json", value => value with
        {
            OriginalPath = Move(value.OriginalPath), ProposedPath = Move(value.ProposedPath),
            BackupPath = Move(value.BackupPath), PreviewPath = Move(value.PreviewPath)
        });
        // Idempotency inputs and user prose remain byte-for-byte unchanged; only returned task paths move.
        Rewrite<TaskView>("callbacks", "result_json", value => value with { Task = MoveTask(value.Task) });
    }

    private static void RewriteRelocatedContexts(StoreSession session, string target, string source, StringComparison comparison)
    {
        var tasks = session.Query("SELECT json FROM tasks", r => RelayJson.Read<TaskRecord>(r.GetString(0)))
            .Where(task => task.IsFileHandoff);
        foreach (var task in tasks)
        {
            var taskDirectory = Path.Combine(target, "workspace", task.TaskId);
            if (!Directory.Exists(taskDirectory)) continue;
            foreach (var attempt in Directory.EnumerateDirectories(taskDirectory))
            {
                var contextPath = Path.Combine(attempt, ".relay", "context.json");
                if (!File.Exists(contextPath)) continue;
                var context = JsonNode.Parse(File.ReadAllText(contextPath))!.AsObject();
                void MoveField(JsonObject? value, string name)
                {
                    if (value?[name] is not JsonValue field || !field.TryGetValue<string>(out var path)) return;
                    if (string.Equals(path, source, comparison)) value[name] = target;
                    else if (Within(path, source, comparison)) value[name] = target + path[source.Length..];
                }
                foreach (var name in new[] { "current_file", "original_request_file", "reply_file", "receipt_file" }) MoveField(context, name);
                if (context["images"] is JsonArray images)
                    foreach (var image in images) MoveField(image?.AsObject(), "path");
                MoveField(context["collaboration"]?.AsObject(), "submission_file");
                MoveField(context["receipt_template"]?.AsObject(), "result_file");
                File.WriteAllText(contextPath, context.ToJsonString(RelayJson.Options));
            }
        }
    }
}
