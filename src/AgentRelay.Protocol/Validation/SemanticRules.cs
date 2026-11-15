using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentRelay.Protocol;

internal static class SemanticRules
{
    private static readonly Regex UtcTimestampPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,9})?Z$",
        RegexOptions.CultureInvariant);

    public static bool IsUtcZ(string? value)
    {
        if (value is null || !UtcTimestampPattern.IsMatch(value))
            return false;
        return DateTimeOffset.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var timestamp)
               && timestamp.Offset == TimeSpan.Zero;
    }

    public static bool IsExactSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;
        if (!string.Equals(sessionId, sessionId.Trim(), StringComparison.Ordinal))
            return false;

        return sessionId.ToLowerInvariant() is not ("latest" or "recent" or "*");
    }

    public static void RequireNonEmpty(
        string? value,
        string field,
        string requiredCode,
        List<ProtocolDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
            diagnostics.Add(ProtocolDiagnostic.Validation(requiredCode, field, $"{field} is required."));
    }

    public static void RequireUtc(
        string? value,
        string field,
        string code,
        List<ProtocolDiagnostic> diagnostics)
    {
        if (value is not null && !IsUtcZ(value))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                code,
                field,
                $"{field} must be an RFC 3339 UTC timestamp ending in Z; found '{value}'."));
        }
    }

    public static void ValidateSession(SessionBinding? session, List<ProtocolDiagnostic> diagnostics)
    {
        if (session is null)
            return;

        if (string.IsNullOrWhiteSpace(session.Host))
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffFieldRequired,
                "session.host",
                "session.host is required."));

        if (!IsExactSessionId(session.SessionId))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffSessionExactRequired,
                "session.session_id",
                "session.session_id must be an exact id; latest/recent/* and empty values are not allowed."));
        }
    }

    public static void ValidateSafePath(
        string? path,
        string field,
        string code,
        string? handoffRoot,
        List<ProtocolDiagnostic> diagnostics)
    {
        if (path is null)
            return;
        if (!PathSafety.IsSafeRelativePath(path))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                code,
                field,
                $"{field} must be a relative path using forward slashes and must not contain '.' or '..' segments; found '{path}'."));
            return;
        }

        if (handoffRoot is not null && !PathSafety.IsContainedInRoot(handoffRoot, path, out _))
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                code,
                field,
                $"{field} escapes the handoff directory: '{path}'."));
        }
    }

    public static bool IsUuid(string? value) => value is not null && Guid.TryParse(value, out _);
}
