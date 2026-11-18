using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using TaskBaton.Core;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TaskBaton.Mcp;

public sealed record McpIdentity(string AgentId, string RouteId);

[McpServerToolType]
public sealed class RelayTools(RelayService service, McpIdentity identity)
{
    [McpServerTool(Name = "relay_task_dispatch", Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("派发一个任务并立即返回等待提示词；随后结束当前轮，不轮询。execute=true 启动有界单次 Codex worker。")]
    public Task<CallToolResult> Dispatch(string to, string content, string idempotency_key, string mode = "auto",
        bool execute = false, int timeout_seconds = 180, OriginSession? origin_session = null, CancellationToken cancellationToken = default) => Invoke(async () =>
    {
        if (timeout_seconds is < 1 or > 1800) throw new RelayException("invalid_execution_timeout", "执行时限必须为 1–1800 秒。");
        if (execute)
        {
            if (mode != "auto") throw new RelayException("invalid_execution_mode", "execute=true 仅支持 auto 模式。");
            service.RequireExecutorReady(to);
        }
        var view = await service.DispatchAsync(new DispatchRequest(identity.AgentId, to, content, mode, execute ? "amq-cli" : service.PreferredDeliveryAdapter(to), idempotency_key,
            OriginSession: origin_session), cancellationToken);
        service.RecordMcpOperation(view.Task.TaskId, identity.AgentId, "dispatch", identity.RouteId);
        if (execute && view.Task.State == TaskState.Dispatched)
        {
            var task = view.Task;
            DetachedWorker.Launch(service, task, timeout_seconds, ["--home", service.Home, "--agent", task.RecipientId,
                "--worker-task", task.TaskId, "--worker-attempt", task.AttemptId, "--timeout-seconds", timeout_seconds.ToString(CultureInfo.InvariantCulture),
                "--report-path", Path.Combine(Path.GetDirectoryName(task.RequestPath)!, "mcp-worker-report.json")]);
        }
        return service.Status(view.Task.TaskId);
    }, dispatch: true);

    [McpServerTool(Name = "relay_task_ack", Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("确认收到当前任务尝试；发送方是此 MCP 服务绑定的 Agent。")]
    public Task<CallToolResult> Ack(string task_id, string attempt_id, string idempotency_key) => Invoke(() =>
        Task.FromResult(service.Acknowledge(task_id, attempt_id, identity.AgentId, idempotency_key)));

    [McpServerTool(Name = "relay_task_start", Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("显式报告开始当前尝试。")]
    public Task<CallToolResult> Start(string task_id, string attempt_id, string idempotency_key) => Invoke(() =>
        Task.FromResult(service.Acknowledge(task_id, attempt_id, identity.AgentId, idempotency_key, start: true)));

    [McpServerTool(Name = "relay_task_complete", Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("提交非空受管结果文件，显式完成当前尝试；文件变化不表示完成。")]
    public Task<CallToolResult> Complete(string task_id, string attempt_id, string idempotency_key, string result_path,
        CancellationToken cancellationToken = default) => Final(task_id, attempt_id, idempotency_key, result_path, false, null, null, null, cancellationToken);

    [McpServerTool(Name = "relay_task_fail", Idempotent = true, Destructive = false, OpenWorld = true)]
    [Description("提交失败报告文件、错误码和说明；不伪造成功结果。")]
    public Task<CallToolResult> Fail(string task_id, string attempt_id, string idempotency_key, string result_path,
        string error_code, string message, bool? side_effects_possible = null, CancellationToken cancellationToken = default) =>
        Final(task_id, attempt_id, idempotency_key, result_path, true, error_code, message, side_effects_possible, cancellationToken);

    private Task<CallToolResult> Final(string task, string attempt, string key, string path, bool failed, string? code,
        string? message, bool? sideEffects, CancellationToken cancellationToken) => Invoke(async () =>
    {
        service.Complete(task, attempt, identity.AgentId, key, path, failed, code, message, sideEffects);
        service.RecordMcpOperation(task, identity.AgentId, failed ? "fail" : "complete", identity.RouteId);
        await service.ProcessContinuationsAsync(task, cancellationToken: cancellationToken);
        return service.Status(task);
    });

    [McpServerTool(Name = "relay_task_status", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("单次只读查询已有任务，不触发派发、执行或恢复；不要循环调用等待结果。")]
    public Task<CallToolResult> Status(string task_id) => Invoke(() => Task.FromResult(service.McpStatus(task_id, identity.AgentId)));

    [McpServerTool(Name = "relay_prompt_show", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("读取参与任务的交接或继续提示词。")]
    public Task<CallToolResult> Prompt(string task_id) => Invoke(() => Task.FromResult(new { text = service.McpStatus(task_id, identity.AgentId).Prompt }));

    private static async Task<CallToolResult> Invoke<T>(Func<Task<T>> operation, bool dispatch = false)
    {
        try
        {
            var data = await operation();
            var view = data as TaskView;
            var failed = dispatch && view?.Task.State == TaskState.Failed;
            return Result(new { relay_version = 1, ok = !failed, task_id = view?.Task.TaskId, state = view?.Task.State,
                next_action = view?.NextAction, data, error = failed ? new { code = view!.Task.ErrorCode, message = view.Task.ErrorMessage } : null }, failed);
        }
        catch (RelayException error) { return Result(new { relay_version = 1, ok = false, data = (object?)null, error = new { code = error.Code, message = error.Message } }, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SqliteException or ArgumentException)
        {
            return Result(new { relay_version = 1, ok = false, data = (object?)null, error = new { code = "boundary_error", message = error.Message } }, true);
        }
    }
    private static CallToolResult Result<T>(T value, bool failed)
    {
        var json = RelayJson.Write(value);
        return new CallToolResult { IsError = failed, Content = [new TextContentBlock { Text = json }], StructuredContent = JsonSerializer.Deserialize<JsonElement>(json) };
    }
}
