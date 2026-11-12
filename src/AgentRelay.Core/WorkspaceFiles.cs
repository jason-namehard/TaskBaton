namespace AgentRelay.Core;

public enum WorkspaceFileOwner { Relay, User, TaskControl, Unknown }
public enum WorkspaceFileStatus { Eligible, InUse, Retained, Protected, Unverified, Missing, Removed, Unsafe }
public sealed record WorkspaceTaskReference(string TaskId, string AttemptId, TaskState State, string Use, bool Active);
public sealed record WorkspaceFileEntry(string Path, WorkspaceFileOwner Owner, WorkspaceFileStatus Status,
    string? ArtifactId, string? OwnerTaskId, string? Kind, DateTimeOffset? ExpiresAt,
    WorkspaceTaskReference[] References, long? SizeBytes, DateTime? LastWriteTimeUtc);

public sealed partial class RelayService
{
    private static readonly StringComparer WorkspacePaths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparison WorkspacePathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool SameWorkspacePath(string left, string right) =>
        WorkspacePaths.Equals(Path.GetFullPath(left), Path.GetFullPath(right));

    private static bool UnderWorkspacePath(string path, string directory) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, WorkspacePathComparison);

    private sealed record WorkspaceIndex(Dictionary<string, TaskRecord> Tasks,
        Dictionary<string, ArtifactRecord[]> Artifacts, Dictionary<string, List<WorkspaceTaskReference>> References,
        List<(string Path, WorkspaceTaskReference Reference)> UserDirectories, HashSet<(string Task, string Attempt)> ActiveAttempts);

    public WorkspaceFileEntry[] WorkspaceFiles()
    {
        var now = Now;
        var index = store.Read(s => ReadWorkspaceIndex(s, now));
        var paths = index.Artifacts.Keys.Concat(index.References.Keys).ToHashSet(WorkspacePaths);
        if (Directory.Exists(workspace.Root))
            paths.UnionWith(Directory.EnumerateFiles(workspace.Root, "*",
                new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }));
        return paths.Order(WorkspacePaths).Select(path => DescribeWorkspaceFile(index, path, now)).ToArray();
    }

    public CleanupResult Cleanup(bool apply)
    {
        var now = Now;
        var index = store.Read(s => ReadWorkspaceIndex(s, now));
        var preview = index.Artifacts.Keys.Order(WorkspacePaths).Select(path => DescribeWorkspaceFile(index, path, now))
            .Where(file => file.Status == WorkspaceFileStatus.Eligible).ToArray();
        return apply ? ApplyWorkspaceCleanup(preview) : new(preview.Length, 0, preview.Select(file => file.Path).ToArray());
    }

    /// <summary>Applies the exact reviewed candidates and rechecks ownership, references and file metadata under the ledger write lock.</summary>
    public CleanupResult ApplyWorkspaceCleanup(IReadOnlyList<WorkspaceFileEntry> preview)
    {
        var candidates = preview.Where(file => file.Status == WorkspaceFileStatus.Eligible)
            .DistinctBy(file => file.Path, WorkspacePaths).ToArray();
        var paths = candidates.Select(file => file.Path).ToArray();
        if (paths.Length == 0) return new(0, 0, paths, []);
        return store.Write(s =>
        {
            var now = Now;
            var index = ReadWorkspaceIndex(s, now);
            var kept = new List<CleanupIssue>();
            var deleted = 0;
            foreach (var approved in candidates)
            {
                if (!index.Artifacts.TryGetValue(approved.Path, out var records) || records is not [var artifact] ||
                    artifact.ArtifactId != approved.ArtifactId)
                {
                    kept.Add(new(approved.Path));
                    continue;
                }
                var current = DescribeWorkspaceFile(index, approved.Path, now);
                if (current.Status != WorkspaceFileStatus.Eligible || current.SizeBytes != approved.SizeBytes ||
                    current.LastWriteTimeUtc != approved.LastWriteTimeUtc)
                {
                    kept.Add(new(approved.Path));
                    continue;
                }
                try { File.Delete(current.Path); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    kept.Add(new(current.Path, error.Message));
                    continue;
                }
                SaveArtifact(s, artifact with { State = "expired" });
                if (index.Tasks.TryGetValue(artifact.TaskId, out var task))
                    s.Event(task, "artifact.expired", "relay", now, artifact.ArtifactId);
                deleted++;
            }
            return new CleanupResult(paths.Length, deleted, paths, kept.ToArray());
        });
    }

    private WorkspaceIndex ReadWorkspaceIndex(StoreSession s, DateTimeOffset now)
    {
        var tasks = s.Query("SELECT json FROM tasks", row => RelayJson.Read<TaskRecord>(row.GetString(0)))
            .ToDictionary(task => task.TaskId, StringComparer.Ordinal);
        var artifacts = s.Query("SELECT json FROM artifacts", row => RelayJson.Read<ArtifactRecord>(row.GetString(0)))
            .GroupBy(artifact => Path.GetFullPath(artifact.Path), WorkspacePaths)
            .ToDictionary(group => group.Key, group => group.ToArray(), WorkspacePaths);
        var active = tasks.Values.Where(task => !Terminal(task.State) ||
                task.CliFileStatus == "submitting" && task.DeadlineAt > now)
            .Select(task => (task.TaskId, task.AttemptId)).ToHashSet();
        active.UnionWith(s.Query("SELECT json FROM executions", row => RelayJson.Read<ExecutionRecord>(row.GetString(0)))
            .Where(run => run.LeaseUntil > now && run.State is "scheduled" or "receiving" or "running")
            .Select(run => (run.TaskId, run.AttemptId)));
        active.UnionWith(s.Query("SELECT json FROM continuations", row => RelayJson.Read<ContinuationRecord>(row.GetString(0)))
            .Where(entry => entry.LeaseUntil > now && (entry.State == "in_flight" || entry.DesktopState == "in_flight"))
            .Select(entry => (entry.TaskId, entry.AttemptId)));

        var cliDraftDirectory = CliDraftDirectory;
        var references = new Dictionary<string, List<WorkspaceTaskReference>>(WorkspacePaths);
        var userDirectories = new List<(string Path, WorkspaceTaskReference Reference)>();
        foreach (var task in tasks.Values)
        {
            void Add(string? value, string use)
            {
                // Deliverable fields also accept a description. Only complete filesystem paths are references.
                if (value is null || !Path.IsPathFullyQualified(value) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return;
                var path = Path.GetFullPath(value);
                // Earlier CLI tasks kept a source reference to the temporary draft after publishing the request.
                if (use == "source" && WorkspacePaths.Equals(Path.GetDirectoryName(path), cliDraftDirectory) && !File.Exists(path)) return;
                var reference = new WorkspaceTaskReference(task.TaskId, task.AttemptId, task.State, use,
                    active.Contains((task.TaskId, task.AttemptId)));
                if (!references.TryGetValue(path, out var list)) references.Add(path, list = []);
                if (list.Contains(reference)) return;
                list.Add(reference);
                if (use == "deliverable") userDirectories.Add((path, reference));
            }
            Add(task.RequestPath, "request");
            Add(task.ResultPath, "result");
            Add(task.ManualSourceFile, "source");
            Add(task.ManualCurrentFile, "current");
            foreach (var image in task.Images ?? []) Add(image.Path, "image");
            if (task.Collaboration is { } collaboration)
            {
                Add(collaboration.SubmissionPath, "review");
                Add(collaboration.ApprovedSubmissionPath, "review");
                Add(collaboration.Deliverable, "deliverable");
                foreach (var step in collaboration.History) Add(step.ResultPath, "review");
            }
            if (task.ManualReceiptJson is { } json)
            {
                var receipt = ManualTransfers.ParseReceipt(json);
                Add(receipt.ResultFile, "submission");
                Add(receipt.Deliverable, "deliverable");
            }
        }
        foreach (var (path, records) in artifacts)
        foreach (var artifact in records)
        {
            if (!tasks.TryGetValue(artifact.TaskId, out var task)) continue;
            if (!references.TryGetValue(path, out var list)) references.Add(path, list = []);
            list.Add(new(task.TaskId, artifact.AttemptId, task.State, "registered",
                active.Contains((task.TaskId, artifact.AttemptId))));
        }
        return new(tasks, artifacts, references, userDirectories, active);
    }

    private WorkspaceFileEntry DescribeWorkspaceFile(WorkspaceIndex index, string path, DateTimeOffset now)
    {
        index.Artifacts.TryGetValue(path, out var records);
        var artifact = records is [var unique] ? unique : null;
        index.Tasks.TryGetValue(artifact?.TaskId ?? "", out var ownerTask);
        var inside = UnderWorkspacePath(path, workspace.Root);
        var references = index.References.TryGetValue(path, out var matches) ? matches.ToList() : [];
        foreach (var directory in index.UserDirectories.Where(directory => UnderWorkspacePath(path, directory.Path)))
            references.Add(directory.Reference);

        var info = new FileInfo(path);
        var exists = inside && info.Exists;
        var owner = references.Any(reference => reference.Use is "source" or "deliverable") ||
            !inside || records?.Any(record => record.RelayOwned == false) == true ? WorkspaceFileOwner.User
            : artifact is not null && (artifact.State != "expired" || !exists) && IsRelayArtifact(artifact, ownerTask)
                ? WorkspaceFileOwner.Relay : WorkspaceFileOwner.Unknown;

        if (records is null && inside)
        {
            var parts = Path.GetRelativePath(workspace.Root, path).Split(Path.DirectorySeparatorChar, 4);
            if (parts.Length == 4 && parts[2] == ".relay" && index.Tasks.TryGetValue(parts[0], out var task) && task.AttemptId == parts[1])
            {
                references.Add(new(task.TaskId, task.AttemptId, task.State, "control", index.ActiveAttempts.Contains((task.TaskId, task.AttemptId))));
                if (owner == WorkspaceFileOwner.Unknown) owner = WorkspaceFileOwner.TaskControl;
            }
        }
        var status = owner switch
        {
            WorkspaceFileOwner.Relay when artifact!.State == "expired" => WorkspaceFileStatus.Removed,
            WorkspaceFileOwner.Relay when !exists => WorkspaceFileStatus.Missing,
            _ when references.Any(reference => reference.Active) => WorkspaceFileStatus.InUse,
            WorkspaceFileOwner.User or WorkspaceFileOwner.TaskControl => WorkspaceFileStatus.Protected,
            WorkspaceFileOwner.Unknown => WorkspaceFileStatus.Unverified,
            _ when artifact!.ExpiresAt > now => WorkspaceFileStatus.Retained,
            _ => WorkspaceFileStatus.Eligible
        };
        if (status == WorkspaceFileStatus.Eligible)
        {
            try { workspace.RequireManaged(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                RelayException { Code: "linked_path" or "unmanaged_path" }) { status = WorkspaceFileStatus.Unsafe; }
        }
        return new(path, owner, status, artifact?.ArtifactId, artifact?.TaskId, artifact?.Kind, artifact?.ExpiresAt,
            references.ToArray(), exists ? info.Length : null, exists ? info.LastWriteTimeUtc : null);
    }

    private void PreserveWorkspaceOwnership(StoreSession s, TaskRecord task)
    {
        var now = Now;
        var index = ReadWorkspaceIndex(s, now);
        foreach (var (path, records) in index.Artifacts)
        {
            if (records is not [var artifact]) continue;
            if (artifact.TaskId != task.TaskId &&
                !(index.References.TryGetValue(path, out var references) &&
                  references.Any(reference => reference.TaskId == task.TaskId && reference.Use is "source" or "deliverable")) &&
                !index.UserDirectories.Any(entry => entry.Reference.TaskId == task.TaskId && UnderWorkspacePath(path, entry.Path)))
                continue;
            var owner = DescribeWorkspaceFile(index, path, now).Owner;
            if (owner == WorkspaceFileOwner.User && artifact.RelayOwned != false)
                SaveArtifact(s, artifact with { RelayOwned = false });
            else if (owner == WorkspaceFileOwner.Relay && artifact.RelayOwned is null)
                SaveArtifact(s, artifact with { RelayOwned = true });
        }
    }

    private bool IsRelayArtifact(ArtifactRecord artifact, TaskRecord? task)
    {
        if (artifact.RelayOwned is { } recorded) return recorded;
        var attempt = workspace.AttemptDirectory(artifact.TaskId, artifact.AttemptId);
        var path = Path.GetFullPath(artifact.Path);
        // Existing ledgers can prove ownership through the original publishing paths or a recorded source and its copy.
        return artifact.Kind switch
        {
            "image" => SameWorkspacePath(path, Path.Combine(attempt, "images", artifact.ArtifactId + ".png")) ||
                SameWorkspacePath(path, Path.Combine(attempt, "images", artifact.ArtifactId + ".jpg")),
            "submission" => SameWorkspacePath(path, Path.Combine(attempt, "result.md")),
            "request" when task is { IsFileHandoff: true, ManualSourceFile: { } source } =>
                UnderWorkspacePath(path, attempt) && SameWorkspacePath(path, task.RequestPath) && !SameWorkspacePath(path, source),
            "request" or "result" or "failure" => SameWorkspacePath(path,
                Path.Combine(attempt, $"{artifact.Kind}-{artifact.ArtifactId}.md")),
            _ => false
        };
    }
}
