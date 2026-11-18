namespace TaskBaton.Core;

public sealed partial class RelayService
{
    public ConfigurationInventory InspectConfiguration(string path, string format) => ConfigurationFiles.Inspect(path, format);
    public List<AdapterSource> AdapterSources() => store.Read(s => s.Query("SELECT json FROM adapter_sources ORDER BY id,version",
        r => RelayJson.Read<AdapterSource>(r.GetString(0))));
    public List<MigrationPlan> Migrations() => store.Read(s => s.Query("SELECT json FROM migrations ORDER BY rowid DESC",
        r => RelayJson.Read<MigrationPlan>(r.GetString(0))));
    public MigrationPlan Migration(string id) => store.Read(s => ReadMigration(s, id));
    public List<MigrationEvent> MigrationAudit(string id) => store.Read(s => s.Query(
        "SELECT json FROM migration_events WHERE migration_id=$id ORDER BY seq", r => RelayJson.Read<MigrationEvent>(r.GetString(0)), ("$id", id)));

    public MigrationPlan PlanMigration(MigrationRequest request)
    {
        var source = request.Source;
        ValidateId(source.AdapterId);
        if (string.IsNullOrWhiteSpace(source.Version) || string.IsNullOrWhiteSpace(source.Revision) ||
            !Uri.TryCreate(source.Repository, UriKind.Absolute, out var repository) || repository.Scheme != "https" ||
            string.IsNullOrWhiteSpace(source.License) || source.Capabilities.Length == 0 || source.Capabilities.Any(string.IsNullOrWhiteSpace) ||
            request.Purpose is not ("install" or "disable_old"))
            throw new RelayException("migration_manifest_invalid", "需要来源、HTTPS 仓库、固定版本/修订、许可、能力映射及 install/disable_old 用途。");
        if (request.Purpose == "disable_old" && (string.IsNullOrEmpty(request.Before) || request.ValidatedMigrationId is null))
            throw new RelayException("migration_validation_required", "停用旧入口需要明确的旧片段和已验证的新迁移。");
        if (request.Purpose == "install" && string.IsNullOrWhiteSpace(request.After))
            throw new RelayException("migration_manifest_invalid", "新入口提案必须有明确的写入片段。");
        var id = Guid.NewGuid().ToString();
        var target = Path.GetFullPath(request.TargetPath);
        var exists = File.Exists(target);
        RequireRegularConfiguration(target);
        var original = exists ? File.ReadAllBytes(target) : [];
        var text = ConfigurationFiles.Decode(original);
        var oldEntries = exists ? ConfigurationFiles.Parse(text, request.Format) : [];
        var after = request.After.Replace("{{migration_id}}", id, StringComparison.Ordinal);
        var proposed = ReplaceFragment(text, request.Before, after);
        if (text == proposed) throw new RelayException("migration_no_change", "迁移没有产生配置变化。");
        var newEntries = ConfigurationFiles.Parse(proposed, request.Format);
        if (request.Purpose == "install" && oldEntries.Where(e => e.Enabled).Any(old => !newEntries.Any(next =>
            next.Enabled && next.Name == old.Name && next.Command == old.Command && next.Url == old.Url && next.Arguments.SequenceEqual(old.Arguments))))
            throw new RelayException("migration_disables_old_entry", "新入口须并行加入；停用、移除或替换已有 MCP 命令需单独的 disable_old 提案及新入口验证。", 5);
        request = request with { TargetPath = target, After = after };
        var directory = Path.Combine(Home, "migrations", id);
        return store.Write(s =>
        {
            if (request.Purpose == "disable_old")
            {
                var installed = RequireValidatedMigration(s, request.ValidatedMigrationId!);
                if (string.Equals(installed.Request.TargetPath, target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    RequireInstalledFragment(installed, proposed);
            }
            var registered = s.Json<AdapterSource>("SELECT json FROM adapter_sources WHERE id=$id AND version=$version",
                ("$id", source.AdapterId), ("$version", source.Version));
            if (registered is not null && RelayJson.Write(registered) != RelayJson.Write(source))
                throw new RelayException("adapter_source_conflict", "相同适配器版本已有不同来源记录，请使用准确的新版本。", 5);
            Directory.CreateDirectory(directory);
            var plan = new MigrationPlan(id, request, "planned", Now, exists, Path.Combine(directory, "original.bin"),
                Path.Combine(directory, "proposed.bin"), Path.Combine(directory, "backup.bin"), Path.Combine(directory, "preview.md"));
            File.WriteAllBytes(plan.OriginalPath, original);
            File.WriteAllBytes(plan.ProposedPath, ConfigurationFiles.Encode(proposed, original));
            File.WriteAllText(plan.PreviewPath, $"""
                # 配置迁移 {id}

                目标：{target}
                用途：{request.Purpose}
                适配器：{source.AdapterId} {source.Version}
                来源：{source.Repository} @ {source.Revision}
                声明许可：{source.License}
                能力：{string.Join(", ", source.Capabilities)}
                依赖：{string.Join(", ", source.Dependencies)}
                限制：{string.Join("；", source.Limitations)}
                原文件存在：{exists}
                插入规则：{(request.Before.Length == 0 ? "追加到文件末尾" : "替换唯一精确片段")}
                原始快照：{plan.OriginalPath}
                完整候选：{plan.ProposedPath}
                应用备份：{plan.BackupPath}

                ## 移除的片段

                ````text
                {request.Before}
                ````

                ## 写入的片段

                ````text
                {after}
                ````
                """);
            s.Execute("INSERT OR IGNORE INTO adapter_sources VALUES($id,$version,$json)",
                ("$id", source.AdapterId), ("$version", source.Version), ("$json", RelayJson.Write(source)));
            s.Execute("INSERT INTO migrations VALUES($id,$json)", ("$id", id), ("$json", RelayJson.Write(plan)));
            MigrationEvent(s, plan, "migration.planned", plan.PreviewPath);
            return plan;
        });
    }

    public MigrationPlan ApplyMigration(string id, bool approved, string reason)
    {
        RequireMigrationApproval(approved, reason);
        var plan = store.Write(s =>
        {
            var current = ReadMigration(s, id);
            if (current.State is "applied" or "validated") return current;
            if (current.State != "planned") throw new RelayException("migration_state_conflict", "请检查迁移状态，必要时先 migration recover。", 5);
            if (current.Request.Purpose == "disable_old") RequireValidatedMigration(s, current.Request.ValidatedMigrationId!);
            RequireTarget(current, original: true);
            File.Copy(current.OriginalPath, current.BackupPath, overwrite: true);
            var claimed = current with { State = "applying", Reason = reason };
            SaveMigration(s, claimed); MigrationEvent(s, claimed, "migration.approved", reason);
            return claimed;
        });
        if (plan.State != "applying") return plan;
        RequireTarget(plan, original: true);
        WriteTarget(plan.Request.TargetPath, File.ReadAllBytes(plan.ProposedPath), overwrite: plan.OriginalExists);
        return FinishMigration(plan, "applied", "migration.applied");
    }

    public MigrationPlan RollbackMigration(string id, bool approved, string reason)
    {
        RequireMigrationApproval(approved, reason);
        var plan = store.Write(s =>
        {
            var current = ReadMigration(s, id);
            if (current.State == "rolled_back") return current;
            if (current.State is not ("applied" or "validated")) throw new RelayException("migration_state_conflict", "只有已应用的迁移可以回滚。", 5);
            var dependents = s.Query("SELECT json FROM migrations", r => RelayJson.Read<MigrationPlan>(r.GetString(0)))
                .Where(p => p.Request.Purpose == "disable_old" && p.Request.ValidatedMigrationId == id && p.State is not ("planned" or "rolled_back"))
                .Select(p => p.MigrationId).ToArray();
            if (dependents.Length > 0)
                throw new RelayException("migration_rollback_order", "请先恢复已停用的旧入口迁移：" + string.Join(", ", dependents), 5);
            RequireTarget(current, original: false);
            var claimed = current with { State = "rolling_back", Reason = reason };
            SaveMigration(s, claimed); MigrationEvent(s, claimed, "migration.rollback_approved", reason);
            return claimed;
        });
        if (plan.State == "rolled_back") return plan;
        RequireTarget(plan, original: false);
        if (plan.OriginalExists) WriteTarget(plan.Request.TargetPath, File.ReadAllBytes(plan.BackupPath), overwrite: true);
        else File.Delete(plan.Request.TargetPath);
        return FinishMigration(plan, "rolled_back", "migration.rolled_back");
    }

    public MigrationPlan RecoverMigration(string id) => store.Write(s =>
    {
        var plan = ReadMigration(s, id);
        if (plan.State is not ("applying" or "rolling_back")) return plan;
        var state = MatchesTarget(plan, original: true)
            ? plan.State == "applying" ? "planned" : "rolled_back"
            : MatchesTarget(plan, original: false) ? plan.ProofTaskId is null ? "applied" : "validated" : "unknown";
        var recovered = plan with { State = state };
        SaveMigration(s, recovered); MigrationEvent(s, recovered, "migration.recovered", state);
        return recovered;
    });

    public MigrationPlan ValidateMigration(string id, string proofTaskId) => store.Write(s =>
    {
        var plan = ReadMigration(s, id);
        if (plan.State is not ("applied" or "validated") || plan.Request.Purpose != "install")
            throw new RelayException("migration_state_conflict", "只能验证已应用的新入口迁移。", 5);
        RequireInstalledFragment(plan);
        var task = s.Task(proofTaskId);
        var events = s.Query("SELECT seq,json FROM events WHERE task_id=$id", r => RelayJson.Read<AuditEvent>(r.GetString(1)) with { Sequence = r.GetInt64(0) }, ("$id", proofTaskId))
            .Where(e => e.AttemptId == task.AttemptId).ToArray();
        if (task.State != TaskState.Succeeded ||
            !events.Any(e => e.Event == "mcp.task.dispatch" && e.Detail == id && events.Any(done => done.Event == "task.completed" && done.Sequence > e.Sequence)) ||
            !events.Any(e => e.Event is "session.resume_submitted" or "session.resume_completed"))
            throw new RelayException("migration_proof_incomplete", "验证需要通过该 route-id 的 MCP 派发、显式完成及原会话继续证据。", 5);
        if (plan.State == "validated" && plan.ProofTaskId == proofTaskId) return plan;
        var validated = plan with { State = "validated", ProofTaskId = proofTaskId };
        SaveMigration(s, validated); MigrationEvent(s, validated, "migration.validated", proofTaskId);
        return validated;
    });

    public void RecordMcpOperation(string taskId, string agentId, string operation, string routeId) => store.Write(s =>
    {
        var task = s.Task(taskId);
        RequireMcpAccess(task, agentId);
        s.Event(task, "mcp.task." + operation, agentId, Now, routeId);
        return true;
    });
    public void RequireMcpAgent(string agentId) => store.Read(s => { RequirePaths(Agent(s, agentId)); return true; });
    public TaskView McpStatus(string taskId, string agentId)
    {
        var view = Status(taskId); RequireMcpAccess(view.Task, agentId); return view;
    }
    private static void RequireMcpAccess(TaskRecord task, string agentId)
    {
        if (task.RequesterId != agentId && task.RecipientId != agentId)
            throw new RelayException("task_access_denied", "此 MCP Agent 不是任务参与方。", 3);
    }
    private MigrationPlan RequireValidatedMigration(StoreSession s, string id)
    {
        var plan = ReadMigration(s, id);
        if (plan.State != "validated" || plan.Request.Purpose != "install")
            throw new RelayException("migration_validation_required", "新入口尚未通过派发、完成和继续验证。", 5);
        RequireInstalledFragment(plan);
        return plan;
    }
    private static void RequireInstalledFragment(MigrationPlan plan, string? content = null)
    {
        var current = content ?? ConfigurationFiles.Decode(File.ReadAllBytes(plan.Request.TargetPath));
        var fragment = plan.Request.After;
        var index = current.IndexOf(fragment, StringComparison.Ordinal);
        if (index < 0 || current.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal) >= 0)
            throw new RelayException("migration_target_changed", "新入口片段发生变化，请重新预览和验证。", 5);
        ConfigurationFiles.Parse(current, plan.Request.Format);
    }
    private static string ReplaceFragment(string original, string before, string after)
    {
        if (before.Length == 0) return original + after;
        var index = original.IndexOf(before, StringComparison.Ordinal);
        if (index < 0 || original.IndexOf(before, index + before.Length, StringComparison.Ordinal) >= 0)
            throw new RelayException("migration_fragment_ambiguous", "原片段必须在配置中精确出现一次。", 5);
        return original[..index] + after + original[(index + before.Length)..];
    }
    private static bool MatchesTarget(MigrationPlan plan, bool original)
    {
        var expectedExists = !original || plan.OriginalExists;
        if (File.Exists(plan.Request.TargetPath) != expectedExists) return false;
        return !expectedExists || File.ReadAllBytes(plan.Request.TargetPath).AsSpan().SequenceEqual(File.ReadAllBytes(original ? plan.OriginalPath : plan.ProposedPath));
    }
    private static void RequireTarget(MigrationPlan plan, bool original)
    {
        RequireRegularConfiguration(plan.Request.TargetPath);
        if (!MatchesTarget(plan, original)) throw new RelayException("migration_target_changed",
            $"目标配置已变化，未覆盖。请对比当前文件与 {plan.OriginalPath}、{plan.ProposedPath}，重新预览。", 5);
    }
    private static void RequireRegularConfiguration(string path)
    {
        // Replacing a file symlink would destroy the link instead of updating its destination.
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new RelayException("linked_configuration", "配置路径是文件链接，请指定其真实文件并重新预览；不会替换链接。", 3);
    }
    private static void WriteTarget(string path, byte[] content, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".relay-" + Guid.NewGuid() + ".tmp";
        try { File.WriteAllBytes(temporary, content); File.Move(temporary, path, overwrite); }
        finally { File.Delete(temporary); }
    }
    private MigrationPlan FinishMigration(MigrationPlan plan, string state, string name) => store.Write(s =>
    {
        var current = ReadMigration(s, plan.MigrationId);
        var finished = current with { State = state };
        SaveMigration(s, finished); MigrationEvent(s, finished, name, finished.Request.TargetPath);
        return finished;
    });
    private static void RequireMigrationApproval(bool approved, string reason)
    {
        if (!approved) throw new RelayException("approval_required", "请审阅 migration show 的精确预览，再使用 --approve --reason 批准该操作。", 4);
        RequireReason(reason);
    }
    private static MigrationPlan ReadMigration(StoreSession s, string id) => s.Json<MigrationPlan>("SELECT json FROM migrations WHERE id=$id", ("$id", id))
        ?? throw new RelayException("migration_not_found", "未找到迁移记录。", 3);
    private static void SaveMigration(StoreSession s, MigrationPlan plan) => s.Execute("UPDATE migrations SET json=$json WHERE id=$id",
        ("$id", plan.MigrationId), ("$json", RelayJson.Write(plan)));
    private void MigrationEvent(StoreSession s, MigrationPlan plan, string name, string detail) => s.Execute(
        "INSERT INTO migration_events(migration_id,json) VALUES($id,$json)", ("$id", plan.MigrationId),
        ("$json", RelayJson.Write(new MigrationEvent(plan.MigrationId, name, Now, detail))));
}
