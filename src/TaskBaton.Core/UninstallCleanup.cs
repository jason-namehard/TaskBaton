namespace TaskBaton.Core;

/// <summary>Uninstall only removes TaskBaton's marked rules and explicitly named data files.</summary>
public static class UninstallCleanup
{
    public static string ResolveHome(string locationFile, string? overrideHome)
    {
        var home = string.IsNullOrWhiteSpace(overrideHome) ? null : overrideHome;
        if (home is null)
        {
            home = TaskBatonHome.ReadLocation(locationFile);
            // A new installer passes the new location file; installations that have not started the
            // new desktop yet still only have the old record.
            if (home is null && SamePath(locationFile, TaskBatonHome.DefaultLocationFile))
                home = TaskBatonHome.ReadLocation(TaskBatonHome.LegacyLocationFile);
        }
        if (home is null)
            throw new RelayException("uninstall_location_missing", "找不到中转站位置记录；为保护数据，未清理任何内容。请重新打开软件确认站点位置后再试。", 3);
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home))
            throw new RelayException("invalid_relay_location", "中转站位置无效；为保护数据，未清理任何内容。");
        return Path.GetFullPath(home);
    }

    public static int Apply(string locationFile, string? overrideHome, string expectedHome,
        bool removeRules, bool removeData)
    {
        var home = ResolveHome(locationFile, overrideHome);
        if (!SamePath(home, expectedHome))
            throw new RelayException("uninstall_location_changed", "中转站位置已经改变；未清理数据，请重新卸载并确认。");

        var removeLocations = removeData
            ? new[] { locationFile, TaskBatonHome.LegacyLocationFile }
                .Where(File.Exists)
                .Where(path => TaskBatonHome.ReadLocation(path) is { } recorded && SamePath(recorded, home))
                .Distinct(StringComparerForPlatform())
                .ToArray()
            : [];

        // The guidance state is the only record of inserted rules, so remove rules before deleting it.
        var removed = removeRules || removeData ? new RelayService(home).RemoveAllGuidance() : 0;
        if (removeData)
        {
            // Delete the database before its WAL; if the database is locked, its recoverable WAL stays intact.
            foreach (var database in TaskBatonHome.DatabaseFiles(home)) File.Delete(database);
            foreach (var name in new[] { "desktop.preferences.json", "agent-guidance.json" })
                File.Delete(Path.Combine(home, name));
            // A TASKBATON_HOME/RELAY_HOME override does not necessarily match the saved location.
            foreach (var path in removeLocations) File.Delete(path);
        }
        return removed;
    }

    private static StringComparer StringComparerForPlatform() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool SamePath(string first, string second) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
