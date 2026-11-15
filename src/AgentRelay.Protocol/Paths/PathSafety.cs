namespace AgentRelay.Protocol;

/// <summary>
/// Portable relative-path rules shared by schema validation and semantic checks.
/// A handoff path is relative to the directory that contains HANDOFF.md, uses forward slashes,
/// and must not escape that directory.
/// </summary>
public static class PathSafety
{
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (path.IndexOf('\0') >= 0)
            return false;
        if (path.Contains('\\'))
            return false;
        if (path.StartsWith('/'))
            return false;
        if (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
            return false;
        if (Path.IsPathRooted(path))
            return false;

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                return false;
        }

        return true;
    }

    public static bool IsContainedInRoot(string? managedRoot, string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(managedRoot) || string.IsNullOrWhiteSpace(path))
            return false;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managedRoot));
        var candidate = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));
        candidate = Path.TrimEndingDirectorySeparator(candidate);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(candidate, root, comparison))
            return true;

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, comparison))
            return false;

        fullPath = candidate;
        return true;
    }
}
