using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;

namespace AgentRelay.Protocol;

internal static class SchemaValidation
{
    private static readonly Regex QuotedValue = new("[\"']([^\"']+)[\"']", RegexOptions.CultureInvariant);

    public static IReadOnlyList<ProtocolDiagnostic> Validate(JsonNode? instance, JsonSchema schema, bool receipt)
    {
        var options = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        };

        var element = JsonSerializer.SerializeToElement(instance ?? new JsonObject());
        var results = schema.Evaluate(element, options);
        if (results.IsValid)
            return [];

        var diagnostics = new List<ProtocolDiagnostic>();
        Collect(results, receipt, diagnostics);
        if (diagnostics.Count == 0)
        {
            diagnostics.Add(ProtocolDiagnostic.Validation(
                receipt ? ProtocolDiagnosticCodes.ReceiptSchemaInvalid : ProtocolDiagnosticCodes.HandoffSchemaInvalid,
                null,
                "JSON Schema validation failed."));
        }

        return Diagnostics.Deduplicate(diagnostics);
    }

    private static void Collect(
        EvaluationResults result,
        bool receipt,
        List<ProtocolDiagnostic> diagnostics)
    {
        if (result.IsValid)
            return;

        var field = PointerToField(result.InstanceLocation.ToString());
        if (result.Errors is { Count: > 0 })
        {
            foreach (var error in result.Errors)
                diagnostics.Add(MapError(error.Key, error.Value, field, receipt));
        }

        if (result.Details is { Count: > 0 })
        {
            foreach (var detail in result.Details)
            {
                // if/then/else/not sub-results describe the conditional's internals, not independent user errors.
                if (FromConditionalBranch(detail)) continue;
                Collect(detail, receipt, diagnostics);
            }
        }
    }

    private static bool FromConditionalBranch(EvaluationResults result)
    {
        var path = result.EvaluationPath.ToString();
        if (path.Length == 0) return false;
        return path.TrimStart('/').Split('/').Any(segment => segment is "if" or "then" or "else" or "not");
    }

    private static ProtocolDiagnostic MapError(
        string keyword,
        string message,
        string? field,
        bool receipt)
    {
        var prefix = receipt ? "receipt" : "handoff";
        if (string.Equals(keyword, "required", StringComparison.Ordinal))
        {
            var missing = MissingProperty(message);
            var missingField = CombineField(field, missing);
            return ProtocolDiagnostic.Validation(
                receipt ? ProtocolDiagnosticCodes.ReceiptFieldRequired : ProtocolDiagnosticCodes.HandoffFieldRequired,
                missingField,
                message);
        }

        if (string.Equals(keyword, "const", StringComparison.Ordinal)
            && string.Equals(field, "handoff_version", StringComparison.Ordinal))
        {
            return ProtocolDiagnostic.Validation(
                receipt ? ProtocolDiagnosticCodes.ReceiptVersionUnsupported : ProtocolDiagnosticCodes.HandoffVersionUnsupported,
                field,
                message);
        }

        if (keyword is "format" or "pattern")
        {
            if (IsTimestampField(field))
            {
                return ProtocolDiagnostic.Validation(
                    receipt ? ProtocolDiagnosticCodes.ReceiptTimestampNotUtc : ProtocolDiagnosticCodes.HandoffTimestampNotUtc,
                    field,
                    message);
            }
            if (IsPathField(field))
            {
                return ProtocolDiagnostic.Validation(
                    receipt ? ProtocolDiagnosticCodes.ReceiptPathUnsafe : ProtocolDiagnosticCodes.HandoffPathUnsafe,
                    field,
                    message);
            }
            if (IsSessionField(field))
            {
                return ProtocolDiagnostic.Validation(
                    ProtocolDiagnosticCodes.HandoffSessionExactRequired,
                    field,
                    message);
            }
        }

        if (IsSessionField(field))
        {
            return ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.HandoffSessionExactRequired,
                field,
                message);
        }

        if (keyword == "maxItems" && string.Equals(field, "open_items", StringComparison.Ordinal))
        {
            return ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptOpenItemsNotAllowed,
                field,
                message);
        }

        if (keyword == "minItems" && string.Equals(field, "open_items", StringComparison.Ordinal))
        {
            return ProtocolDiagnostic.Validation(
                ProtocolDiagnosticCodes.ReceiptOpenItemsRequired,
                field,
                message);
        }

        return ProtocolDiagnostic.Validation(
            $"{prefix}.schema_invalid",
            field,
            message);
    }

    private static string? MissingProperty(string message)
    {
        var match = QuotedValue.Match(message);
        if (match.Success)
            return match.Groups[1].Value;

        match = Regex.Match(message, @"\[(.*?)\]", RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;

        var first = match.Groups[1].Value.Split(',')[0].Trim();
        return first.Trim('"', '\'');
    }

    private static string CombineField(string? parent, string? child)
    {
        if (string.IsNullOrWhiteSpace(parent))
            return string.IsNullOrWhiteSpace(child) ? "(root)" : child;
        return string.IsNullOrWhiteSpace(child) ? parent : $"{parent}.{child}";
    }

    private static bool IsTimestampField(string? field) =>
        field is not null
        && (field.EndsWith("_at", StringComparison.Ordinal)
            || field.EndsWith(".time", StringComparison.Ordinal));

    private static bool IsPathField(string? field) =>
        field is not null
        && (field.EndsWith(".path", StringComparison.Ordinal)
            || field is "path"
            || field.EndsWith("path", StringComparison.Ordinal)
            || field.EndsWith("reply_to", StringComparison.Ordinal));

    private static bool IsSessionField(string? field) =>
        field is not null
        && (field.EndsWith("session_id", StringComparison.Ordinal)
            || string.Equals(field, "session", StringComparison.Ordinal));

    private static string? PointerToField(string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer == "/")
            return null;

        var tokens = pointer.TrimStart('/').Split('/');
        var builder = new StringBuilder();
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (index > 0 && int.TryParse(token, out _))
            {
                builder.Append('[').Append(token).Append(']');
            }
            else
            {
                if (builder.Length > 0)
                    builder.Append('.');
                builder.Append(token);
            }
        }

        return builder.ToString();
    }
}

internal static class Diagnostics
{
    public static List<ProtocolDiagnostic> Deduplicate(IEnumerable<ProtocolDiagnostic> source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ProtocolDiagnostic>();
        foreach (var diagnostic in source)
        {
            var key = $"{diagnostic.Code}|{diagnostic.Field}|{diagnostic.Class}|{diagnostic.Severity}";
            if (seen.Add(key))
                result.Add(diagnostic);
        }
        return result;
    }
}
