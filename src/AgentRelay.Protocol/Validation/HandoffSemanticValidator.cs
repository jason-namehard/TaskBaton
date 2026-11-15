namespace AgentRelay.Protocol;

internal static class HandoffSemanticValidator
{
    public static void Validate(
        HandoffDocument document,
        string? handoffRoot,
        List<ProtocolDiagnostic> diagnostics)
    {
        var frontmatter = document.Frontmatter;

        RequireField(frontmatter.HandoffVersion, "handoff_version", diagnostics);
        RequireField(frontmatter.HandoffId, "handoff_id", diagnostics);
        RequireField(frontmatter.TaskId, "task_id", diagnostics);
        RequireField(frontmatter.AttemptId, "attempt_id", diagnostics);
        if (frontmatter.Turn is null)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffFieldRequired,
                "turn",
                "turn is required."));
        }
        RequireField(frontmatter.From, "from", diagnostics);
        RequireField(frontmatter.To, "to", diagnostics);
        RequireField(frontmatter.CreatedAt, "created_at", diagnostics);
        RequireField(frontmatter.IdempotencyKey, "idempotency_key", diagnostics);
        RequireField(frontmatter.ReceiptPath, "receipt_path", diagnostics);

        if (frontmatter.HandoffVersion is not null
            && !string.Equals(frontmatter.HandoffVersion, "1", StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffVersionUnsupported,
                "handoff_version",
                $"Unsupported handoff_version '{frontmatter.HandoffVersion}'; this reader supports \"1\"."));
        }

        if (!string.IsNullOrWhiteSpace(frontmatter.HandoffId) && !SemanticRules.IsUuid(frontmatter.HandoffId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffFieldInvalid,
                "handoff_id",
                $"handoff_id must be a UUID; found '{frontmatter.HandoffId}'."));
        }

        if (!string.IsNullOrWhiteSpace(frontmatter.AttemptId) && !SemanticRules.IsUuid(frontmatter.AttemptId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffFieldInvalid,
                "attempt_id",
                $"attempt_id must be a UUID; found '{frontmatter.AttemptId}'."));
        }

        if (!string.IsNullOrWhiteSpace(frontmatter.From)
            && !string.IsNullOrWhiteSpace(frontmatter.To)
            && string.Equals(frontmatter.From, frontmatter.To, StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffFromEqualsTo,
                "to",
                $"from and to must differ; both are '{frontmatter.From}'."));
        }

        SemanticRules.RequireUtc(
            frontmatter.CreatedAt,
            "created_at",
            ProtocolDiagnosticCodes.HandoffTimestampNotUtc,
            diagnostics);
        SemanticRules.RequireUtc(
            frontmatter.DeadlineAt,
            "deadline_at",
            ProtocolDiagnosticCodes.HandoffTimestampNotUtc,
            diagnostics);

        SemanticRules.ValidateSafePath(
            frontmatter.ReceiptPath,
            "receipt_path",
            ProtocolDiagnosticCodes.HandoffPathUnsafe,
            handoffRoot,
            diagnostics);
        SemanticRules.ValidateSafePath(
            frontmatter.ReplyTo,
            "reply_to",
            ProtocolDiagnosticCodes.HandoffPathUnsafe,
            handoffRoot,
            diagnostics);

        SemanticRules.ValidateSession(frontmatter.Session, diagnostics);

        if (frontmatter.Artifacts is not null)
        {
            for (var index = 0; index < frontmatter.Artifacts.Count; index++)
            {
                var artifact = frontmatter.Artifacts[index];
                var prefix = $"artifacts[{index}]";
                SemanticRules.RequireNonEmpty(
                    artifact.Path,
                    $"{prefix}.path",
                    ProtocolDiagnosticCodes.HandoffFieldRequired,
                    diagnostics);
                SemanticRules.RequireNonEmpty(
                    artifact.Role,
                    $"{prefix}.role",
                    ProtocolDiagnosticCodes.HandoffFieldRequired,
                    diagnostics);
                SemanticRules.ValidateSafePath(
                    artifact.Path,
                    $"{prefix}.path",
                    ProtocolDiagnosticCodes.HandoffPathUnsafe,
                    handoffRoot,
                    diagnostics);
                SemanticRules.RequireUtc(
                    artifact.ExpiresAt,
                    $"{prefix}.expires_at",
                    ProtocolDiagnosticCodes.HandoffTimestampNotUtc,
                    diagnostics);
            }
        }

        foreach (var section in HandoffDocument.RequiredSectionNames)
        {
            if (!document.HasSection(section))
            {
                diagnostics.Add(ProtocolDiagnostic.Validation(
                    ProtocolDiagnosticCodes.HandoffSectionRequired,
                    $"body.{section}",
                    $"The Markdown body must contain a '## {section}' section."));
            }
        }
    }

    private static void RequireField(
        string? value,
        string field,
        List<ProtocolDiagnostic> diagnostics) =>
        SemanticRules.RequireNonEmpty(
            value,
            field,
            ProtocolDiagnosticCodes.HandoffFieldRequired,
            diagnostics);
}
