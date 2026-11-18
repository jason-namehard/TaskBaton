using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TaskBaton.Core;

/// <summary>
/// Resolves the data root and the split between the new TaskBaton names and the legacy AgentRelay
/// names. Old homes and location records stay readable; a default old home is copied once to the new
/// default location and the old directory is left untouched for rollback.
/// </summary>
public static class TaskBatonHome
{
    public const string HomeVariable = "TASKBATON_HOME";
    public const string LegacyHomeVariable = "RELAY_HOME";
    public const string NewFolder = "TaskBaton";
    public const string LegacyFolder = "AgentRelay";
    public const string LocationFileName = "taskbaton.location.json";
    public const string LegacyLocationFileName = "relay.location.json";
    public const string NewDatabaseFileName = "taskbaton.db";
    public const string LegacyDatabaseFileName = "relay.db";

    private sealed record Location(string Home);

    public static string DefaultHome => DefaultHomeFor(LocalApplicationData);
    public static string LegacyHome => LegacyHomeFor(LocalApplicationData);
    public static string DefaultLocationFile => DefaultLocationFileFor(LocalApplicationData);
    public static string LegacyLocationFile => LegacyLocationFileFor(LocalApplicationData);

    internal static string DefaultHomeFor(string localApplicationData) => Path.Combine(localApplicationData, NewFolder);
    internal static string LegacyHomeFor(string localApplicationData) => Path.Combine(localApplicationData, LegacyFolder);
    internal static string DefaultLocationFileFor(string localApplicationData) =>
        Path.Combine(DefaultHomeFor(localApplicationData), LocationFileName);
    internal static string LegacyLocationFileFor(string localApplicationData) =>
        Path.Combine(LegacyHomeFor(localApplicationData), LegacyLocationFileName);

    public static string? EnvironmentHome =>
        NonEmpty(Environment.GetEnvironmentVariable(HomeVariable)) ??
        NonEmpty(Environment.GetEnvironmentVariable(LegacyHomeVariable));

    /// <summary>
    /// Resolution order: explicit --home, TASKBATON_HOME, RELAY_HOME, new location record, old
    /// location record, a migrated old default home, then the new default home.
    /// </summary>
    public static string Resolve(string? explicitHome = null, string? locationFile = null) =>
        ResolveForRoot(LocalApplicationData, explicitHome, locationFile, checkRunningProcesses: true);

    internal static string ResolveForRoot(string localApplicationData, string? explicitHome = null,
        string? locationFile = null, bool checkRunningProcesses = false)
    {
        if (!string.IsNullOrWhiteSpace(explicitHome)) return FullPath(explicitHome);
        if (EnvironmentHome is { } environmentHome) return FullPath(environmentHome);

        var defaultHome = DefaultHomeFor(localApplicationData);
        var legacyHome = LegacyHomeFor(localApplicationData);
        var defaultLocation = DefaultLocationFileFor(localApplicationData);
        var location = locationFile ?? defaultLocation;
        if (ReadLocation(location) is { } saved)
        {
            // A previous fallback may have recorded the old default home as the new location.
            // Retry the verified copy while the new default still does not exist.
            if (SamePath(location, defaultLocation) && SamePath(saved, legacyHome) && !Directory.Exists(defaultHome))
                return MigrateLegacyHome(localApplicationData, checkRunningProcesses) ?? saved;
            return saved;
        }

        if (SamePath(location, defaultLocation))
        {
            if (ReadLocation(LegacyLocationFileFor(localApplicationData)) is { } legacySaved)
            {
                if (SamePath(legacySaved, legacyHome) && !Directory.Exists(defaultHome))
                    return MigrateLegacyHome(localApplicationData, checkRunningProcesses) ?? legacySaved;
                return legacySaved;
            }
            if (Directory.Exists(legacyHome) && !Directory.Exists(defaultHome))
                return MigrateLegacyHome(localApplicationData, checkRunningProcesses) ?? FullPath(legacyHome);
        }
        return defaultHome;
    }

