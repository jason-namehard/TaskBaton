using System.Text.Json.Nodes;

namespace TaskBaton.Protocol;

public sealed class ReceiptResult
{
    public string Path { get; init; } = string.Empty;

    public string? Sha256 { get; init; }

    public string? Summary { get; init; }

    public static ReceiptResult FromJson(JsonObject obj) => new()
    {
        Path = JsonRead.String(obj, "path") ?? string.Empty,
        Sha256 = JsonRead.String(obj, "sha256"),
        Summary = JsonRead.String(obj, "summary"),
    };

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject { ["path"] = Path };
        if (Sha256 is not null)
            result["sha256"] = Sha256;
        if (Summary is not null)
            result["summary"] = Summary;
        return result;
    }
}

public sealed class ReceiptVerification
{
    public bool? Passed { get; init; }

    public IReadOnlyList<string> Commands { get; init; } = [];

    public IReadOnlyList<string> Evidence { get; init; } = [];

    public static ReceiptVerification FromJson(JsonObject obj) => new()
    {
        Passed = JsonRead.Bool(obj, "passed"),
        Commands = ReadStringArray(JsonRead.Array(obj, "commands")),
        Evidence = ReadStringArray(JsonRead.Array(obj, "evidence")),
    };

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject();
        if (Passed is not null)
            result["passed"] = Passed.Value;
        result["commands"] = ToArray(Commands);
        result["evidence"] = ToArray(Evidence);
        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonArray? array)
    {
        if (array is null)
            return [];
        return array.OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => text is not null)
            .Cast<string>()
            .ToArray();
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}

public sealed class ReceiptError
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public static ReceiptError FromJson(JsonObject obj) => new()
    {
        Code = JsonRead.String(obj, "code") ?? string.Empty,
        Message = JsonRead.String(obj, "message") ?? string.Empty,
    };

    public JsonObject ToJsonObject() => new()
    {
        ["code"] = Code,
        ["message"] = Message,
    };
}

/// <summary>
/// Optional additive review metadata. handoff/1 readers that do not understand it must ignore it.
/// <see cref="Final"/> is the authoritative marker for reviewer-final approval.
/// </summary>
public sealed class ReceiptReview
{
    public string? Decision { get; init; }

    public string? Reviewer { get; init; }

    public string? Author { get; init; }

    public bool? Final { get; init; }

    public bool IsReviewerFinal => Final == true;

    public static ReceiptReview FromJson(JsonObject obj) => new()
    {
        Decision = JsonRead.String(obj, "decision"),
        Reviewer = JsonRead.String(obj, "reviewer"),
        Author = JsonRead.String(obj, "author"),
        Final = JsonRead.Bool(obj, "final"),
    };

    public JsonObject ToJsonObject()
    {
        var result = new JsonObject();
        if (Decision is not null)
            result["decision"] = Decision;
        if (Reviewer is not null)
            result["reviewer"] = Reviewer;
        if (Author is not null)
            result["author"] = Author;
        if (Final is not null)
            result["final"] = Final.Value;
        return result;
    }
}
