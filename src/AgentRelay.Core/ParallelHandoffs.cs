namespace AgentRelay.Core;

public sealed record ParallelWork(string AgentId, string Instructions);
public sealed record ParallelHandoffDraft(string Title, string From, string MergeAgentId, string ProjectDirectory,
    string[] InputFiles, ParallelWork[] Work, string MergeInstructions);
public sealed record ParallelFileChange(string Path, string Kind, string? Snapshot);
public sealed record ParallelMember(string TaskId, string AgentId, string Instructions, TaskState? State, string? Problem, string? Detail,
    string WorkingDirectory, string? Report, ParallelFileChange[] Changes)
{
    public bool HasResult => Report is not null && Problem is null;
}
public sealed record ParallelReview(string GroupId, string Title, string MergeAgentId, ParallelMember[] Workers,
    string[] SharedConflicts, string[] ProjectConflicts, bool CanMerge, bool Partial, ParallelMember? Merge);

public sealed partial class RelayService
{
    private sealed record ParallelSubmission(string Id, ParallelChange[] Changes);
    private sealed record ParallelChange(string Path, string Kind);
    private sealed record ParallelPart(string TaskId, string AttemptId, string AgentId, string Instructions,
        ParallelSubmission? Submission = null);
    private sealed record ParallelGroup(string Id, string Title, string From, string MergeAgentId, string ProjectDirectory,
        string[] InputFiles, string MergeInstructions, ParallelPart[] Workers, ParallelPart? Merge = null);

