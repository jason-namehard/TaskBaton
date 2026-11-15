namespace AgentRelay.Protocol;

public enum ReceiptApplyKind
{
    NotApplicable,
    Applied,
    Duplicate,
    LateAccepted,
    OldAttempt,
    IdMismatch,
    IdempotencyConflict,
    ConflictingFinal,
    Cancelled,
}

/// <summary>The classification of one receipt against the current task state.</summary>
public sealed record ReceiptApplyResult(
    ReceiptApplyKind Kind,
    IReadOnlyList<ProtocolDiagnostic> Diagnostics,
    int ExitCode,
    bool StateChanged)
{
    public bool Passed => ExitCode == ProtocolExitCodes.Pass;

    public bool HasCode(string code) => Diagnostics.Any(d => string.Equals(d.Code, code, StringComparison.Ordinal));

    public static ReceiptApplyResult From(
        ReceiptApplyKind kind,
        IEnumerable<ProtocolDiagnostic> diagnostics,
        bool stateChanged)
    {
        var list = diagnostics as IReadOnlyList<ProtocolDiagnostic> ?? diagnostics.ToArray();
        return new ReceiptApplyResult(kind, list, ProtocolExitCodes.Classify(list), stateChanged);
    }
}
