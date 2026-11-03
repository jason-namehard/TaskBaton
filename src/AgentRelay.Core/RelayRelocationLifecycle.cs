using System.Security.Cryptography;

namespace AgentRelay.Core;

internal sealed record RelocationFileSnapshot(string RelativePath, long Length, string ContentHash);
internal sealed record RelocationCleanupPlan(RelayRelocationRequest Request, DateTimeOffset DeleteAfter,
    RelocationFileSnapshot[] Files, string[] Directories, string[] CompletedFiles, string[] ModifiedFiles,
    string[] AddedEntries, bool Attempted);
public sealed record RelayRelocationCleanupStatus(string Source, string Target, DateTimeOffset DeleteAfter,
    bool Attempted, string[] ModifiedFiles, string[] AddedEntries);

public sealed partial class RelayService
{
    private const string RelocationCleanupFile = "relay.relocation.cleanup.json";
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public void QueueRelocation(string target)
    {
        var request = ValidateRelocationTarget(target);
        var previous = PendingRelocation();
        if (previous is not null && previous != request && File.Exists(Path.Combine(previous.Target, RelocationIncompleteFile)))
            throw new RelayException("relocation_retry_required", "已有未完成的迁移，请先使用迁移校验重试。");
        WriteRelocationFile(Path.Combine(Home, RelocationPendingFile), RelayJson.Write(request));
    }

