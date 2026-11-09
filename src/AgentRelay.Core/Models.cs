using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRelay.Core;

public static class RelayJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        Converters = { new UtcTimestampConverter(), new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new JsonException($"Expected {typeof(T).Name}.");
}

public sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
}

public sealed class RelayException(string code, string message, int exitCode = 2) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;
}

public enum TaskState
{
    Created, AwaitingConfirmation, AwaitingManual, Dispatching, Dispatched,
    Acknowledged, Running, Succeeded, Failed, TimedOut, DeliveryUnknown, Cancelled, AwaitingReply, Paused
}

public sealed record AgentRecord(string AgentId, string Host, string ConfigPath, string? CliPath,
    string? HostVersion, string[] InstructionPaths, string ValidationStatus, DateTimeOffset? VerifiedAt,
    string? DisplayName = null);
public sealed record AdapterRecord(string AdapterId, string Executable, string Version, string VerificationStatus,
    DateTimeOffset VerifiedAt, string Evidence);
public sealed record OriginSession(string Host, string SessionId, string ConfigProfile, string Cwd);
public sealed record DispatchRequest(string From, string To, string Content, string Mode, string AdapterId,
    string IdempotencyKey, DateTimeOffset? DeadlineAt = null, OriginSession? OriginSession = null);
public sealed record RelaySettings(int ArtifactTtlDays = 3, int TaskTimeoutMinutes = 30,
    int DeliveryTimeoutSeconds = 30, int MaxDeliveryAttempts = 3);
public sealed record ImageInput(string FileName, string MediaType, byte[] Data);
public sealed record TaskImage(string ImageId, string FileName, string MediaType, string Path);

public sealed class TaskRecord
{
    public required string TaskId { get; set; }
    public required string RequesterId { get; set; }
    public required string RecipientId { get; set; }
    public required string AttemptId { get; set; }
    public required string AdapterId { get; set; }
    public required string Mode { get; set; }
    public required string RequestPath { get; set; }
    public required string ResultPath { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }
    public required DateTimeOffset DeadlineAt { get; set; }
    public TaskState State { get; set; }
    public int Revision { get; set; }
    public int ExecutionAttempt { get; set; } = 1;
    public string ResumeStatus { get; set; } = "manual_required";
    public string DeadlineDelivery { get; set; } = "on_next_command";
    public OriginSession? OriginSession { get; set; }
    public ResumerRecord? ResumeAdapter { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool? SideEffectsPossible { get; set; }
    public bool IsFileHandoff { get; set; }
    public string? AwaitingAgentId { get; set; }
    public int ManualTurn { get; set; }
    public bool ManualContinuous { get; set; }
    public string? ManualCurrentFile { get; set; }
    public string? ManualNextAction { get; set; }
    public string? ManualReceiptJson { get; set; }
    public string? ManualSourceFile { get; set; }
    public TaskImage[]? Images { get; set; }
    public int? CliFileTurn { get; set; }
    public int? ManualResendTurn { get; set; }
    public string? CliFileStatus { get; set; }
    public string? CliFileSessionId { get; set; }
    public string? CliFileError { get; set; }
    public string? CliNewSessionCwd { get; set; }
    public string? CliFileOperation { get; set; }
    public string? CliFileRevision { get; set; }
    public string[]? HandoffPlan { get; set; }
    public CollaborationRecord? Collaboration { get; set; }
    public int? MaxTurns { get; set; }
    public int? TurnTimeoutMinutes { get; set; }
    public bool AutoDeliver { get; set; }
    public string? PauseReason { get; set; }
    public string? PauseMessage { get; set; }
    public TaskState? PausedFrom { get; set; }
    // Every applied receipt status is kept, so quick received → working → done updates and restarts do not lose steps.
    public List<TurnStatusEntry> TurnStatuses { get; set; } = [];
    // Lightweight fixed-count relay chosen by the user; null keeps the single-handoff or review-loop behaviour.
    public RelayStep[]? RelaySteps { get; set; }
    public bool IndependentAcceptance { get; set; }
    public bool NewSessionEachTurn { get; set; }
    // Images travel with the first turn by default; a later turn only receives the images the user selected for it.
    public int? ImageTurn { get; set; }
    public string[]? ImageTurnIds { get; set; }
    // Delivery, acknowledgement and work start are separate facts; the turn deadline only bounds the agent's own work.
    public DateTimeOffset? SendStartedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? WorkingAt { get; set; }
    public int HostWaitSeconds { get; set; }
    public bool? HostTurnEnded { get; set; }
    public string? CompletionNote { get; set; }
}

public sealed record TaskView(TaskRecord Task, bool DeadlineExceeded, string NextAction, string Prompt,
    ExecutionRecord? Execution = null, ContinuationRecord? Continuation = null, HandoffProgress? Progress = null);
public sealed record ArtifactRecord(string ArtifactId, string TaskId, string AttemptId, string Kind,
    string Path, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string State);
public sealed record AuditEvent(long Sequence, string EventId, string TaskId, string AttemptId,
    string Event, string SenderId, DateTimeOffset RecordedAt, string Detail);
public sealed record InboxItem(long NotificationId, string TaskId, string Event, string Message,
    DateTimeOffset CreatedAt, bool IsRead);
public sealed record OutboxItem(string TaskId, string AttemptId, string State, int Submissions,
    DateTimeOffset? LeaseUntil, string? Receipt);
public sealed record DeliveryResult(string Status, string? Receipt, string? Error, bool Retryable = false,
    CliSidebarSyncResult? SidebarSync = null);
public sealed record CleanupResult(int Eligible, int Deleted, string[] Paths);

public sealed record ExecutorRecord(string AgentId, string Kind, string Executable, string Version,
    string VerificationStatus, DateTimeOffset VerifiedAt, string ConfigDirectory,
    string ConfigurationMode = "ignore_user_config_read_only");
public sealed record ExecutionRecord(string TaskId, string AttemptId, string AgentId, string State,
    DateTimeOffset CreatedAt, DateTimeOffset LeaseUntil, string? SessionId, string? ErrorCode, int? WorkerPid = null);
public sealed record ExecutionInput(TaskRecord Task, string RequestMarkdown, string Directory, int TimeoutSeconds);
public sealed record AgentOutcome(string Status, string ResultMarkdown, string ErrorCode, string Message,
    string? SessionId = null);

public interface IAgentExecutor
{
    Task<AgentOutcome> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken);
}

public interface IMessageReceiver
{
    Task ReceiveAsync(TaskRecord task, string home, OutboxItem evidence, TimeSpan timeout, CancellationToken cancellationToken);
}

public interface IDeliveryAdapter
{
    string Id { get; }
    Task<DeliveryResult> DeliverAsync(TaskRecord task, string home, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record ResumerRecord(string AgentId, string Kind, string Executable, string Version,
    string ConfigPath, DateTimeOffset VerifiedAt);
public sealed record ContinuationRecord(string TaskId, string AttemptId, string Event, string State,
    string DesktopState, DateTimeOffset? LeaseUntil, string? ErrorCode = null, string? Detail = null,
    string? OutputPath = null);
public sealed record ResumeInput(TaskRecord Task, string Prompt, string Directory, int TimeoutSeconds);
public sealed record ResumeResult(string State, string? ErrorCode = null, string? Detail = null, string? OutputPath = null);
public interface ISessionResumer
{
    Task<ResumeResult> ResumeAsync(ResumeInput input, CancellationToken cancellationToken);
}
public interface IDesktopNotifier
{
    Task<ResumeResult> NotifyAsync(string home, TaskRecord task, CancellationToken cancellationToken);
}
