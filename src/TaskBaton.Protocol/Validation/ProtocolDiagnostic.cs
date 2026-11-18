namespace TaskBaton.Protocol;

/// <summary>How serious a diagnostic is. Informational diagnostics do not change the exit code.</summary>
public enum DiagnosticSeverity
{
    Info,
    Error,
}

/// <summary>The exit-code family a diagnostic belongs to.</summary>
public enum DiagnosticClass
{
    Argument,
    Validation,
    StateConflict,
}

/// <summary>
/// A structured validation diagnostic. <see cref="Code"/> is stable and safe for scripts;
/// <see cref="Field"/> is a JSON/YAML location such as <c>task_id</c> or <c>session.session_id</c>.
/// </summary>
public sealed record ProtocolDiagnostic(
    string Code,
    string? Field,
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    DiagnosticClass Class = DiagnosticClass.Validation)
{
    public static ProtocolDiagnostic Info(string code, string? field, string message) =>
        new(code, field, message, DiagnosticSeverity.Info, DiagnosticClass.Validation);

    public static ProtocolDiagnostic Argument(string code, string? field, string message) =>
        new(code, field, message, DiagnosticSeverity.Error, DiagnosticClass.Argument);

    public static ProtocolDiagnostic Validation(string code, string? field, string message) =>
        new(code, field, message, DiagnosticSeverity.Error, DiagnosticClass.Validation);

    public static ProtocolDiagnostic StateConflict(string code, string? field, string message) =>
        new(code, field, message, DiagnosticSeverity.Error, DiagnosticClass.StateConflict);
}
