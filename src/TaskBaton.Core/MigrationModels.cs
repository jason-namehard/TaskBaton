namespace TaskBaton.Core;

public sealed record AdapterSource(string AdapterId, string Version, string Repository, string Revision,
    string License, string[] Capabilities, string[] Dependencies, string[] Limitations);
public sealed record MigrationRequest(AdapterSource Source, string TargetPath, string Format,
    string Before, string After, string Purpose = "install", string? ValidatedMigrationId = null);
public sealed record MigrationPlan(string MigrationId, MigrationRequest Request, string State,
    DateTimeOffset CreatedAt, bool OriginalExists, string OriginalPath, string ProposedPath,
    string BackupPath, string PreviewPath, string? ProofTaskId = null, string? Reason = null);
public sealed record MigrationEvent(string MigrationId, string Event, DateTimeOffset RecordedAt, string Detail);
public sealed record McpEntry(string Name, string? Command, string[] Arguments, string? Url, bool Enabled = true);
public sealed record ConfigurationInventory(string Path, string Format, McpEntry[] McpEntries);