    public RelayRelocationRequest? PendingRelocation()
    {
        var path = Path.Combine(Home, RelocationPendingFile);
        if (!File.Exists(path)) return null;
        var request = RelayJson.Read<RelayRelocationRequest>(File.ReadAllText(path));
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Home), request.Source, PathComparison))
            throw new RelayException("relocation_request_invalid", "待迁移记录与当前中转站不符，请检查 " + path);
        return request;
    }

    public void CancelPendingRelocation()
    {
        var pending = PendingRelocation() ?? throw new RelayException("relocation_no_pending", "没有待继续的目录迁移。");
        if (File.Exists(Path.Combine(pending.Target, RelocationIncompleteFile)))
            throw new RelayException("relocation_retry_required", "迁移复制已开始，请使用迁移校验重试。");
        File.Delete(Path.Combine(Home, RelocationPendingFile));
    }

    public RelayService? TryCompletePendingRelocation()
    {
        var pending = PendingRelocation();
        if (pending is null || File.Exists(Path.Combine(pending.Target, RelocationIncompleteFile))) return null;
        try { return Relocate(pending.Target); }
        catch (RelayException error) when (error.Code == "relocation_active_tasks") { return null; }
    }

    public static string ResolveRelocatedHome(string home)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        var visited = new HashSet<string>(PathComparer);
        while (File.Exists(Path.Combine(current, RelocationMovedFile)))
        {
            if (!visited.Add(current)) throw new RelayException("relocation_cycle", "迁移记录存在循环，请检查中转站位置。");
            var request = RelayJson.Read<RelayRelocationRequest>(File.ReadAllText(Path.Combine(current, RelocationMovedFile)));
            if (!string.Equals(request.Source, current, PathComparison) || !Path.IsPathFullyQualified(request.Target) ||
                !File.Exists(Path.Combine(request.Target, "relay.db")) || File.Exists(Path.Combine(request.Target, RelocationIncompleteFile)))
                throw new RelayException("relocation_target_invalid", "已迁移的中转站位置无效，请检查旧目录中的迁移记录：" + current);
            current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Target));
        }
        return current;
    }

    public IReadOnlyList<RelayRelocationCleanupStatus> RelocationCleanupPlans() =>
        ReadRelocationCleanupPlans(Home).Select(CleanupStatus).ToArray();

    public IReadOnlyList<RelayRelocationCleanupStatus> CleanupRelocatedHomes()
    {
        var plans = ReadRelocationCleanupPlans(Home).ToList();
        for (var index = 0; index < plans.Count;)
        {
            var plan = plans[index];
            if (plan.DeleteAfter > Now) { index++; continue; }
            if (!Directory.Exists(plan.Request.Source)) { plans.RemoveAt(index); SaveRelocationCleanupPlans(Home, plans); continue; }
            var completed = new HashSet<string>(plan.CompletedFiles, PathComparer);
            var modified = new HashSet<string>(plan.ModifiedFiles, PathComparer);
            try
            {
                RequireCleanupSource(plan.Request);
                foreach (var file in plan.Files)
                {
                    var path = CleanupChildPath(plan.Request.Source, file.RelativePath);
                    if (completed.Contains(file.RelativePath) || modified.Contains(file.RelativePath)) continue;
                    if (!File.Exists(path) && !Directory.Exists(path)) { completed.Add(file.RelativePath); continue; }
                    if (HasRelocationLink(plan.Request.Source, path) || !File.Exists(path) ||
                        new FileInfo(path).Length != file.Length || RelocationFileHash(path) != file.ContentHash)
                    {
                        modified.Add(file.RelativePath);
                        continue;
                    }
                    File.Delete(path);
                    completed.Add(file.RelativePath);
                }
                foreach (var relative in plan.Directories.OrderByDescending(path => path.Length))
                {
                    var directory = CleanupChildPath(plan.Request.Source, relative);
                    if (Directory.Exists(directory) && !HasRelocationLink(plan.Request.Source, directory) &&
                        !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                var remaining = RelocationRemainingEntries(plan.Request.Source);
                var modifiedPaths = modified.Select(path => CleanupChildPath(plan.Request.Source, path))
                    .Where(Path.Exists).ToArray();
                var modifiedSet = modifiedPaths.ToHashSet(PathComparer);
                var added = remaining.Where(path => !modifiedSet.Contains(path)).ToArray();
                if (remaining.Length == 0)
                {
                    var marker = Path.Combine(plan.Request.Source, RelocationMovedFile);
                    File.Delete(marker);
                    try { Directory.Delete(plan.Request.Source); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        if (Directory.Exists(plan.Request.Source)) File.WriteAllText(marker, RelayJson.Write(plan.Request));
                        throw;
                    }
                    plans.RemoveAt(index);
                }
                else
                {
                    plans[index] = plan with { CompletedFiles = [.. completed], ModifiedFiles = [.. modified], AddedEntries = added, Attempted = true };
                    index++;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                plans[index] = plan with { CompletedFiles = [.. completed], ModifiedFiles = [.. modified], Attempted = true };
                SaveRelocationCleanupPlans(Home, plans);
                throw new RelayException("relocation_cleanup_failed", $"无法清理旧中转站 {plan.Request.Source}，下次刷新会重试：{error.Message}");
            }
            SaveRelocationCleanupPlans(Home, plans);
        }
        return plans.Select(CleanupStatus).ToArray();
    }

    private void AddRelocationCleanupPlan(string target, RelayRelocationRequest request,
        List<RelocationFileSnapshot> files, List<string> directories, List<RelocationCleanupPlan> plans)
    {
        plans.Add(new(request, Now.AddHours(24), [.. files], [.. directories], [], [], [], false));
        SaveRelocationCleanupPlans(target, plans);
    }

    private static List<RelocationCleanupPlan> OutstandingRelocationCleanupPlans(string home) =>
        ReadRelocationCleanupPlans(home).Where(plan => Directory.Exists(plan.Request.Source) &&
            Directory.EnumerateFileSystemEntries(plan.Request.Source).Any()).ToList();

    private static void FinalizeRelocationDatabaseSnapshot(string target, RelayRelocationRequest request)
    {
        string[] names = ["relay.db", "relay.db-wal", "relay.db-shm"];
        var plans = ReadRelocationCleanupPlans(target).ToList();
        var index = plans.FindIndex(plan => plan.Request == request);
        var files = plans[index].Files.Where(file => !names.Contains(file.RelativePath, PathComparer)).ToList();
        foreach (var name in names)
            if (File.Exists(Path.Combine(request.Source, name)))
                files.Add(SnapshotRelocationFile(request.Source, Path.Combine(request.Source, name)));
        plans[index] = plans[index] with { Files = [.. files] };
        SaveRelocationCleanupPlans(target, plans);
    }

    private static RelayRelocationCleanupStatus CleanupStatus(RelocationCleanupPlan plan) => new(
        plan.Request.Source, plan.Request.Target, plan.DeleteAfter, plan.Attempted,
        plan.ModifiedFiles.Select(path => CleanupChildPath(plan.Request.Source, path)).Where(Path.Exists).ToArray(), plan.AddedEntries);

    private static RelocationCleanupPlan[] ReadRelocationCleanupPlans(string home)
    {
        var path = Path.Combine(home, RelocationCleanupFile);
        return File.Exists(path) ? RelayJson.Read<RelocationCleanupPlan[]>(File.ReadAllText(path)) : [];
    }

    private static void SaveRelocationCleanupPlans(string home, IEnumerable<RelocationCleanupPlan> plans) =>
        WriteRelocationFile(Path.Combine(home, RelocationCleanupFile), RelayJson.Write(plans));

    private static void WriteRelocationFile(string path, string text)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }

    private static RelocationFileSnapshot SnapshotRelocationFile(string root, string path) =>
        new(Path.GetRelativePath(root, path), new FileInfo(path).Length, RelocationFileHash(path));

    // The digest determines whether the old file is still the exact copy that was migrated.
    private static string RelocationFileHash(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private void RequireCleanupSource(RelayRelocationRequest request)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Source));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Home));
        if (string.Equals(source, Path.GetPathRoot(source), PathComparison) || string.Equals(source, current, PathComparison) ||
            Within(current, source, PathComparison) || Within(source, current, PathComparison) ||
            (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new RelayException("relocation_cleanup_invalid", "旧中转站清理位置无效，未删除任何内容：" + source);
        var marker = Path.Combine(source, RelocationMovedFile);
        if (!File.Exists(marker) && !Directory.EnumerateFileSystemEntries(source).Any()) return;
        if (!File.Exists(marker) || RelayJson.Read<RelayRelocationRequest>(File.ReadAllText(marker)) != request)
            throw new RelayException("relocation_cleanup_marker", "旧中转站迁移标记不存在或已更改，未删除该目录：" + source);
    }

    private static string CleanupChildPath(string source, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new RelayException("relocation_cleanup_invalid", "旧中转站清理记录包含绝对路径。");
        var path = Path.GetFullPath(Path.Combine(source, relative));
        if (!Within(path, Path.TrimEndingDirectorySeparator(source), PathComparison))
            throw new RelayException("relocation_cleanup_invalid", "旧中转站清理记录超出旧目录：" + path);
        return path;
    }

    private static bool HasRelocationLink(string source, string path)
    {
        var current = source;
        foreach (var part in Path.GetRelativePath(source, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        }
        return false;
    }

    private static string[] RelocationRemainingEntries(string source)
    {
        var result = new List<string>();
        void Visit(string directory)
        {
            var entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            if (entries.Length == 0 && !string.Equals(directory, source, PathComparison)) result.Add(directory);
            foreach (var entry in entries)
            {
                if (string.Equals(entry, Path.Combine(source, RelocationMovedFile), PathComparison)) continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0) Visit(entry);
                else result.Add(entry);
            }
        }
        Visit(source);
        return [.. result];
    }
}