    /// <summary>Reads a location record; only the new file is written, the old file remains readable.</summary>
    public static string? ReadLocation(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var value = RelayJson.Read<Location>(File.ReadAllText(path));
            if (string.IsNullOrWhiteSpace(value.Home) || !Path.IsPathFullyQualified(value.Home))
                throw new RelayException("invalid_relay_location", $"中转站位置记录无效：{path}");
            return FullPath(value.Home);
        }
        catch (JsonException error)
        {
            throw new RelayException("invalid_relay_location", $"无法解析中转站位置记录 {path}：{error.Message}");
        }
    }

    public static void SaveLocation(string home, string? path = null)
    {
        var file = path ?? DefaultLocationFile;
        var value = new Location(FullPath(home));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, RelayJson.Write(value));
        File.Move(temporary, file, true);
    }

    /// <summary>New ledgers use taskbaton.db; an existing relay.db in the chosen home remains active.</summary>
    public static string ResolveDatabasePath(string home)
    {
        var current = Path.Combine(home, NewDatabaseFileName);
        if (File.Exists(current)) return current;
        var legacy = Path.Combine(home, LegacyDatabaseFileName);
        return File.Exists(legacy) ? legacy : current;
    }

    public static IEnumerable<string> DatabaseFiles(string home)
    {
        foreach (var name in new[] { NewDatabaseFileName, LegacyDatabaseFileName })
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                yield return Path.Combine(home, name + suffix);
    }

    public static bool IsDatabaseFile(string name) =>
        name is NewDatabaseFileName or LegacyDatabaseFileName or
            NewDatabaseFileName + "-wal" or NewDatabaseFileName + "-shm" or
            LegacyDatabaseFileName + "-wal" or LegacyDatabaseFileName + "-shm";

    /// <summary>
    /// Copies the old default home to the new one, verifies the copied ledger against the source,
    /// then records the new location. Returns null when the copy cannot be verified; the caller
    /// then keeps using the old home and no user file is deleted.
    /// </summary>
    internal static string? MigrateLegacyHome(string localApplicationData, bool checkRunningProcesses = false)
    {
        var source = LegacyHomeFor(localApplicationData);
        var destination = DefaultHomeFor(localApplicationData);
        if (!Directory.Exists(source) || Directory.Exists(destination)) return null;
        // Copying a live ledger can capture a mid-write WAL; let the old app finish or be closed first.
        if (checkRunningProcesses && (Process.GetProcessesByName("AgentRelay.Desktop").Length > 0 ||
            Process.GetProcessesByName("AgentRelay.Notify").Length > 0)) return null;
        var sourceDatabase = ResolveDatabasePath(source);
        if (!File.Exists(sourceDatabase)) return null;
        var expected = ReadCounts(sourceDatabase);
        if (expected is null) return null;

        var staging = destination + ".migrating-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(source, staging);
            if (!File.Exists(Path.Combine(staging, NewDatabaseFileName)) &&
                File.Exists(Path.Combine(staging, LegacyDatabaseFileName)))
                RenameDatabaseFamily(staging, LegacyDatabaseFileName, NewDatabaseFileName);
            var copied = ReadCounts(ResolveDatabasePath(staging));
            if (copied is null || copied != expected)
                throw new InvalidDataException("The copied ledger could not be verified.");
            Directory.Move(staging, destination);
            SaveLocation(destination, DefaultLocationFileFor(localApplicationData));
            return destination;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            TryDeleteOwnCopy(staging);
            return null;
        }
    }

    private static void RenameDatabaseFamily(string home, string from, string to)
    {
        foreach (var suffix in new[] { "-wal", "-shm", "" })
        {
            var source = Path.Combine(home, from + suffix);
            if (File.Exists(source)) File.Move(source, Path.Combine(home, to + suffix));
        }
    }

    private static (long Agents, long Tasks)? ReadCounts(string database)
    {
        if (!File.Exists(database)) return null;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            connection.Open();
            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA quick_check";
            if ((string?)check.ExecuteScalar() != "ok") return null;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT (SELECT COUNT(*) FROM agents),(SELECT COUNT(*) FROM tasks)";
            using var reader = command.ExecuteReader();
            return reader.Read() ? (reader.GetInt64(0), reader.GetInt64(1)) : null;
        }
        catch (SqliteException) { return null; }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The legacy home contains a linked path: " + entry);
            var output = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyDirectory(entry, output);
            else File.Copy(entry, output, false);
        }
    }

    // Only the staging directory created by this method is removed; the old home is never touched.
    private static void TryDeleteOwnCopy(string staging)
    {
        try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string LocalApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool SamePath(string first, string second) => string.Equals(
        FullPath(first), FullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
