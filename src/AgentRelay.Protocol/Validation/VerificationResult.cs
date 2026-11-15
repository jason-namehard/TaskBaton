namespace AgentRelay.Protocol;

/// <summary>
/// Result returned by <see cref="HandoffVerifier"/>, <see cref="SessionBindingVerifier"/>, and
/// the state machine helpers. <see cref="ExitCode"/> is already classified: 0 pass, 2 argument/format,
/// 3 validation, 5 id/state conflict.
/// </summary>
public sealed record VerificationResult(
    IReadOnlyList<ProtocolDiagnostic> Diagnostics,
    int ExitCode,
    HandoffDocument? Handoff = null,
    ReceiptDocument? Receipt = null,
    ReceiptApplyResult? ReceiptState = null)
{
    public bool Passed => ExitCode == ProtocolExitCodes.Pass;

    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public bool HasCode(string code) => Diagnostics.Any(d => string.Equals(d.Code, code, StringComparison.Ordinal));

    public static VerificationResult FromDiagnostics(
        IEnumerable<ProtocolDiagnostic> diagnostics,
        HandoffDocument? handoff = null,
        ReceiptDocument? receipt = null,
        ReceiptApplyResult? receiptState = null)
    {
        var list = diagnostics as IReadOnlyList<ProtocolDiagnostic> ?? diagnostics.ToArray();
        return new VerificationResult(list, ProtocolExitCodes.Classify(list), handoff, receipt, receiptState);
    }
}
