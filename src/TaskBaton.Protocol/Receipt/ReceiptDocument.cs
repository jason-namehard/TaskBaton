using System.Text.Json;
using System.Text.Json.Nodes;

namespace TaskBaton.Protocol;

/// <summary>
/// A parsed receipt. The original JSON is retained in <see cref="Raw"/> so duplicate detection
/// compares content rather than re-serialized approximations.
/// </summary>
public sealed class ReceiptDocument
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "handoff_version",
        "handoff_id",
        "task_id",
        "attempt_id",
        "turn",
        "sender",
        "status",
        "outcome",
        "needs_reply",
        "result",
        "deliverable",
        "open_items",
        "next_action",
        "verification",
        "error",
        "side_effects_possible",
        "session",
        "artifacts",
        "written_at",
        "idempotency_key",
        "review",
    };

    public string? HandoffVersion { get; init; }

    public string? HandoffId { get; init; }

    public string? TaskId { get; init; }

    public string? AttemptId { get; init; }

    public int? Turn { get; init; }

    public string? Sender { get; init; }

    public string? Status { get; init; }

    public string? Outcome { get; init; }

    public bool? NeedsReply { get; init; }

    public ReceiptResult? Result { get; init; }

    public string? Deliverable { get; init; }

    public IReadOnlyList<string>? OpenItems { get; init; }

    public string? NextAction { get; init; }

    public ReceiptVerification? Verification { get; init; }

    public ReceiptError? Error { get; init; }

    public bool? SideEffectsPossible { get; init; }

    public SessionBinding? Session { get; init; }

    public IReadOnlyList<HandoffArtifact>? Artifacts { get; init; }

    public string? WrittenAt { get; init; }

    public string? IdempotencyKey { get; init; }

    public ReceiptReview? Review { get; init; }

    public JsonObject Raw { get; init; } = new();

    public JsonObject Extra { get; init; } = new();

    public ReceiptKey Key => ReceiptKey.FromReceipt(this);

    public bool HasOpenItems => OpenItems is { Count: > 0 };

    public bool IsReviewerFinalApproval =>
        Status == "done"
        && Outcome == "approved"
        && Review?.IsReviewerFinal == true
        && !HasOpenItems
        && Result is not null;

    public bool IsLegacyAuthorAccept =>
        Status == "done"
        && Outcome == "accepted"
        && !HasOpenItems;

    /// <summary>A claim that should not be replaced by a different final claim for the same attempt.</summary>
    public bool IsFinalClaim =>
        Status == "failed"
        || (Status == "done" && (NeedsReply == false || Outcome == "accepted" || Review?.IsReviewerFinal == true));

    /// <summary>Whether this receipt completes the handoff. <c>failed</c> parks a task but does not complete it.</summary>
    public bool IsTerminalCompletion =>
        Status == "done"
        && (IsLegacyAuthorAccept || IsReviewerFinalApproval || (NeedsReply == false && Outcome != "approved"));

    public static ReceiptDocument LoadFile(string path) => Load(File.ReadAllText(path));

    public static ReceiptDocument Load(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ProtocolFormatException(
                ProtocolDiagnosticCodes.InvalidJson,
                $"receipt.json is not valid JSON: {ex.Message}",
                innerException: ex);
        }

        if (node is not JsonObject obj)
        {
            throw new ProtocolFormatException(
                ProtocolDiagnosticCodes.InvalidJson,
                "receipt.json must contain a JSON object.");
        }

        return FromJson(obj);
    }

    public static ReceiptDocument FromJson(JsonObject obj)
    {
        var extra = new JsonObject();
        foreach (var pair in obj)
        {
            if (!KnownKeys.Contains(pair.Key))
                extra[pair.Key] = pair.Value?.DeepClone();
        }

        return new ReceiptDocument
        {
            HandoffVersion = JsonRead.String(obj, "handoff_version"),
            HandoffId = JsonRead.String(obj, "handoff_id"),
            TaskId = JsonRead.String(obj, "task_id"),
            AttemptId = JsonRead.String(obj, "attempt_id"),
            Turn = JsonRead.Int(obj, "turn"),
            Sender = JsonRead.String(obj, "sender"),
            Status = JsonRead.String(obj, "status"),
            Outcome = JsonRead.String(obj, "outcome"),
            NeedsReply = JsonRead.Bool(obj, "needs_reply"),
            Result = JsonRead.Object(obj, "result") is { } result ? ReceiptResult.FromJson(result) : null,
            Deliverable = JsonRead.String(obj, "deliverable"),
            OpenItems = JsonRead.Array(obj, "open_items") is { } items ? ReadStringArray(items) : null,
            NextAction = JsonRead.String(obj, "next_action"),
            Verification = JsonRead.Object(obj, "verification") is { } verification
                ? ReceiptVerification.FromJson(verification)
                : null,
            Error = JsonRead.Object(obj, "error") is { } error ? ReceiptError.FromJson(error) : null,
            SideEffectsPossible = JsonRead.Bool(obj, "side_effects_possible"),
            Session = JsonRead.Object(obj, "session") is { } session ? SessionBinding.FromJson(session) : null,
            Artifacts = JsonRead.Array(obj, "artifacts") is { } artifacts
                ? artifacts.OfType<JsonObject>().Select(HandoffArtifact.FromJson).ToArray()
                : null,
            WrittenAt = JsonRead.String(obj, "written_at"),
            IdempotencyKey = JsonRead.String(obj, "idempotency_key"),
            Review = JsonRead.Object(obj, "review") is { } review ? ReceiptReview.FromJson(review) : null,
            Raw = obj.DeepClone().AsObject(),
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
        AddString(result, "sender", Sender);
        AddString(result, "status", Status);
        AddString(result, "outcome", Outcome);
        if (NeedsReply is not null)
            result["needs_reply"] = NeedsReply.Value;
        if (Result is not null)
            result["result"] = Result.ToJsonObject();
        AddString(result, "deliverable", Deliverable);
        if (OpenItems is not null)
        {
            var items = new JsonArray();
            foreach (var item in OpenItems)
                items.Add(item);
            result["open_items"] = items;
        }
        AddString(result, "next_action", NextAction);
        if (Verification is not null)
            result["verification"] = Verification.ToJsonObject();
        if (Error is not null)
            result["error"] = Error.ToJsonObject();
        if (SideEffectsPossible is not null)
            result["side_effects_possible"] = SideEffectsPossible.Value;
        if (Session is not null)
            result["session"] = Session.ToJsonObject();
        if (Artifacts is not null)
        {
            var artifacts = new JsonArray();
            foreach (var artifact in Artifacts)
                artifacts.Add(artifact.ToJsonObject());
            result["artifacts"] = artifacts;
        }
        AddString(result, "written_at", WrittenAt);
        AddString(result, "idempotency_key", IdempotencyKey);
        if (Review is not null)
            result["review"] = Review.ToJsonObject();
        return result;
    }

    public bool ContentEquals(ReceiptDocument other) => JsonNode.DeepEquals(Raw, other.Raw);

    private static void AddString(JsonObject obj, string name, string? value)
    {
        if (value is not null)
            obj[name] = value;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonArray array) =>
        array.OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => text is not null)
            .Cast<string>()
            .ToArray();
}

/// <summary>Receipt uniqueness key: one idempotent write per task/attempt/turn/sender/key.</summary>
public sealed record ReceiptKey(
    string TaskId,
    string AttemptId,
    int Turn,
    string Sender,
    string IdempotencyKey)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(TaskId)
        && !string.IsNullOrWhiteSpace(AttemptId)
        && !string.IsNullOrWhiteSpace(Sender)
        && !string.IsNullOrWhiteSpace(IdempotencyKey);

    public static ReceiptKey FromReceipt(ReceiptDocument receipt) => new(
        receipt.TaskId ?? string.Empty,
        receipt.AttemptId ?? string.Empty,
        receipt.Turn ?? 0,
        receipt.Sender ?? string.Empty,
        receipt.IdempotencyKey ?? string.Empty);
}