    public ParallelReview CreateParallelHandoff(ParallelHandoffDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.MergeInstructions) ||
            string.IsNullOrWhiteSpace(draft.From) || string.IsNullOrWhiteSpace(draft.MergeAgentId) ||
            draft.Work is not { Length: >= 2 } || draft.Work.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.AgentId) || string.IsNullOrWhiteSpace(item.Instructions)))
            throw new RelayException("parallel_work_required", "请填写标题、汇合要求以及至少两份分工。");
        if (draft.InputFiles is null || draft.InputFiles.Any(string.IsNullOrWhiteSpace))
            throw new RelayException("parallel_relative_path", "请提供共享文件路径清单，没有共享文件时使用空清单。");
        if (draft.Work.Any(item => item.AgentId == draft.From) || draft.MergeAgentId == draft.From)
            throw new RelayException("parallel_same_agent", "发起方与分工或汇合的接收方必须不同。");
        if (string.IsNullOrWhiteSpace(draft.ProjectDirectory) || !Path.IsPathFullyQualified(draft.ProjectDirectory) ||
            !Directory.Exists(draft.ProjectDirectory))
            throw new RelayException("parallel_project_required", "请选择存在的项目目录。");
        var project = Path.GetFullPath(draft.ProjectDirectory);
        if (SameWorkspacePath(project, Home) || UnderWorkspacePath(project, Home))
            throw new RelayException("parallel_project_in_station", "请在中转站之外选择项目目录。");
        var inputs = draft.InputFiles.Select(NormalizeParallelPath).Distinct(WorkspacePaths).Order(WorkspacePaths).ToArray();
        var id = Guid.NewGuid().ToString();
        var groupRoot = ParallelGroupRoot(id);
        var created = new List<string> { groupRoot };
        try
        {
            return store.Write(s =>
            {
                Agent(s, draft.From); Agent(s, draft.MergeAgentId);
                foreach (var worker in draft.Work) Agent(s, worker.AgentId);
                foreach (var relative in inputs)
                {
                    var source = Path.Combine(project, relative);
                    if (!File.Exists(source) || UnderWorkspacePath(source, Home) || HasRelocationLink(project, source) ||
                        (File.GetAttributes(project) & FileAttributes.ReparsePoint) != 0)
                        throw new RelayException("parallel_input_unavailable", "共享输入必须是项目内可读取的普通文件：" + relative);
                    CopyParallelFile(source, Path.Combine(groupRoot, "base", relative));
                }
                var workers = draft.Work.Select(item => CreateParallelPart(s, id, draft.From, item.AgentId,
                    draft.Title.Trim(), item.Instructions.Trim(), inputs, created)).ToArray();
                var group = new ParallelGroup(id, draft.Title.Trim(), draft.From, draft.MergeAgentId, project,
                    inputs, draft.MergeInstructions.Trim(), workers);
                s.Execute("INSERT INTO parallel_handoffs VALUES($id,$json)", ("$id", id), ("$json", RelayJson.Write(group)));
                return ReviewParallel(s, group);
            });
        }
        catch
        {
            foreach (var directory in created.Where(Directory.Exists)) Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public ParallelReview ReviewParallelHandoff(string groupId) => store.Read(s => ReviewParallel(s, ReadParallelGroup(s, groupId)));

    public TaskView CreateParallelMerge(ParallelReview reviewed, bool allowPartial, bool reviewConflicts)
    {
        var created = new List<string>();
        try
        {
            return store.Write(s =>
            {
                var group = ReadParallelGroup(s, reviewed.GroupId);
                if (group.Merge is { } existing) return ManualView(s.Task(existing.TaskId));
                var current = ReviewParallel(s, group);
                if (RelayJson.Write(current) != RelayJson.Write(reviewed))
                    throw new RelayException("parallel_review_changed", "分工结果或冲突已变化，请刷新后重新确认。", 5);
                if (!current.CanMerge)
                    throw new RelayException("parallel_not_ready", "请等待所有分工结束，且至少要有一份可用成果。", 5);
                if (current.Partial && !allowPartial)
                    throw new RelayException("parallel_partial_approval", "有分工没有可用成果，请明确确认只汇合成功成果。", 4);
                if ((current.SharedConflicts.Length != 0 || current.ProjectConflicts.Length != 0) && !reviewConflicts)
                    throw new RelayException("parallel_conflict_approval", "请先查看冲突，再确认把各个版本交给汇合 Agent 处理。", 4);
                Agent(s, group.MergeAgentId);
                var part = CreateParallelPart(s, group.Id, group.From, group.MergeAgentId, group.Title,
                    group.MergeInstructions, group.InputFiles, created, merge: true);
                var task = s.Task(part.TaskId);
                var folder = Path.GetDirectoryName(ParallelMergeManifest(task))!;
                var members = current.Workers.Select(worker =>
                {
                    if (!worker.HasResult) return new { worker.TaskId, worker.AgentId, worker.Instructions,
                        worker.State, worker.Problem, worker.Detail, Report = (string?)null, Changes = Array.Empty<ParallelFileChange>() };
                    var report = Path.Combine(worker.TaskId, "report.md");
                    CopyParallelFile(workspace.RequireManaged(worker.Report!), Path.Combine(folder, report));
                    var changes = worker.Changes.Select(change =>
                    {
                        if (change.Snapshot is null) return change;
                        var file = Path.Combine(worker.TaskId, "files", change.Path);
                        CopyParallelFile(workspace.RequireManaged(change.Snapshot), Path.Combine(folder, file));
                        return change with { Snapshot = file };
                    }).ToArray();
                    return new { worker.TaskId, worker.AgentId, worker.Instructions,
                        worker.State, worker.Problem, worker.Detail, Report = (string?)report, Changes = changes };
                }).ToArray();
                var projectVersions = current.ProjectConflicts.Select(relative =>
                {
                    var source = Path.Combine(group.ProjectDirectory, relative);
                    var copy = File.Exists(source) && (File.GetAttributes(group.ProjectDirectory) & FileAttributes.ReparsePoint) == 0 &&
                        !HasRelocationLink(group.ProjectDirectory, source) ? Path.Combine("project-current", relative) : null;
                    if (copy is not null) CopyParallelFile(source, Path.Combine(folder, copy));
                    return new { Path = relative, Snapshot = copy };
                }).ToArray();
                workspace.WriteNew(ParallelMergeManifest(task), RelayJson.Write(new
                {
                    group.Id, group.Title, group.MergeInstructions, current.Partial, current.SharedConflicts,
                    current.ProjectConflicts, ProjectVersions = projectVersions, Workers = members
                }));
                SaveParallelGroup(s, group with { Merge = part });
                s.Event(task, "parallel.merge_created", group.From, Now, group.Id);
                return ManualView(task);
            });
        }
        catch
        {
            foreach (var directory in created.Where(Directory.Exists)) Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    private ParallelPart CreateParallelPart(StoreSession s, string groupId, string from, string to,
        string title, string instructions, string[] inputs, List<string> created, bool merge = false)
    {
        var now = Now;
        var settings = s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!;
        var task = new TaskRecord
        {
            TaskId = Guid.NewGuid().ToString(), AttemptId = Guid.NewGuid().ToString(), RequesterId = from, RecipientId = to,
            Mode = "manual", AdapterId = "manual-file", State = TaskState.AwaitingManual, IsFileHandoff = true,
            AwaitingAgentId = to, CreatedAt = now, DeadlineAt = now.AddMinutes(settings.TaskTimeoutMinutes),
            RequestPath = "", ResultPath = "", ManualNextAction = instructions, ParallelGroupId = groupId, IsParallelMerge = merge
        };
        var taskRoot = workspace.RequireManaged(Path.Combine(workspace.Root, task.TaskId));
        created.Add(taskRoot);
        var working = ParallelWorkingDirectory(task);
        Directory.CreateDirectory(working);
        foreach (var relative in inputs)
            CopyParallelFile(workspace.RequireManaged(Path.Combine(ParallelGroupRoot(groupId), "base", relative)),
                Path.Combine(working, relative));
        task.CliNewSessionCwd = working;
        var artifact = workspace.Publish(task, "request", title + "\n\n" + instructions, now, settings.ArtifactTtlDays);
        AddArtifact(s, artifact);
        task.RequestPath = task.ManualCurrentFile = artifact.Path;
        task.ResultPath = TurnReplyPath(task);
        InsertFileHandoff(s, task);
        return new(task.TaskId, task.AttemptId, to, instructions);
    }

    private void AcceptParallelReceipt(StoreSession s, TaskRecord task, ManualReceipt receipt)
    {
        if (receipt.NeedsReply != false || receipt.ResultFile is null || !SameWorkspacePath(receipt.ResultFile, TurnReplyPath(task)))
            throw new RelayException("parallel_result_required", "请将分工总结写入本轮 reply_file，回执填 result_file，并设置 needs_reply=false。");
        ManualTransfers.RequireFile(receipt.ResultFile);
        var group = ReadParallelGroup(s, task.ParallelGroupId!);
        var working = ParallelWorkingDirectory(task);
        var files = ParallelWorkFiles(working).ToHashSet(WorkspacePaths);
        var inputs = group.InputFiles.ToHashSet(WorkspacePaths);
        var baseline = Path.Combine(ParallelGroupRoot(group.Id), "base");
        var changes = files.Union(inputs, WorkspacePaths).Order(WorkspacePaths).Select(path =>
        {
            if (!files.Contains(path)) return new ParallelChange(path, "deleted");
            if (!inputs.Contains(path)) return new ParallelChange(path, "added");
            return SameContent(workspace.RequireManaged(Path.Combine(baseline, path)), Path.Combine(working, path))
                ? null : new ParallelChange(path, "modified");
        }).OfType<ParallelChange>().ToArray();
        var submission = new ParallelSubmission(Guid.NewGuid().ToString(), changes);
        var part = task.IsParallelMerge ? group.Merge! : group.Workers.Single(worker => worker.TaskId == task.TaskId);
        part = part with { Submission = submission };
        var root = ParallelSubmissionRoot(part);
        var now = Now;
        var ttl = s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!.ArtifactTtlDays;
        void SaveFile(string from, string name, string kind)
        {
            var path = workspace.RequireManaged(Path.Combine(root, name));
            CopyParallelFile(workspace.RequireManaged(from), path);
            AddArtifact(s, new ArtifactRecord(Guid.NewGuid().ToString(), task.TaskId, task.AttemptId, kind,
                path, now, now.AddDays(ttl), "available", RelayOwned: false));
        }
        try
        {
            SaveFile(receipt.ResultFile, "report.md", "parallel-report");
            foreach (var change in changes.Where(change => change.Kind != "deleted"))
                SaveFile(Path.Combine(working, change.Path), Path.Combine("files", change.Path), "parallel-output");
            SaveParallelGroup(s, task.IsParallelMerge ? group with { Merge = part } : group with
            {
                Workers = group.Workers.Select(worker => worker.TaskId == task.TaskId ? part : worker).ToArray()
            });
            task.ResultPath = task.ManualCurrentFile = Path.Combine(root, "report.md");
        }
        catch
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            throw;
        }
    }

    private ParallelReview ReviewParallel(StoreSession s, ParallelGroup group)
    {
        ParallelMember ReadPart(ParallelPart part)
        {
            var task = s.Json<TaskRecord>("SELECT json FROM tasks WHERE id=$id", ("$id", part.TaskId));
            var root = ParallelSubmissionRoot(part);
            var report = part.Submission is null ? null : Path.Combine(root, "report.md");
            var changes = part.Submission?.Changes.Select(change => new ParallelFileChange(change.Path, change.Kind,
                change.Kind == "deleted" ? null : Path.Combine(root, "files", change.Path))).ToArray() ?? [];
            var problem = report is not null && (!File.Exists(report) ||
                    changes.Any(change => change.Snapshot is not null && !File.Exists(change.Snapshot)))
                ? "missing_output"
                : part.Submission is null && (task is null || Terminal(task.State)) ? "no_result" : null;
            return new(part.TaskId, part.AgentId, part.Instructions, task?.State, problem, task?.ErrorMessage ?? task?.PauseMessage,
                Path.Combine(workspace.AttemptDirectory(part.TaskId, part.AttemptId), ".relay", "parallel-work"),
                report, changes);
        }
        var workers = group.Workers.Select(ReadPart).ToArray();
        var paths = workers.Where(worker => worker.HasResult).SelectMany(worker => worker.Changes
            .Select(change => (Change: change, worker.TaskId))).ToLookup(item => item.Change.Path, WorkspacePaths);
        var shared = new HashSet<string>(WorkspacePaths);
        foreach (var path in paths)
        {
            var first = path.First().Change;
            if (path.Skip(1).Any(item => item.Change.Kind != first.Kind ||
                item.Change.Snapshot is { } snapshot && !SameContent(workspace.RequireManaged(first.Snapshot!), workspace.RequireManaged(snapshot))))
                shared.Add(path.Key);
            for (var parent = Path.GetDirectoryName(path.Key); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                if (paths[parent].Any(other => path.Any(item => item.TaskId != other.TaskId)))
                {
                    shared.Add(parent);
                    shared.Add(path.Key);
                }
        }
        var projectConflicts = paths.Select(path => path.Key).Where(path => ParallelProjectChanged(group, path))
            .Order(WorkspacePaths).ToArray();
        var ready = group.Merge is null && workers.All(worker => worker.State is not { } state || Terminal(state)) &&
            workers.Any(worker => worker.HasResult);
        return new(group.Id, group.Title, group.MergeAgentId, workers, shared.Order(WorkspacePaths).ToArray(),
            projectConflicts, ready, workers.Any(worker => worker.Problem is not null), group.Merge is null ? null : ReadPart(group.Merge));
    }

    private bool ParallelProjectChanged(ParallelGroup group, string relative)
    {
        var original = Path.Combine(group.ProjectDirectory, relative);
        if (!Directory.Exists(group.ProjectDirectory) || (File.GetAttributes(group.ProjectDirectory) & FileAttributes.ReparsePoint) != 0 ||
            HasRelocationLink(group.ProjectDirectory, original) || Directory.Exists(original)) return true;
        for (var parent = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            if (File.Exists(Path.Combine(group.ProjectDirectory, parent))) return true;
        if (!group.InputFiles.Contains(relative, WorkspacePaths)) return File.Exists(original);
        var baseline = workspace.RequireManaged(Path.Combine(ParallelGroupRoot(group.Id), "base", relative));
        return !File.Exists(original) || !SameContent(original, baseline);
    }

    private IEnumerable<string> ParallelWorkFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(workspace.RequireManaged(root));
        while (pending.TryPop(out var folder))
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (string.Equals(Path.GetFileName(entry), ".git", StringComparison.OrdinalIgnoreCase)) continue;
            var path = workspace.RequireManaged(entry);
            if (Directory.Exists(path)) pending.Push(path);
            else yield return NormalizeParallelPath(Path.GetRelativePath(root, path));
        }
    }

    private static string NormalizeParallelPath(string path)
    {
        var parts = path.Split(['/', '\\']);
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            part.Contains(':') || string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase) ||
            OperatingSystem.IsWindows() && (part.EndsWith('.') || part.EndsWith(' '))))
            throw new RelayException("parallel_relative_path", "请填写项目内的相对文件路径：" + path);
        return Path.Combine(parts);
    }

    private static void CopyParallelFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to);
        // A project file's read-only attribute must not prevent editing or removing Relay's own copies.
        var attributes = File.GetAttributes(to);
        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(to, attributes & ~FileAttributes.ReadOnly);
    }

    private string ParallelGroupRoot(string id) => workspace.RequireManaged(Path.Combine(workspace.Root, "parallel", id));
    private string ParallelWorkingDirectory(TaskRecord task) => Path.Combine(AttemptControlDirectory(task), "parallel-work");
    private string ParallelMergeManifest(TaskRecord task) => Path.Combine(AttemptControlDirectory(task), "parallel-inputs", "manifest.json");
    private string ParallelSubmissionRoot(ParallelPart part) => Path.Combine(
        workspace.AttemptDirectory(part.TaskId, part.AttemptId), ".relay", "parallel-results", part.Submission?.Id ?? "");

    private static ParallelGroup ReadParallelGroup(StoreSession s, string id) =>
        s.Json<ParallelGroup>("SELECT json FROM parallel_handoffs WHERE id=$id", ("$id", id)) ??
        throw new RelayException("parallel_not_found", "找不到并行分工。", 3);

    private static void SaveParallelGroup(StoreSession s, ParallelGroup group) =>
        s.Execute("UPDATE parallel_handoffs SET json=$json WHERE id=$id", ("$id", group.Id), ("$json", RelayJson.Write(group)));
}
