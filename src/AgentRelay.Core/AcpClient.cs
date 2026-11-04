using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentRelay.Core;

/// <summary>
/// Minimal Agent Client Protocol client (https://agentclientprotocol.com): newline-delimited JSON-RPC 2.0 over the agent's stdio.
/// One call starts the agent, opens or resumes one session, sends one prompt and waits for the turn to end.
/// </summary>
internal static class AcpClient
{
    public const int ProtocolVersion = 1;

    /// <summary>Returns the turn as relay-json on stdout so the regular output reader and diagnostics apply unchanged.</summary>
    public static async Task<ProcessResult> RunAsync(CliCommand command, string prompt, string? sessionId, string cwd,
        TimeSpan timeout, CancellationToken cancellationToken, IReadOnlyList<PreparedTaskImage> images)
    {
        var start = new ProcessStartInfo(command.Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in command.Arguments) start.ArgumentList.Add(argument);
        if (command.WorkingDirectory is not null) start.WorkingDirectory = command.WorkingDirectory;
        if (command.Environment is not null)
            foreach (var (key, value) in command.Environment) start.Environment[key] = value;
        using var process = new Process { StartInfo = start };
        process.Start();
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var session = new Session(process.StandardInput, process.StandardOutput, cwd);
        try
        {
            var reply = await session.RunTurnAsync(prompt, sessionId, images, deadline.Token);
            return new ProcessResult(0, reply, await Finish(process, stderr), false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return new ProcessResult(-1, "", await Finish(process, stderr) + session.Transcript, true);
        }
        catch (AcpImageUnsupportedException)
        {
            await Finish(process, stderr);
            throw new RelayException("cli_image_unsupported", "该 ACP Agent 未声明支持图片，任务未发送。请改用支持图片的 Agent 和模型。");
        }
        catch (AcpException error)
        {
            // Agent-reported failures (for example missing authentication) carry the agent's own message for diagnosis.
            return new ProcessResult(1, "", error.Message + "\n" + await Finish(process, stderr) + session.Transcript, false);
        }
    }

    private static async Task<string> Finish(Process process, Task<string> stderr)
    {
        try { process.StandardInput.Close(); } catch (IOException) { }
        if (!process.WaitForExit(2000)) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(CancellationToken.None);
        return await stderr;
    }

    private sealed class AcpException(string message) : Exception(message);
    private sealed class AcpImageUnsupportedException : Exception;

    private sealed class Session(StreamWriter input, StreamReader output, string cwd)
    {
        private int nextId;
        private readonly StringBuilder reply = new();
        private readonly StringBuilder transcript = new();
        public string Transcript => transcript.Length <= 8 * 1024 ? transcript.ToString() : transcript.ToString()[^(8 * 1024)..];

        public async Task<string> RunTurnAsync(string prompt, string? sessionId, IReadOnlyList<PreparedTaskImage> images, CancellationToken token)
        {
            var init = await Request("initialize", new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["clientCapabilities"] = new JsonObject
                {
                    ["fs"] = new JsonObject { ["readTextFile"] = false, ["writeTextFile"] = false },
                    ["terminal"] = false
                }
            }, token);
            if (images.Count > 0 && !(init?["agentCapabilities"]?["promptCapabilities"]?["image"] is JsonValue capability &&
                capability.TryGetValue<bool>(out var supported) && supported))
                throw new AcpImageUnsupportedException();
            var mcp = new JsonArray();
            if (sessionId is null)
            {
                var created = await Request("session/new", new JsonObject { ["cwd"] = cwd, ["mcpServers"] = mcp }, token);
                sessionId = created?["sessionId"]?.GetValue<string>() ?? throw new AcpException("ACP session/new returned no sessionId.");
            }
            else
            {
                var capabilities = init?["agentCapabilities"];
                // resume continues without replay; load replays history first, which this client discards.
                var method = capabilities?["sessionCapabilities"]?["resume"] is not null ? "session/resume"
                    : capabilities?["loadSession"]?.GetValue<bool>() == true ? "session/load"
                    : throw new AcpException("The ACP agent supports neither session/resume nor session/load, so the exact session cannot be continued.");
                await Request(method, new JsonObject { ["sessionId"] = sessionId, ["cwd"] = cwd, ["mcpServers"] = mcp }, token);
                reply.Clear();
            }
            var content = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = prompt });
            foreach (var image in images)
                content.Add(new JsonObject { ["type"] = "image", ["mimeType"] = image.Image.MediaType, ["data"] = Convert.ToBase64String(image.Data) });
            var result = await Request("session/prompt", new JsonObject
            {
                ["sessionId"] = sessionId,
                ["prompt"] = content
            }, token);
            var stop = result?["stopReason"]?.GetValue<string>();
            if (stop != "end_turn") throw new AcpException("ACP turn ended with stopReason " + (stop ?? "missing") + ".");
            return RelayJson.Write(new { session_id = sessionId, reply = reply.ToString(), accepted = true });
        }

        private async Task<JsonNode?> Request(string method, JsonObject parameters, CancellationToken token)
        {
            var id = ++nextId;
            await Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }, token);
            while (true)
            {
                var line = await output.ReadLineAsync(token) ?? throw new AcpException("The ACP agent exited before answering " + method + ".");
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonNode? message;
                try { message = JsonNode.Parse(line); }
                catch (JsonException) { transcript.AppendLine(line); continue; } // Some agents log plain text to stdout.
                if (message is not JsonObject frame) continue;
                if (frame["method"] is { } call)
                {
                    await Handle(call.GetValue<string>(), frame, token);
                    continue;
                }
                if (frame["id"]?.GetValue<int>() != id) continue;
                if (frame["error"] is { } error)
                    throw new AcpException("ACP " + method + " failed: " + (error["message"]?.ToString() ?? error.ToJsonString()));
                return frame["result"];
            }
        }

        private async Task Handle(string method, JsonObject frame, CancellationToken token)
        {
            var parameters = frame["params"];
            switch (method)
            {
                case "session/update":
                    var update = parameters?["update"];
                    if (update?["sessionUpdate"]?.GetValue<string>() == "agent_message_chunk" &&
                        update["content"]?["type"]?.GetValue<string>() == "text")
                        reply.Append(update["content"]!["text"]!.GetValue<string>());
                    else if (update?["sessionUpdate"]?.GetValue<string>() == "notice")
                        transcript.AppendLine("notice: " + update["title"] + " " + update["description"]);
                    return;
                case "session/request_permission":
                    // Unattended handoffs cannot ask the user; grant once, as the recipe's own permission flags would.
                    var allow = parameters?["options"]?.AsArray().FirstOrDefault(o => o?["kind"]?.GetValue<string>() == "allow_once")
                        ?? parameters?["options"]?.AsArray().FirstOrDefault(o => o?["kind"]?.GetValue<string>() == "allow_always");
                    transcript.AppendLine("permission: " + parameters?["toolCall"]?["title"] + " -> " + (allow is null ? "cancelled" : "allowed"));
                    await Respond(frame, allow is null
                        ? new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "cancelled" } }
                        : new JsonObject { ["outcome"] = new JsonObject { ["outcome"] = "selected", ["optionId"] = allow["optionId"]!.GetValue<string>() } }, token);
                    return;
                default:
                    if (frame["id"] is not null)
                        await Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = frame["id"]!.DeepClone(),
                            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not supported by Agent Relay: " + method } }, token);
                    return;
            }
        }

        private Task Respond(JsonObject request, JsonObject result, CancellationToken token) =>
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = result }, token);

        private async Task Send(JsonObject message, CancellationToken token)
        {
            await input.WriteLineAsync(message.ToJsonString().AsMemory(), token);
            await input.FlushAsync(token);
        }
    }
}
