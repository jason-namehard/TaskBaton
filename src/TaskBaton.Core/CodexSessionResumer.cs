using System.Text.Json;

namespace TaskBaton.Core;

public sealed class CodexSessionResumer(ResumerRecord registration) : ISessionResumer
{
    public static async Task<ResumerRecord> VerifyAsync(AgentRecord agent, string kind, CancellationToken cancellationToken)
    {
        if (kind is not ("codex-queue" or "codex-exec-resume"))
            throw new RelayException("unsupported_resumer", "kind 必须为 codex-queue 或 codex-exec-resume。");
        if (agent.Host != "codex" || agent.CliPath is null || Path.GetFileName(agent.ConfigPath) != "config.toml")
            throw new RelayException("resumer_unavailable", "需要 Codex 原生 CLI 和明确的 config.toml 路径。", 3);
        var version = await RelayProcess.RunAsync(agent.CliPath, ["--version"], null, TimeSpan.FromSeconds(15), cancellationToken);
        var help = await RelayProcess.RunAsync(agent.CliPath,
            kind == "codex-queue" ? ["queue", "--help"] : ["exec", "resume", "--help"], null, TimeSpan.FromSeconds(15), cancellationToken);
        string[] flags = kind == "codex-queue" ? ["--thread", "--message", "--cd"] : ["SESSION_ID", "--json", "--ignore-user-config", "--output-last-message"];
        if (version.TimedOut || version.ExitCode != 0 || !version.Output.StartsWith("codex-cli ", StringComparison.Ordinal) ||
            help.TimedOut || help.ExitCode != 0 || flags.Any(f => !help.Output.Contains(f, StringComparison.Ordinal)))
            throw new RelayException("resumer_verification_failed", "Codex 版本或精确会话接口未通过验证。", 3);
        return new ResumerRecord(agent.AgentId, kind, agent.CliPath, version.Output.Trim(), agent.ConfigPath, DateTimeOffset.UtcNow);
    }

    public static void RequireBinding(AgentRecord agent, ResumerRecord adapter, OriginSession origin)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (origin.Host != "codex" || agent.Host != "codex" || adapter.AgentId != agent.AgentId ||
            !Guid.TryParseExact(origin.SessionId, "D", out _) || !Path.IsPathFullyQualified(origin.ConfigProfile) ||
            !string.Equals(Path.GetFullPath(origin.ConfigProfile), adapter.ConfigPath, comparison) ||
            !string.Equals(agent.ConfigPath, adapter.ConfigPath, comparison) || !string.Equals(agent.CliPath, adapter.Executable, comparison))
            throw new RelayException("origin_binding_mismatch", "原会话的 UUID、宿主、配置路径或 CLI 与派发时的绑定不一致。", 3);
    }

    public static void RequireSession(OriginSession origin, string configDirectory)
    {
        if (!Directory.Exists(origin.Cwd)) throw new RelayException("origin_cwd_missing", "原会话工作目录不存在。", 3);
        var root = Path.Combine(configDirectory, "sessions");
        var files = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, $"rollout-*-{origin.SessionId}.jsonl", SearchOption.AllDirectories).ToArray() : [];
        if (files.Length != 1) throw new RelayException("origin_session_missing", "指定配置目录内没有唯一的精确会话记录。", 3);
        using var stream = File.OpenText(files[0]);
        try
        {
            using var metadata = JsonDocument.Parse(stream.ReadLine() ?? "");
            var entry = metadata.RootElement;
            if (entry.GetProperty("type").GetString() != "session_meta" ||
                entry.GetProperty("payload").GetProperty("id").GetString() != origin.SessionId ||
                !string.Equals(Path.GetFullPath(entry.GetProperty("payload").GetProperty("cwd").GetString()!), Path.GetFullPath(origin.Cwd),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new RelayException("origin_session_mismatch", "会话记录的 UUID 或 cwd 与原始绑定不一致。", 3);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new RelayException("origin_session_invalid", "会话元数据无法验证：" + error.Message, 3);
        }
    }

    public async Task<ResumeResult> ResumeAsync(ResumeInput input, CancellationToken cancellationToken)
    {
        var origin = input.Task.OriginSession!;
        var configDirectory = Path.GetDirectoryName(registration.ConfigPath)!;
        RequireSession(origin, configDirectory);
        var version = await RelayProcess.RunAsync(registration.Executable, ["--version"], null, TimeSpan.FromSeconds(15), cancellationToken);
        if (version.TimedOut || version.ExitCode != 0 || version.Output.Trim() != registration.Version)
            return new ResumeResult("failed", "resumer_stale", "Codex 版本已改变，请重新验证后用于新任务。");
        var output = Path.Combine(input.Directory, "last-message.md");
        var args = registration.Kind == "codex-queue"
            ? new[] { "queue", "--thread", origin.SessionId, "--message", input.Prompt, "--cd", origin.Cwd }
            : new[] { "exec", "--ignore-user-config", "--sandbox", "read-only", "--cd", origin.Cwd, "resume",
                "--skip-git-repo-check", "--json", "--output-last-message", output, origin.SessionId, "-" };
        var process = await RelayProcess.RunAsync(registration.Executable, args,
            registration.Kind == "codex-queue" ? null : input.Prompt, TimeSpan.FromSeconds(input.TimeoutSeconds), cancellationToken,
            new Dictionary<string, string> { ["CODEX_HOME"] = configDirectory }, origin.Cwd);
        File.WriteAllText(Path.Combine(input.Directory, "events.log"), process.Output);
        File.WriteAllText(Path.Combine(input.Directory, "stderr.log"), process.Error);
        return ParseResult(registration.Kind, origin.SessionId, process, File.Exists(output) ? File.ReadAllText(output) : null, output);
    }

    public static ResumeResult ParseResult(string kind, string sessionId, ProcessResult process, string? lastMessage, string outputPath)
    {
        if (process.TimedOut) return new ResumeResult("unknown", "resume_timed_out", "会话继续调用超时，可能已提交，禁止自动重复唤醒。");
        if (kind == "codex-queue")
        {
            var accepted = process.Output.Contains("Queued message ", StringComparison.Ordinal) && process.Output.Contains(sessionId, StringComparison.Ordinal);
            if (process.ExitCode == 0 && accepted)
                return new ResumeResult("submitted", Detail: "已提交到精确原会话；不代表 A 已阅读或执行完成。");
            return new ResumeResult(accepted || process.ExitCode == 0 ? "unknown" : "failed", "queue_not_confirmed", process.Error + process.Output);
        }
        // Once exec starts, an error may follow a partial turn; never automatically replay it.
        if (process.ExitCode != 0) return new ResumeResult("unknown", "resume_execution_failed", process.Error);
        try
        {
            var sessions = new List<string>(); var completed = false;
            foreach (var line in process.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(line);
                var type = json.RootElement.GetProperty("type").GetString();
                if (type == "thread.started") sessions.Add(json.RootElement.GetProperty("thread_id").GetString()!);
                if (type == "turn.completed") completed = true;
            }
            if (sessions.Count == 1 && sessions[0] == sessionId && completed && !string.IsNullOrWhiteSpace(lastMessage))
                return new ResumeResult("completed", Detail: "精确原会话的新一轮已完成；原任务结果保持独立。", OutputPath: outputPath);
            return new ResumeResult("unknown", "resume_evidence_missing", "缺少匹配 UUID、turn.completed 或最终回复，不能确认继续轮次完成。");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new ResumeResult("unknown", "resume_evidence_invalid", error.Message);
        }
    }
}
