namespace TaskBaton.Protocol;

internal static class ReceiptSemanticValidator
{
    public static void Validate(
        ReceiptDocument receipt,
        HandoffFrontmatter? handoff,
        string? handoffRoot,
        List<ProtocolDiagnostic> diagnostics)
    {
        RequireField(receipt.HandoffVersion, "handoff_version", diagnostics);
        RequireField(receipt.HandoffId, "handoff_id", diagnostics);
        RequireField(receipt.TaskId, "task_id", diagnostics);
        RequireField(receipt.AttemptId, "attempt_id", diagnostics);
        if (receipt.Turn is null)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptFieldRequired,
                "turn",
                "turn is required."));
        }
        RequireField(receipt.Sender, "sender", diagnostics);
        RequireField(receipt.Status, "status", diagnostics);
        RequireField(receipt.WrittenAt, "written_at", diagnostics);
        RequireField(receipt.IdempotencyKey, "idempotency_key", diagnostics);

        if (receipt.HandoffVersion is not null
            && !string.Equals(receipt.HandoffVersion, "1", StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptVersionUnsupported,
                "handoff_version",
                $"Unsupported handoff_version '{receipt.HandoffVersion}'; this reader supports \"1\"."));
        }

        if (!string.IsNullOrWhiteSpace(receipt.HandoffId) && !SemanticRules.IsUuid(receipt.HandoffId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptFieldInvalid,
                "handoff_id",
                $"handoff_id must be a UUID; found '{receipt.HandoffId}'."));
        }

        if (!string.IsNullOrWhiteSpace(receipt.AttemptId) && !SemanticRules.IsUuid(receipt.AttemptId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptFieldInvalid,
                "attempt_id",
                $"attempt_id must be a UUID; found '{receipt.AttemptId}'."));
        }

        if (handoff is not null)
            ValidateIdentity(receipt, handoff, diagnostics);

        SemanticRules.RequireUtc(
            receipt.WrittenAt,
            "written_at",
            ProtocolDiagnosticCodes.ReceiptTimestampNotUtc,
            diagnostics);

        SemanticRules.ValidateSafePath(
            receipt.Result?.Path,
            "result.path",
            ProtocolDiagnosticCodes.ReceiptPathUnsafe,
            handoffRoot,
            diagnostics);

        if (receipt.Artifacts is not null)
        {
            for (var index = 0; index < receipt.Artifacts.Count; index++)
            {
                var artifact = receipt.Artifacts[index];
                var prefix = $"artifacts[{index}]";
                SemanticRules.RequireNonEmpty(
                    artifact.Path,
                    $"{prefix}.path",
                    ProtocolDiagnosticCodes.ReceiptFieldRequired,
                    diagnostics);
                SemanticRules.RequireNonEmpty(
                    artifact.Role,
                    $"{prefix}.role",
                    ProtocolDiagnosticCodes.ReceiptFieldRequired,
                    diagnostics);
                SemanticRules.ValidateSafePath(
                    artifact.Path,
                    $"{prefix}.path",
                    ProtocolDiagnosticCodes.ReceiptPathUnsafe,
                    handoffRoot,
                    diagnostics);
                SemanticRules.RequireUtc(
                    artifact.ExpiresAt,
                    $"{prefix}.expires_at",
                    ProtocolDiagnosticCodes.ReceiptTimestampNotUtc,
                    diagnostics);
            }
        }

        SemanticRules.ValidateSession(receipt.Session, diagnostics);

        ValidateStatusRules(receipt, diagnostics);
        ValidateOutcomeRules(receipt, diagnostics);
        ValidateReviewRules(receipt, diagnostics);
    }

    private static void ValidateIdentity(
        ReceiptDocument receipt,
        HandoffFrontmatter handoff,
        List<ProtocolDiagnostic> diagnostics)
    {
        Match("handoff_id", handoff.HandoffId, receipt.HandoffId);
        Match("task_id", handoff.TaskId, receipt.TaskId);
        Match("attempt_id", handoff.AttemptId, receipt.AttemptId);
        Match("turn", handoff.Turn?.ToString(System.Globalization.CultureInfo.InvariantCulture), receipt.Turn?.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Match("sender", handoff.To, receipt.Sender);

        void Match(string field, string? expected, string? actual)
        {
            if (expected is null || actual is null)
                return;
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.ReceiptIdMismatch,
                field,
                $"{field} mismatch: receipt has '{actual}', handoff has '{expected}'."));
        }
    }

    private static void ValidateStatusRules(ReceiptDocument receipt, List<ProtocolDiagnostic> diagnostics)
    {
        if (string.Equals(receipt.Status, "done", StringComparison.Ordinal))
        {
            if (receipt.NeedsReply is null)
            {
                diagnostics.Add(ProtocolDiagnostic.Validation(
                    ProtocolDiagnosticCodes.ReceiptNeedsReplyRequired,
                    "needs_reply",
                    "needs_reply is required when status is done."));
                return;
            }

            if (receipt.NeedsReply == true)
            {
                if (receipt.Result is null)
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptResultRequired,
                        "result",
                        "result is required when status is done and needs_reply is true."));
                }
                if (string.IsNullOrWhiteSpace(receipt.NextAction))
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptNextActionRequired,
                        "next_action",
                        "next_action must be a non-empty string when status is done and needs_reply is true."));
                }
            }
            else if (!string.IsNullOrWhiteSpace(receipt.NextAction))
            {
                diagnostics.Add(ProtocolDiagnostic.Validation(
                    ProtocolDiagnosticCodes.ReceiptNextActionNotAllowed,
                    "next_action",
                    "next_action must be null when status is done and needs_reply is false."));
            }
        }

        if (receipt.Outcome is not null
            && !string.Equals(receipt.Status, "done", StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptOutcomeRequiresDone,
                "outcome",
                "outcome may only be present when status is done."));
        }

        if (string.Equals(receipt.Status, "failed", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(receipt.Error?.Code) || string.IsNullOrWhiteSpace(receipt.Error?.Message))
            {
                diagnostics.Add(ProtocolDiagnostic.Validation(
                    ProtocolDiagnosticCodes.ReceiptErrorRequired,
                    "error",
                    "status=failed requires error.code and error.message."));
            }
        }
    }

    private static void ValidateOutcomeRules(ReceiptDocument receipt, List<ProtocolDiagnostic> diagnostics)
    {
        switch (receipt.Outcome)
        {
            case "changes_requested":
                if (!receipt.HasOpenItems)
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptOpenItemsRequired,
                        "open_items",
                        "outcome=changes_requested requires at least one open item."));
                }
                break;

            case "approved":
                if (receipt.HasOpenItems)
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptOpenItemsNotAllowed,
                        "open_items",
                        "outcome=approved must not have open items."));
                }
                if (receipt.NeedsReply == false && receipt.Review?.IsReviewerFinal != true)
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptReviewFinalInvalid,
                        "needs_reply",
                        "outcome=approved without review.final must keep needs_reply=true so the author can accept the delivery."));
                }
                break;

            case "submitted":
                if (receipt.Result is null)
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptResultRequired,
                        "result",
                        "outcome=submitted requires result."));
                }
                break;

            case "accepted":
                if (receipt.Result is null)
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptResultRequired,
                        "result",
                        "outcome=accepted requires result."));
                }
                if (string.IsNullOrWhiteSpace(receipt.Deliverable))
                {
                    diagnostics.Add(ProtocolDiagnostic.Validation(
                        ProtocolDiagnosticCodes.ReceiptDeliverableRequired,
                        "deliverable",
                        "outcome=accepted requires deliverable."));
                }
                break;
        }
    }

    private static void ValidateReviewRules(ReceiptDocument receipt, List<ProtocolDiagnostic> diagnostics)
    {
        if (receipt.Review is null)
            return;

        if (string.Equals(receipt.Review.Decision, "reviewer_final", StringComparison.Ordinal)
            && receipt.Review.Final != true)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptReviewFinalInvalid,
                "review.final",
                "review.decision=reviewer_final requires review.final=true."));
        }

        if (!receipt.Review.IsReviewerFinal)
            return;

        if (!string.Equals(receipt.Outcome, "approved", StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptReviewFinalInvalid,
                "review.final",
                "A reviewer-final approval requires outcome=approved."));
        }

        if (receipt.Result is null)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptResultRequired,
                "result",
                "A reviewer-final approval requires result."));
        }

        if (receipt.HasOpenItems)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptOpenItemsNotAllowed,
                "open_items",
                "A reviewer-final approval must not have open items."));
        }

        if (receipt.NeedsReply == true)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptReviewFinalInvalid,
                "needs_reply",
                "A reviewer-final approval must set needs_reply to false."));
        }

        if (receipt.Review.Decision is not null
            && !string.Equals(receipt.Review.Decision, "reviewer_final", StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptReviewFinalInvalid,
                "review.decision",
                "review.decision must be 'reviewer_final' when review.final is true."));
        }
    }

    private static void RequireField(
        string? value,
        string field,
        List<ProtocolDiagnostic> diagnostics) =>
        SemanticRules.RequireNonEmpty(
            value,
            field,
            ProtocolDiagnosticCodes.ReceiptFieldRequired,
            diagnostics);
}
