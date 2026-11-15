using System.Globalization;

namespace AgentRelay.Protocol;

/// <summary>
/// L3 rules: duplicate idempotent receipt, idempotency conflict, late acceptance, old attempt
/// evidence, conflicting final claims, and cancellation. <see cref="Evaluate"/> is non-mutating;
/// <see cref="Apply"/> advances the supplied state.
/// </summary>
public sealed class HandoffTaskStateMachine
{
    public HandoffTaskStateMachine(HandoffTaskState state) => State = state;

    public HandoffTaskState State { get; }

    public ReceiptApplyResult Evaluate(ReceiptDocument receipt, DateTimeOffset? now = null)
    {
        _ = now;
        var diagnostics = new List<ProtocolDiagnostic>();

        if (State.Cancelled)
        {
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptCancelled,
                null,
                "The task is cancelled; the receipt is retained only as evidence."));
            return ReceiptApplyResult.From(ReceiptApplyKind.Cancelled, diagnostics, stateChanged: false);
        }

        if (string.IsNullOrWhiteSpace(State.TaskId)
            || string.IsNullOrWhiteSpace(State.CurrentAttemptId)
            || !receipt.Key.IsComplete)
        {
            return ReceiptApplyResult.From(ReceiptApplyKind.NotApplicable, [], stateChanged: false);
        }

        if (!string.Equals(receipt.TaskId, State.TaskId, StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptIdMismatch,
                "task_id",
                $"task_id mismatch: receipt has '{receipt.TaskId}', task has '{State.TaskId}'."));
            return ReceiptApplyResult.From(ReceiptApplyKind.IdMismatch, diagnostics, stateChanged: false);
        }

        if (!string.Equals(receipt.AttemptId, State.CurrentAttemptId, StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptOldAttempt,
                "attempt_id",
                $"Receipt is for attempt '{receipt.AttemptId}' but the current attempt is '{State.CurrentAttemptId}'."));
            return ReceiptApplyResult.From(ReceiptApplyKind.OldAttempt, diagnostics, stateChanged: false);
        }

        if (receipt.Turn != State.CurrentTurn)
        {
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptIdMismatch,
                "turn",
                $"turn mismatch: receipt has '{receipt.Turn}', task has '{State.CurrentTurn}'."));
            return ReceiptApplyResult.From(ReceiptApplyKind.IdMismatch, diagnostics, stateChanged: false);
        }

        if (!string.IsNullOrWhiteSpace(State.ExpectedSender)
            && !string.Equals(receipt.Sender, State.ExpectedSender, StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptIdMismatch,
                "sender",
                $"sender mismatch: receipt has '{receipt.Sender}', handoff expects '{State.ExpectedSender}'."));
            return ReceiptApplyResult.From(ReceiptApplyKind.IdMismatch, diagnostics, stateChanged: false);
        }

        if (State.Receipts.TryGetValue(receipt.Key, out var existing))
        {
            if (existing.ContentEquals(receipt))
            {
                diagnostics.Add(ProtocolDiagnostic.Info(
                    ProtocolDiagnosticCodes.ReceiptDuplicateIdempotent,
                    "idempotency_key",
                    "Duplicate receipt with the same key and content; returning the original result without a second transition."));
                return ReceiptApplyResult.From(ReceiptApplyKind.Duplicate, diagnostics, stateChanged: false);
            }

            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptIdempotencyConflict,
                "idempotency_key",
                "The idempotency key was reused with different receipt content."));
            return ReceiptApplyResult.From(ReceiptApplyKind.IdempotencyConflict, diagnostics, stateChanged: false);
        }

        if (State.HasFinalClaim && receipt.IsFinalClaim)
        {
            var priorFinal = State.Receipts.Values.FirstOrDefault(candidate =>
                candidate.IsFinalClaim
                && string.Equals(candidate.AttemptId, receipt.AttemptId, StringComparison.Ordinal));
            if (priorFinal is not null && !priorFinal.ContentEquals(receipt))
            {
                diagnostics.Add(ProtocolDiagnostic.StateConflict(
                    ProtocolDiagnosticCodes.ReceiptConflictingFinal,
                    "status",
                    "A final claim already exists for this attempt; a different final receipt cannot overwrite it."));
                return ReceiptApplyResult.From(ReceiptApplyKind.ConflictingFinal, diagnostics, stateChanged: false);
            }
        }

        var late = IsLate(receipt);
        if (late)
        {
            diagnostics.Add(ProtocolDiagnostic.Info(
                ProtocolDiagnosticCodes.ReceiptLateAccepted,
                "written_at",
                "Receipt was written after deadline_at; accepted and recorded as late."));
        }

        return ReceiptApplyResult.From(
            late ? ReceiptApplyKind.LateAccepted : ReceiptApplyKind.Applied,
            diagnostics,
            stateChanged: true);
    }

    public ReceiptApplyResult Apply(ReceiptDocument receipt, DateTimeOffset? now = null)
    {
        var result = Evaluate(receipt, now);
        switch (result.Kind)
        {
            case ReceiptApplyKind.Applied:
            case ReceiptApplyKind.LateAccepted:
                State.Record(receipt);
                break;
            case ReceiptApplyKind.OldAttempt:
            case ReceiptApplyKind.Cancelled:
                State.RecordEvidence(receipt);
                break;
        }
        return result;
    }

    private bool IsLate(ReceiptDocument receipt)
    {
        if (State.DeadlineAt is not { } deadline || !SemanticRules.IsUtcZ(receipt.WrittenAt))
            return false;
        return DateTimeOffset.TryParse(
                   receipt.WrittenAt,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var writtenAt)
               && writtenAt > deadline;
    }
}
