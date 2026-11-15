namespace AgentRelay.Protocol;

/// <summary>Optional ledger context supplied by a CLI when it already knows the task state.</summary>
public sealed class HandoffTaskContext
{
    public HandoffTaskState? State { get; init; }

    public DateTimeOffset? Now { get; init; }

    public DateTimeOffset? DeadlineAt { get; init; }

    public bool? Cancelled { get; init; }

    public ISessionStore? SessionStore { get; init; }
}
