using System.Globalization;
using System.IO;
using AgentRelay.Core;

namespace AgentRelay.Desktop;

public static class DesktopLocalizedContent
{
    public static string TaskPrompt(TaskView view, IReadOnlyCollection<ArtifactRecord> artifacts, string language,
        Func<string,string>? agentName = null)
    {
        var task = view.Task;
        string Format(string source, params object[] values) => string.Format(CultureInfo.InvariantCulture, UiText.Translate(source, language), values);
        if (view.NextAction == "inspect_execution_evidence")
            return Format("Task {0} has an unknown execution outcome. Inspect task execution evidence; do not start the Agent again automatically.", task.TaskId);
        if (task.State == TaskState.Cancelled)
            return Format("Task {0} was cancelled. Inspect its audit and any late results; do not continue or dispatch again automatically.", task.TaskId);
        if (task.State == TaskState.DeliveryUnknown)
            return Format("Task {0} has an unknown delivery outcome and may have been received. Inspect delivery evidence; do not resend automatically.", task.TaskId);
        if (view.DeadlineExceeded || task.State == TaskState.TimedOut)
            return Format("Task {0} exceeded its deadline without a valid completion result. Run recover or inspect the evidence; do not dispatch again automatically.", task.TaskId);
        if (task.State == TaskState.AwaitingManual)
            return Format("Task {0} awaits manual submission to {1}. Request: {2}; attempt: {3}; write the result to: {4}. Explicitly ack after receiving, then complete/fail when finished.",
                task.TaskId, agentName is null ? task.RecipientId : agentName(task.RecipientId), task.RequestPath, task.AttemptId, task.ResultPath);
        if (task.State is TaskState.Succeeded or TaskState.Failed)
        {
            var available = artifacts.Any(a => a.Path == task.ResultPath && a.State == "available" && a.ExpiresAt > DateTimeOffset.UtcNow) && File.Exists(task.ResultPath);
            return available
                ? Format("Continue TaskBaton task {0}, state {1}. Request: {2}; result: {3}. Read the result and verification details, then continue the original task; no repeated dispatch is needed.",
                    task.TaskId, task.State, task.RequestPath, task.ResultPath)
                : Format("The result file for task {0} has expired or is missing. Inspect the task record and decide whether to regenerate it.", task.TaskId);
        }
        return Format("Task {0} is saved, state {1}, awaiting result file {2}. End this turn and wait for a continuation message or a prompt pasted by the user. Deadline: {3}.",
            task.TaskId, task.State, task.ResultPath, task.DeadlineAt.ToString("O", CultureInfo.InvariantCulture));
    }

    public static string NotificationEvent(string eventKey, string language)
    {
        // The stored key starts with an attempt ID; only its event suffix is presentation text.
        var separator = eventKey.IndexOf(':');
        var name = separator < 0 || eventKey.StartsWith("resume_attention:", StringComparison.Ordinal) ? eventKey : eventKey[(separator + 1)..];
        if (name.StartsWith("resume_attention:", StringComparison.Ordinal))
            return UiText.Translate("Continuation needs attention", language) + " · " + EventLabel(name["resume_attention:".Length..], language);
        return EventLabel(name, language);
    }

    private static string EventLabel(string name, string language) => UiText.Translate(name switch
    {
        "waiting" => "Waiting for handoff",
        "ready" => "Result ready",
        "failed" => "Task failed",
        "late_result" => "Late result received",
        "cancelled" => "Task cancelled",
        "execution_attention" => "Execution needs attention",
        "delivery_attention" => "Delivery needs attention",
        "timed_out" => "Task timed out",
        _ => name
    }, language);

    public static string NotificationMessage(InboxItem item, string language) => TranslateMessage(item.Message, language);

