using System.Text.Json.Nodes;

namespace TaskBaton.Protocol;

/// <summary>
/// The normative handoff frontmatter. Timestamps stay strings so a parser cannot silently turn
/// them into local datetimes. Unknown keys are preserved in <see cref="Extra"/>.
/// </summary>
public sealed class HandoffFrontmatter
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "handoff_version",
        "handoff_id",
        "task_id",
        "attempt_id",
        "turn",
        "from",
        "to",
        "created_at",
        "idempotency_key",
        "receipt_path",
        "reply_to",
        "deadline_at",
        "session",
        "artifacts",
        "labels",
    };

    public string? HandoffVersion { get; init; }

    public string? HandoffId { get; init; }

    public string? TaskId { get; init; }

    public string? AttemptId { get; init; }

    public int? Turn { get; init; }

    public string? From { get; init; }

    public string? To { get; init; }

    public string? CreatedAt { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? ReceiptPath { get; init; }

    public string? ReplyTo { get; init; }

    public string? DeadlineAt { get; init; }

    public SessionBinding? Session { get; init; }

    public IReadOnlyList<HandoffArtifact>? Artifacts { get; init; }

    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>Unknown keys from the source document, preserved for round-tripping.</summary>
    public JsonObject Extra { get; init; } = new();

    public static HandoffFrontmatter FromJson(JsonObject obj)
    {
        var extra = new JsonObject();
        foreach (var pair in obj)
        {
            if (!KnownKeys.Contains(pair.Key))
                extra[pair.Key] = pair.Value?.DeepClone();
        }

        return new HandoffFrontmatter
        {
            HandoffVersion = JsonRead.String(obj, "handoff_version"),
            HandoffId = JsonRead.String(obj, "handoff_id"),
            TaskId = JsonRead.String(obj, "task_id"),
            AttemptId = JsonRead.String(obj, "attempt_id"),
            Turn = JsonRead.Int(obj, "turn"),
            From = JsonRead.String(obj, "from"),
            To = JsonRead.String(obj, "to"),
            CreatedAt = JsonRead.String(obj, "created_at"),
            IdempotencyKey = JsonRead.String(obj, "idempotency_key"),
            ReceiptPath = JsonRead.String(obj, "receipt_path"),
            ReplyTo = JsonRead.String(obj, "reply_to"),
            DeadlineAt = JsonRead.String(obj, "deadline_at"),
            Session = JsonRead.Object(obj, "session") is { } session ? SessionBinding.FromJson(session) : null,
            Artifacts = JsonRead.Array(obj, "artifacts") is { } artifacts
                ? artifacts.OfType<JsonObject>().Select(HandoffArtifact.FromJson).ToArray()
                : null,
            Labels = ReadLabels(JsonRead.Object(obj, "labels")),
            Extra = extra,
        };
    }

    public JsonObject ToJsonObject()
    {
        var result = Extra.DeepClone().AsObject();
        AddString(result, "handoff_version", HandoffVersion);
        AddString(result, "handoff_id", HandoffId);
        AddString(result, "task_id", TaskId);
        AddString(result, "attempt_id", AttemptId);
        if (Turn is not null)
            result["turn"] = Turn.Value;
        AddString(result, "from", From);
        AddString(result, "to", To);
        AddString(result, "created_at", CreatedAt);
        AddString(result, "idempotency_key", IdempotencyKey);
        AddString(result, "receipt_path", ReceiptPath);
        AddString(result, "reply_to", ReplyTo);
        AddString(result, "deadline_at", DeadlineAt);
        if (Session is not null)
            result["session"] = Session.ToJsonObject();
        if (Artifacts is not null)
        {
            var array = new JsonArray();
            foreach (var artifact in Artifacts)
                array.Add(artifact.ToJsonObject());
            result["artifacts"] = array;
        }
        if (Labels is not null)
        {
            var labels = new JsonObject();
            foreach (var pair in Labels)
                labels[pair.Key] = pair.Value;
            result["labels"] = labels;
        }
        return result;
    }

    private static void AddString(JsonObject obj, string name, string? value)
    {
        if (value is not null)
            obj[name] = value;
    }

    private static IReadOnlyDictionary<string, string>? ReadLabels(JsonObject? obj)
    {
        if (obj is null)
            return null;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in obj)
        {
            if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text))
                labels[pair.Key] = text;
        }
        return labels;
    }
}
