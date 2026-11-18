namespace TaskBaton.Protocol;

/// <summary>Process exit codes a CLI should return after calling the verifier.</summary>
public static class ProtocolExitCodes
{
    public const int Pass = 0;
    public const int ArgumentOrFormat = 2;
    public const int ValidationFailed = 3;
    public const int IdOrStateConflict = 5;

    public static int Classify(IEnumerable<ProtocolDiagnostic> diagnostics)
    {
        var list = diagnostics as IReadOnlyCollection<ProtocolDiagnostic> ?? diagnostics.ToArray();
        if (list.Any(d => d.Class == DiagnosticClass.Argument))
            return ArgumentOrFormat;
        if (list.Any(d => d.Class == DiagnosticClass.StateConflict))
            return IdOrStateConflict;
        if (list.Any(d => d.Severity == DiagnosticSeverity.Error))
            return ValidationFailed;
        return Pass;
    }
}