    private static string TranslateMessage(string message, string language)
    {
        if (NotificationMessages.TryGetValue(message, out var source)) return UiText.Translate(source, language);
        foreach (var (prefix, key) in NotificationPrefixes)
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return UiText.Translate(key, language) + TranslateMessage(message[prefix.Length..], language);
        }
        // Unrecognized content may contain agent output, a path or an external error.
        return message;
    }

    public static string SetupError(RelayException error, string language)
    {
        var detail = error.Code switch
        {
            "setup_response_too_large" => string.Format(CultureInfo.InvariantCulture, UiText.Translate("The setup response exceeds {0} UTF-8 bytes.", language), SetupAssistant.MaxResponseBytes),
            "setup_invalid_json" => UiText.Translate("Return one complete JSON object, optionally inside one json code fence, without surrounding text.", language),
            "setup_invalid_response" or "setup_invalid_version" or "setup_invalid_fields" or "setup_invalid_fence" => UiText.Translate(error.Message, language),
            "setup_unknown_property" => Parameter("Unknown setup response property: ", ".", "Unknown setup response property: {0}."),
            "setup_context_mismatch" => Parameter("The setup response must use context '", "'.", "The setup response must use context '{0}'."),
            "setup_unknown_field" => Parameter("The setup response contains an unrequested field: ", ".", "The setup response contains an unrequested field: {0}."),
            "setup_invalid_field_value" => Parameter("Setup field '", "' must contain a JSON string.", "Setup field '{0}' must contain a JSON string."),
            "setup_invalid_context" => Parameter("Unsupported setup context: ", ".", "Unsupported setup context: {0}."),
            "setup_duplicate_property" => Parameter("Duplicate setup response property: ", ".", "Duplicate setup response property: {0}."),
            _ => UiText.Translate(error.Message, language)
        };
        return error.Code + ": " + detail;

        string Parameter(string prefix, string suffix, string format)
        {
            if (!error.Message.StartsWith(prefix, StringComparison.Ordinal) || !error.Message.EndsWith(suffix, StringComparison.Ordinal)) return error.Message;
            return string.Format(CultureInfo.InvariantCulture, UiText.Translate(format, language), error.Message[prefix.Length..^suffix.Length]);
        }
    }

    private static readonly Dictionary<string, string> NotificationMessages = new(StringComparer.Ordinal)
    {
        ["待手工提交给接收方。"] = "Awaiting manual submission to the recipient.",
        ["任务已保存，正在等待处理。"] = "Task saved and awaiting processing.",
        ["新执行尝试已创建。"] = "A new execution attempt was created.",
        ["接收方报告失败，请查看失败文件。"] = "The recipient reported failure. Inspect the failure file.",
        ["结果已就绪，可复制继续提示词。"] = "The result is ready. Copy the continuation prompt.",
        ["已取消任务收到晚到结果；任务仍保持取消。"] = "A cancelled task received a late result; it remains cancelled.",
        ["已停止 Relay 后续派发；宿主执行未被终止。"] = "Further Relay dispatches have stopped; host execution was not terminated.",
        ["单次 worker 未能启动，可显式运行 task run。"] = "The one-shot worker did not start. You can explicitly run task run.",
        ["执行结果未知，不会自动重新启动 Agent。"] = "The execution outcome is unknown. The Agent will not restart automatically.",
        ["截止时间已过，未收到有效完成结果；宿主可能仍在执行。"] = "The deadline passed without a valid completion result; the host may still be running.",
        ["运输进程未留下确定结果，禁止自动重发。"] = "Delivery left no confirmed outcome. Do not resend automatically.",
        ["交接产物已过期，请检查任务并决定重新生成。"] = "Handoff artifacts have expired. Inspect the task and decide whether to regenerate them.",
        ["运输重试次数已耗尽。"] = "Delivery retries are exhausted.",
        ["此任务没有可用的精确会话恢复绑定。"] = "This task has no available binding to its exact original session.",
        ["提交期间进程中断，禁止自动重复唤醒；请检查原会话或复制提示词。"] = "The process stopped during submission. Do not wake the session again automatically; inspect the original session or copy the prompt.",
        ["Codex 版本已改变，请重新验证后用于新任务。"] = "The Codex version changed. Verify it again before using it for new tasks.",
        ["会话继续调用超时，可能已提交，禁止自动重复唤醒。"] = "The continuation call timed out and may have been submitted. Do not wake the session again automatically.",
        ["缺少匹配 UUID、turn.completed 或最终回复，不能确认继续轮次完成。"] = "A matching UUID, turn.completed event or final reply is missing. Completion of the continuation turn is unconfirmed."
    };

    private static readonly (string Prefix, string Key)[] NotificationPrefixes =
    [
        ("自动继续不可用，请复制提示词。", "Automatic continuation is unavailable. Copy the prompt. "),
        ("执行结果未知，禁止自动再次启动 Agent。", "The execution outcome is unknown. Do not start the Agent again automatically. "),
        ("运输需要人工检查：", "Delivery needs manual inspection: ")
    ];
}
