using System.Text.Json;

namespace TaskBaton.Core;

public sealed class CodexExecutor(ExecutorRecord registration) : IAgentExecutor
{
    public const string OutcomeSchema = """
        {"type":"object","properties":{"status":{"type":"string","enum":["succeeded","failed"]},"result_markdown":{"type":"string"},"error_code":{"type":"string"},"message":{"type":"string"}},"required":["status","result_markdown","error_code","message"],"additionalProperties":false}
        """;

    public static async Task<ExecutorRecord> VerifyAsync(AgentRecord agent, CancellationToken cancellationToken = default)
    {
        if (agent.Host != "codex" || agent.CliPath is null || !File.Exists(agent.CliPath) ||
            !string.Equals(Path.GetFileName(agent.ConfigPath), "config.toml", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new RelayException("executor_unavailable", "需要 host=codex 和明确的原生 CLI 可执行文件路径。", 3);
        var version = await RelayProcess.RunAsync(agent.CliPath, ["--version"], null, TimeSpan.FromSeconds(15), cancellationToken);
        var help = await RelayProcess.RunAsync(agent.CliPath, ["exec", "--help"], null, TimeSpan.FromSeconds(15), cancellationToken);
        string[] required = ["--output-schema", "--output-last-message", "--ephemeral", "--ignore-user-config", "--sandbox", "--json"];
        if (version.TimedOut || version.ExitCode != 0 || !version.Output.StartsWith("codex-cli ", StringComparison.Ordinal) ||
            help.TimedOut || help.ExitCode != 0 || required.Any(flag => !help.Output.Contains(flag, StringComparison.Ordinal)))
            throw new RelayException("executor_verification_failed", "Codex CLI 版本或非交互能力不满足要求。", 3);
        return new ExecutorRecord(agent.AgentId, "codex-exec", agent.CliPath, version.Output.Trim(), "passed", DateTimeOffset.UtcNow,
            Path.GetDirectoryName(agent.ConfigPath)!);
    }

    public async Task<AgentOutcome> ExecuteAsync(ExecutionInput input, CancellationToken cancellationToken)
    {
        var version = await RelayProcess.RunAsync(registration.Executable, ["--version"], null, TimeSpan.FromSeconds(15), cancellationToken);
        if (version.TimedOut || version.ExitCode != 0 || version.Output.Trim() != registration.Version)
            throw new RelayException("executor_stale", "Codex 版本已改变或不可用，请重新验证执行器。", 3);
        Directory.CreateDirectory(input.Directory);
        var schema = Path.Combine(input.Directory, "outcome.schema.json");
        var response = Path.Combine(input.Directory, "outcome.json");
        File.WriteAllText(schema, OutcomeSchema);
        var prompt = $"""
            You are TaskBaton recipient {input.Task.RecipientId}, processing exactly one task.
            Task ID: {input.Task.TaskId}; attempt ID: {input.Task.AttemptId}.
            Read the request below and produce the requested result. You have a read-only sandbox.
            Return the schema-defined JSON only. status=succeeded requires a nonempty result_markdown
            and an empty error_code. If the request cannot be completed, return status=failed,
            a nonempty error_code, message and a Markdown failure report in result_markdown.
            The one-shot Relay worker will explicitly submit your declaration through the existing
            complete/fail operation. Exit code zero alone never completes the task.
            Do not dispatch other agents, change configuration, publish, or poll for other work.

            <request>
            {input.RequestMarkdown}
            </request>
            """;
        var result = await RelayProcess.RunAsync(registration.Executable,
            ["exec", "--ignore-user-config", "--ephemeral", "--sandbox", "read-only", "--skip-git-repo-check",
                "--cd", input.Directory, "--output-schema", schema, "--output-last-message", response, "--json", "-"],
            prompt, TimeSpan.FromSeconds(input.TimeoutSeconds), cancellationToken,
            new Dictionary<string, string> { ["CODEX_HOME"] = registration.ConfigDirectory });
        File.WriteAllText(Path.Combine(input.Directory, "events.jsonl"), result.Output);
        File.WriteAllText(Path.Combine(input.Directory, "stderr.log"), result.Error);
        return ParseOutcome(result, File.Exists(response) ? File.ReadAllText(response) : null);
    }

    public static AgentOutcome ParseOutcome(ProcessResult result, string? declarationJson)
    {
        if (result.TimedOut)
            return new AgentOutcome("missing_callback", "", "executor_timed_out", "Codex 未在执行时限内提交有效声明。");
        string? session = null;
        var turnCompleted = false;
        try
        {
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var entry = JsonDocument.Parse(line);
                if (entry.RootElement.ValueKind != JsonValueKind.Object || !entry.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
                    return Failure("invalid_event_stream", "CLI 事件缺少有效 type。", session);
                if (type.GetString() == "thread.started")
                {
                    if (!entry.RootElement.TryGetProperty("thread_id", out var id) || id.ValueKind != JsonValueKind.String)
                        return Failure("invalid_event_stream", "CLI 会话事件缺少 thread_id。", session);
                    session = id.GetString();
                }
                if (type.GetString() == "turn.completed") turnCompleted = true;
            }
        }
        catch (JsonException)
        {
            return Failure("invalid_event_stream", "CLI 未返回有效的 JSONL 事件。", session);
        }
        if (result.ExitCode != 0)
            return Failure("executor_exited", $"Codex CLI 退出码 {result.ExitCode}；检查执行日志。", session);
        if (declarationJson is null) return Failure("completion_missing", "CLI 正常退出，但没有结构化完成声明。", session);
        if (string.IsNullOrWhiteSpace(session) || !turnCompleted)
            return Failure("completion_unverified", "CLI 未提供有效的会话及轮次完成证据。", session);
        try
        {
            var declaration = RelayJson.Read<AgentOutcome>(declarationJson);
            if (declaration.Status is not ("succeeded" or "failed") || string.IsNullOrWhiteSpace(declaration.ResultMarkdown) || declaration.Message is null ||
                (declaration.Status == "failed" && (string.IsNullOrWhiteSpace(declaration.ErrorCode) || string.IsNullOrWhiteSpace(declaration.Message))) ||
                (declaration.Status == "succeeded" && declaration.ErrorCode != ""))
                return Failure("invalid_completion", "完成声明字段不符合契约。", session);
            return declaration with { SessionId = session };
        }
        catch (JsonException)
        {
            return Failure("invalid_completion", "完成声明不是有效 JSON。", session);
        }
    }

    private static AgentOutcome Failure(string code, string message, string? session) =>
        new("failed", $"## Execution Failure\n\n{message}\n\nError: `{code}`", code, message, session);
}

public sealed class AmqMessageReceiver(AdapterRecord registration) : IMessageReceiver
{
    public async Task ReceiveAsync(TaskRecord task, string home, OutboxItem evidence, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var receipt = JsonDocument.Parse(evidence.Receipt!);
        var messageId = receipt.RootElement.GetProperty("id").GetString();
        var root = Path.Combine(home, "transport", "amq", task.TaskId, task.AttemptId);
        var result = await RelayProcess.RunAsync(registration.Executable,
            ["drain", "--root", root, "--me", task.RecipientId, "--include-body", "--json"], null, timeout, cancellationToken);
        if (result.TimedOut || result.ExitCode != 0) throw new RelayException("receive_failed", "AMQ 取件失败，执行状态需要人工检查。", 6);
        using var drained = JsonDocument.Parse(result.Output);
        var messages = drained.RootElement.GetProperty("drained").EnumerateArray().ToArray();
        if (messages.Length != 1 || messages[0].GetProperty("id").GetString() != messageId ||
            messages[0].GetProperty("from").GetString() != task.RequesterId ||
            !messages[0].GetProperty("to").EnumerateArray().Any(to => to.GetString() == task.RecipientId) ||
            !messages[0].GetProperty("body").GetString()!.Contains($"task={task.TaskId} attempt={task.AttemptId}", StringComparison.Ordinal))
            throw new RelayException("message_mismatch", "AMQ 消息与当前任务、尝试或收件人不匹配，未启动 Agent。", 6);
    }
}
