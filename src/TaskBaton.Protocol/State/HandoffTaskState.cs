using System.Globalization;

namespace TaskBaton.Protocol;

/// <summary>
/// The small amount of ledger state needed for L3 checks. It is deliberately not a persistence
/// layer: callers may keep it in memory, in a database row, or reconstruct it from receipts.
/// </summary>
public sealed class HandoffTaskState
{
    private readonly Dictionary<ReceiptKey, ReceiptDocument> receipts = new();
    private readonly List<ReceiptDocument> evidence = [];

    private HandoffTaskState(
        string taskId,
        string currentAttemptId,
        int currentTurn,
        string expectedSender,
        DateTimeOffset? deadlineAt,
        bool cancelled)
    {
        TaskId = taskId;
        CurrentAttemptId = currentAttemptId;
        CurrentTurn = currentTurn;
        ExpectedSender = expectedSender;
        DeadlineAt = deadlineAt;
        Cancelled = cancelled;
    }

    public string TaskId { get; }

    public string CurrentAttemptId { get; }

    public int CurrentTurn { get; }

    public string ExpectedSender { get; }

    public DateTimeOffset? DeadlineAt { get; }

    public bool Cancelled { get; private set; }

    public bool IsTerminal { get; private set; }

    public bool HasFinalClaim { get; private set; }

    public int AppliedReceiptCount { get; private set; }

    public ReceiptDocument? LastReceipt { get; private set; }

    public IReadOnlyDictionary<ReceiptKey, ReceiptDocument> Receipts => receipts;

    public IReadOnlyCollection<ReceiptDocument> Evidence => evidence;

    public static HandoffTaskState FromHandoff(HandoffFrontmatter frontmatter, DateTimeOffset? deadlineAt = null) =>
        new(
            frontmatter.TaskId ?? string.Empty,
            frontmatter.AttemptId ?? string.Empty,
            frontmatter.Turn ?? 0,
            frontmatter.To ?? string.Empty,
            deadlineAt ?? ParseTimestamp(frontmatter.DeadlineAt),
            cancelled: false);

    public static HandoffTaskState Create(
        string taskId,
        string currentAttemptId,
        int currentTurn,
        string expectedSender,
        DateTimeOffset? deadlineAt = null,
        bool cancelled = false) =>
        new(taskId, currentAttemptId, currentTurn, expectedSender, deadlineAt, cancelled);

    public HandoffTaskState Clone()
    {
        var clone = new HandoffTaskState(
            TaskId,
            CurrentAttemptId,
            CurrentTurn,
            ExpectedSender,
            DeadlineAt,
            Cancelled)
        {
            IsTerminal = IsTerminal,
            HasFinalClaim = HasFinalClaim,
            AppliedReceiptCount = AppliedReceiptCount,
            LastReceipt = LastReceipt,
        };
        foreach (var pair in receipts)
            clone.receipts[pair.Key] = pair.Value;
        clone.evidence.AddRange(evidence);
        return clone;
    }

    public void MarkCancelled() => Cancelled = true;

    internal void Record(ReceiptDocument receipt)
    {
        receipts[receipt.Key] = receipt;
        LastReceipt = receipt;
        AppliedReceiptCount++;
        if (receipt.IsFinalClaim)
            HasFinalClaim = true;
        if (receipt.IsTerminalCompletion)
            IsTerminal = true;
    }

    internal void RecordEvidence(ReceiptDocument receipt) => evidence.Add(receipt);

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (!SemanticRules.IsUtcZ(value))
            return null;
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var timestamp)
            ? timestamp
            : null;
    }
}
