using System.Text.Json.Nodes;
using Json.Schema;

namespace TaskBaton.Protocol;

/// <summary>
/// The handoff/1 JSON Schemas. The default set is embedded in the assembly so a CLI can verify
/// files without locating protocol/schemas at runtime. Tests use <see cref="FromDirectory"/> to
/// validate the checked-in schema files directly.
/// </summary>
public sealed class ProtocolSchemaSet
{
    private const string HandoffResource = "TaskBaton.Protocol.Schemas.handoff.schema.json";
    private const string ReceiptResource = "TaskBaton.Protocol.Schemas.receipt.schema.json";

    private ProtocolSchemaSet(JsonSchema handoffSchema, JsonSchema receiptSchema)
    {
        HandoffSchema = handoffSchema;
        ReceiptSchema = receiptSchema;
    }

    public JsonSchema HandoffSchema { get; }

    public JsonSchema ReceiptSchema { get; }

    public static ProtocolSchemaSet Embedded { get; } = CreateEmbedded();

    public static ProtocolSchemaSet FromDirectory(string directory)
    {
        var handoffPath = Path.Combine(directory, "handoff.schema.json");
        var receiptPath = Path.Combine(directory, "receipt.schema.json");
        foreach (var path in new[] { handoffPath, receiptPath })
        {
            if (!File.Exists(path))
            {
                throw new ProtocolFormatException(
                    ProtocolDiagnosticCodes.SchemaUnavailable,
                    $"Protocol schema not found: {path}");
            }
        }

        try
        {
            var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
            return new ProtocolSchemaSet(
                JsonSchema.FromFile(handoffPath, options),
                JsonSchema.FromFile(receiptPath, options));
        }
        catch (Exception ex) when (ex is not ProtocolFormatException)
        {
            throw new ProtocolFormatException(
                ProtocolDiagnosticCodes.SchemaUnavailable,
                $"Could not load protocol schemas from '{directory}': {ex.Message}",
                innerException: ex);
        }
    }

    public IReadOnlyList<ProtocolDiagnostic> ValidateHandoff(JsonNode? frontmatter) =>
        SchemaValidation.Validate(frontmatter, HandoffSchema, receipt: false);

    public IReadOnlyList<ProtocolDiagnostic> ValidateReceipt(JsonNode? receipt) =>
        SchemaValidation.Validate(receipt, ReceiptSchema, receipt: true);

    private static ProtocolSchemaSet CreateEmbedded()
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        return new ProtocolSchemaSet(
            JsonSchema.FromText(ReadResource(HandoffResource), options),
            JsonSchema.FromText(ReadResource(ReceiptResource), options));
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(ProtocolSchemaSet).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded protocol schema '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
