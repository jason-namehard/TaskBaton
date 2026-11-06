using System.Text.Json;

namespace AgentRelay.Core;

internal static class CliOutput
{
    public static (string? SessionId, string Reply)? Read(ProcessResult process, string format)
    {
        if (process.TimedOut || process.ExitCode != 0) return null;
        if (format == "text") return string.IsNullOrWhiteSpace(process.Output) ? null : (null, process.Output);
        try
        {
            // The ACP and DSH desktop clients report the finished turn in relay-json.
            if (format is "relay-json" or "acp" or "dsh-desktop")
            {
                using var document = JsonDocument.Parse(process.Output);
                var root = document.RootElement;
                return True(root, "accepted") && Text(root, "session_id") is { Length: > 0 } session &&
                    Text(root, "reply") is { } reply ? (session, reply) : null;
            }

            string? sessionId = null;
            string? finalReply = null;
            var completedTurn = false;
            foreach (var root in Records(process.Output, format))
            {
                if (root.ValueKind != JsonValueKind.Object) return null;
                var type = Text(root, "type");
                if (type == "error") return null;
                switch (format)
                {
                    case "dsh-jsonl":
                        if (type == "session") sessionId = Text(root, "sessionId");
                        if (type == "status" && Text(root, "phase") == "turn_end")
                            completedTurn = root.TryGetProperty("reason", out var reason) && Text(reason, "kind") == "completed";
                        if (type == "final") finalReply = Text(root, "text");
                        break;
                    case "zcode-json":
                    case "zcode-jsonl":
                        // Zcode 0.17 prints a single untyped summary object for --json.
                        if (type == "result" || type is null && format == "zcode-json" && Text(root, "response") is not null)
                        {
                            if (True(root, "is_error") || Text(root, "error") is { Length: > 0 }) return null;
                            sessionId = Text(root, "sessionId");
                            finalReply = Text(root, "response");
                            completedTurn = true;
                        }
                        break;
                    case "hermes-jsonl":
                        if (type == "result")
                        {
                            if (!root.TryGetProperty("exit_code", out var exit) || exit.ValueKind != JsonValueKind.Number || !exit.TryGetInt32(out var code) || code != 0 ||
                                Text(root, "error") is { Length: > 0 }) return null;
                            sessionId = Text(root, "session_id");
                            finalReply = Text(root, "text");
                            completedTurn = true;
                        }
                        break;
                    case "claude-json":
                        if (type == "result")
                        {
                            if (!False(root, "is_error") || Text(root, "subtype") != "success" ||
                                Text(root, "terminal_reason") is { Length: > 0 }) return null;
                            sessionId = Text(root, "session_id");
                            finalReply = Text(root, "result");
                            completedTurn = true;
                        }
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown CLI output format.");
                }
            }
            return completedTurn && !string.IsNullOrWhiteSpace(sessionId) && finalReply is not null
                ? (sessionId, finalReply) : null;
        }
        catch (JsonException) { return null; } // A partial or malformed external stream is not a successful invocation.
    }

    /// <summary>
    /// Reads a session id from one streaming record while the turn is still running. Only a host that announces the
    /// session before the turn ends (DSH headless prints a session record first) can be read this early; the other
    /// formats report the id only in the final result, so their id stays unknown until the call returns.
    /// </summary>
    internal static string? SessionIdFromLine(string line, string format)
    {
        if (format != "dsh-jsonl") return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            return Text(document.RootElement, "type") == "session" ? Text(document.RootElement, "sessionId") : null;
        }
        catch (JsonException) { return null; }
    }

    private static IEnumerable<JsonElement> Records(string output, string format)
    {
        if (format is "zcode-json" or "claude-json")
        {
            using var document = JsonDocument.Parse(output);
            // CodeBuddy Code 2.17x prints the whole transcript as an array ending with the result record.
            if (format == "claude-json" && document.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var item in document.RootElement.EnumerateArray()) yield return item;
            else yield return document.RootElement;
            yield break;
        }
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            yield return document.RootElement;
        }
    }

    private static string? Text(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static bool True(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.True;
    private static bool False(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.False;
}
