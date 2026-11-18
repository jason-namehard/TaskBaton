namespace TaskBaton.Protocol;

/// <summary>
/// L4 rules. A session binding is exact: no "latest", no fallback to the most recent conversation,
/// and a resume response carrying a different id is rejected.
/// </summary>
public static class SessionBindingVerifier
{
    public static VerificationResult VerifyBeforeResume(SessionBinding? binding, ISessionStore? store = null)
    {
        var diagnostics = new List<ProtocolDiagnostic>();
        if (binding is null)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffSessionExactRequired,
                "session",
                "An exact session binding is required before resuming."));
            return VerificationResult.FromDiagnostics(diagnostics);
        }

        if (string.IsNullOrWhiteSpace(binding.Host))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffFieldRequired,
                "session.host",
                "session.host is required."));
        }

        if (!SemanticRules.IsExactSessionId(binding.SessionId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffSessionExactRequired,
                "session.session_id",
                "session.session_id must be an exact id; latest/recent/* and empty values are not allowed."));
            return VerificationResult.FromDiagnostics(diagnostics);
        }

        if (store is not null)
        {
            var lookup = store.LookupExact(binding.Host, binding.SessionId);
            if (!lookup.Exists)
            {
                diagnostics.Add(ProtocolDiagnostic.StateConflict(
                    ProtocolDiagnosticCodes.HandoffSessionNotFound,
                    "session.session_id",
                    $"Session '{binding.SessionId}' does not exist in host '{binding.Host}'."));
            }
            else if (lookup.ActualSessionId is not null
                     && !string.Equals(lookup.ActualSessionId, binding.SessionId, StringComparison.Ordinal))
            {
                diagnostics.Add(ProtocolDiagnostic.StateConflict(
                    ProtocolDiagnosticCodes.HandoffSessionMismatch,
                    "session.session_id",
                    $"Host resolved session '{lookup.ActualSessionId}' but the handoff requested '{binding.SessionId}'."));
            }
        }

        return VerificationResult.FromDiagnostics(diagnostics);
    }

    public static VerificationResult VerifyResume(SessionBinding binding, ISessionResumer resumer)
    {
        var before = VerifyBeforeResume(binding);
        if (before.HasErrors)
            return before;

        var resumed = resumer.Resume(binding);
        if (!resumed.Resumed)
        {
            return VerificationResult.FromDiagnostics(
            [
                ProtocolDiagnostic.StateConflict(
                    ProtocolDiagnosticCodes.HandoffSessionNotFound,
                    "session.session_id",
                    $"Host '{binding.Host}' could not resume session '{binding.SessionId}'."),
            ]);
        }

        return VerifyResumeResult(binding, resumed.SessionId);
    }

    public static VerificationResult VerifyResumeResult(SessionBinding binding, string? actualSessionId)
    {
        var diagnostics = new List<ProtocolDiagnostic>();
        if (!SemanticRules.IsExactSessionId(binding.SessionId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffSessionExactRequired,
                "session.session_id",
                "session.session_id must be an exact id; latest/recent/* and empty values are not allowed."));
            return VerificationResult.FromDiagnostics(diagnostics);
        }

        if (!string.Equals(actualSessionId, binding.SessionId, StringComparison.Ordinal))
        {
            diagnostics.Add(ProtocolDiagnostic.StateConflict(
                ProtocolDiagnosticCodes.HandoffSessionMismatch,
                "session.session_id",
                $"Resume returned session '{actualSessionId ?? "(null)"}' but the handoff requested '{binding.SessionId}'."));
        }

        return VerificationResult.FromDiagnostics(diagnostics);
    }
}
