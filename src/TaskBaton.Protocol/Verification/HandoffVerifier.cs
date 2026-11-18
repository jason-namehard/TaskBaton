namespace TaskBaton.Protocol;

/// <summary>
/// Verifies a HANDOFF.md, optionally with a receipt and known task/session context.
/// Exit codes are part of the public contract: 0 pass, 2 argument/format, 3 validation,
/// 5 id/state conflict.
/// </summary>
public sealed class HandoffVerifier
{
    private readonly ProtocolSchemaSet schemas;

    public HandoffVerifier(ProtocolSchemaSet? schemas = null) => this.schemas = schemas ?? ProtocolSchemaSet.Embedded;

    public VerificationResult Verify(
        string handoffFile,
        string? receiptFile = null,
        HandoffTaskContext? taskContext = null)
    {
        var arguments = new List<ProtocolDiagnostic>();
        if (string.IsNullOrWhiteSpace(handoffFile))
        {
            arguments.Add(ProtocolDiagnostic.Argument(
                ProtocolDiagnosticCodes.ArgumentInvalid,
                "handoff",
                "A HANDOFF.md path is required."));
            return VerificationResult.FromDiagnostics(arguments);
        }

        if (!File.Exists(handoffFile))
        {
            arguments.Add(ProtocolDiagnostic.Argument(
                ProtocolDiagnosticCodes.FileNotFound,
                "handoff",
                $"HANDOFF.md not found: {handoffFile}"));
            return VerificationResult.FromDiagnostics(arguments);
        }

        HandoffDocument handoff;
        try
        {
            handoff = HandoffReader.ReadFile(handoffFile);
        }
        catch (ProtocolFormatException ex)
        {
            arguments.Add(ProtocolDiagnostic.Argument(ex.Code, ex.Field, ex.Message));
            return VerificationResult.FromDiagnostics(arguments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            arguments.Add(ProtocolDiagnostic.Argument(
                ProtocolDiagnosticCodes.ArgumentInvalid,
                "handoff",
                $"Could not read HANDOFF.md: {ex.Message}"));
            return VerificationResult.FromDiagnostics(arguments);
        }

        ReceiptDocument? receipt = null;
        if (receiptFile is not null)
        {
            if (string.IsNullOrWhiteSpace(receiptFile))
            {
                arguments.Add(ProtocolDiagnostic.Argument(
                    ProtocolDiagnosticCodes.ArgumentInvalid,
                    "receipt",
                    "The receipt path was empty."));
                return VerificationResult.FromDiagnostics(arguments);
            }

            if (!File.Exists(receiptFile))
            {
                arguments.Add(ProtocolDiagnostic.Argument(
                    ProtocolDiagnosticCodes.FileNotFound,
                    "receipt",
                    $"Receipt not found: {receiptFile}"));
                return VerificationResult.FromDiagnostics(arguments);
            }

            try
            {
                receipt = ReceiptDocument.LoadFile(receiptFile);
            }
            catch (ProtocolFormatException ex)
            {
                arguments.Add(ProtocolDiagnostic.Argument(ex.Code, ex.Field, ex.Message));
                return VerificationResult.FromDiagnostics(arguments);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                arguments.Add(ProtocolDiagnostic.Argument(
                    ProtocolDiagnosticCodes.ArgumentInvalid,
                    "receipt",
                    $"Could not read receipt.json: {ex.Message}"));
                return VerificationResult.FromDiagnostics(arguments);
            }
        }

        var root = Path.GetDirectoryName(Path.GetFullPath(handoffFile));
        return VerifyCore(handoff, receipt, taskContext, root);
    }

    public VerificationResult Verify(
        HandoffDocument handoff,
        ReceiptDocument? receipt = null,
        HandoffTaskContext? taskContext = null) =>
        VerifyCore(handoff, receipt, taskContext, handoffRoot: null);

    private VerificationResult VerifyCore(
        HandoffDocument handoff,
        ReceiptDocument? receipt,
        HandoffTaskContext? taskContext,
        string? handoffRoot)
    {
        var diagnostics = new List<ProtocolDiagnostic>();

        diagnostics.AddRange(schemas.ValidateHandoff(handoff.FrontmatterJson));
        HandoffSemanticValidator.Validate(handoff, handoffRoot, diagnostics);

        ReceiptApplyResult? stateResult = null;
        if (receipt is not null)
        {
            diagnostics.AddRange(schemas.ValidateReceipt(receipt.Raw));
            ReceiptSemanticValidator.Validate(receipt, handoff.Frontmatter, handoffRoot, diagnostics);

            var state = (taskContext?.State
                         ?? HandoffTaskState.FromHandoff(handoff.Frontmatter, taskContext?.DeadlineAt)).Clone();
            if (taskContext?.Cancelled == true)
                state.MarkCancelled();

            // A receipt that already failed validation must not be reported as applied.
            if (!diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                stateResult = new HandoffTaskStateMachine(state).Evaluate(receipt, taskContext?.Now);
            if (stateResult is not null) diagnostics.AddRange(stateResult.Diagnostics);
        }

        if (taskContext?.SessionStore is not null && handoff.Frontmatter.Session is not null)
        {
            diagnostics.AddRange(
                SessionBindingVerifier.VerifyBeforeResume(handoff.Frontmatter.Session, taskContext.SessionStore).Diagnostics);
        }

        var deduplicated = Diagnostics.Deduplicate(diagnostics);
        return new VerificationResult(
            deduplicated,
            ProtocolExitCodes.Classify(deduplicated),
            handoff,
            receipt,
            stateResult);
    }
}
