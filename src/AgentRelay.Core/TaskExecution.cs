using System.ComponentModel;

namespace AgentRelay.Core;

public sealed partial class RelayService
{
    public async Task<ExecutorRecord> VerifyExecutorAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var agent = store.Read(s => Agent(s, agentId));
        RequirePaths(agent);
        var verified = await CodexExecutor.VerifyAsync(agent, cancellationToken);
        return store.Write(s =>
        {
            s.Execute("INSERT INTO executors VALUES($agent,$json) ON CONFLICT(agent_id) DO UPDATE SET json=excluded.json",
                ("$agent", agentId), ("$json", RelayJson.Write(verified)));
            s.Execute("UPDATE agents SET json=$json WHERE id=$agent", ("$json", RelayJson.Write(agent with { HostVersion = verified.Version })), ("$agent", agentId));
            return verified;
        });
    }

    public List<ExecutorRecord> Executors() => store.Read(s => s.Query("SELECT json FROM executors ORDER BY agent_id",
        r => RelayJson.Read<ExecutorRecord>(r.GetString(0))));

    public void RequireExecutorReady(string agentId) => store.Read(s =>
    {
        RequirePaths(Agent(s, agentId));
        return Executor(s, agentId);
    });

    public ExecutionRecord? ExecutionEvidence(string taskId, string? attemptId = null) => store.Read(s =>
    {
        var task = s.Task(taskId);
        return Execution(s, taskId, attemptId ?? task.AttemptId);
    });

    public bool ScheduleExecution(string taskId, string attemptId, string sender, int timeoutSeconds)
    {
        ValidateExecutionTimeout(timeoutSeconds);
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            RequireExecutionIdentity(s, task, attemptId, sender);
            if (Terminal(task.State)) return false;
            if (Execution(s, taskId, attemptId) is not null) return false;
            Executor(s, task.RecipientId);
            if (task.State != TaskState.Dispatched || task.AdapterId != "amq-cli")
                throw new RelayException("execution_not_ready", "自动执行需要已入队的 AMQ 任务。", 3);
            var execution = new ExecutionRecord(taskId, attemptId, sender, "scheduled", Now,
                Now.AddSeconds(timeoutSeconds + 45), null, null);
            s.Execute("INSERT INTO executions VALUES($task,$attempt,$json)", ("$task", taskId), ("$attempt", attemptId), ("$json", RelayJson.Write(execution)));
            s.Event(task, "execution.scheduled", "relay", Now);
            return true;
        });
    }

    public void ExecutionLaunchFailed(string taskId, string attemptId, string error) => store.Write(s =>
    {
        var current = Execution(s, taskId, attemptId)!;
        SaveExecution(s, current with { State = "launch_failed", ErrorCode = "worker_launch_failed" });
        var task = s.Task(taskId);
        s.Event(task, "execution.launch_failed", "relay", Now, error);
        s.Notify(task, "execution_attention", "单次 worker 未能启动，可显式运行 task run。", Now);
        return true;
    });

    public void RecordExecutionWorker(string taskId, string attemptId, int processId) => store.Write(s =>
    {
        SaveExecution(s, Execution(s, taskId, attemptId)! with { WorkerPid = processId });
        return true;
    });

    public async Task<TaskView> RunTaskAsync(string taskId, string attemptId, string sender, int timeoutSeconds = 180,
        IAgentExecutor? executor = null, IMessageReceiver? receiver = null, CancellationToken cancellationToken = default)
    {
        ValidateExecutionTimeout(timeoutSeconds);
        var claim = store.Write(s =>
        {
            var task = s.Task(taskId);
            RequireExecutionIdentity(s, task, attemptId, sender);
            if (Terminal(task.State)) return null;
            var existing = Execution(s, taskId, attemptId);
            if (existing is not null && existing.State is not ("scheduled" or "launch_failed")) return null;
            if (task.State != TaskState.Dispatched)
                throw new RelayException("execution_not_ready", "执行器只能领取已入队、尚未开始的任务。", 5);
            if (executor is null) Executor(s, sender);
            var execution = existing is null
                ? new ExecutionRecord(taskId, attemptId, sender, "receiving", Now, Now.AddSeconds(timeoutSeconds + 45), null, null)
                : existing with { State = "receiving", LeaseUntil = Now.AddSeconds(timeoutSeconds + 45), ErrorCode = null };
            s.Execute("INSERT INTO executions VALUES($task,$attempt,$json) ON CONFLICT(task_id,attempt_id) DO UPDATE SET json=excluded.json",
                ("$task", taskId), ("$attempt", attemptId), ("$json", RelayJson.Write(execution)));
            s.Event(task, "execution.claimed", sender, Now);
            return task;
        });
        if (claim is null) return Status(taskId);
        try
        {
            var transport = DeliveryEvidence(taskId) ?? throw new RelayException("receipt_missing", "任务没有运输凭据。", 6);
            if (receiver is null)
            {
                if (claim.AdapterId != "amq-cli") throw new RelayException("unsupported_receiver", "当前执行器仅支持 AMQ 取件。", 3);
                var registration = store.Read(s => s.Json<AdapterRecord>("SELECT json FROM adapters WHERE id='amq-cli'"))
                    ?? throw new RelayException("adapter_missing", "未登记 AMQ。", 3);
                receiver = new AmqMessageReceiver(registration);
            }
            await receiver.ReceiveAsync(claim, Home, transport, TimeSpan.FromSeconds(Settings().DeliveryTimeoutSeconds), cancellationToken);
            Acknowledge(taskId, attemptId, sender, "worker-ack");
            Acknowledge(taskId, attemptId, sender, "worker-start", start: true);
            UpdateExecution(taskId, attemptId, "running");
            var directory = workspace.RequireManaged(Path.Combine(workspace.AttemptDirectory(taskId, attemptId), "execution"));
            Directory.CreateDirectory(directory);
            executor ??= new CodexExecutor(store.Read(s => Executor(s, sender)));
            var outcome = await executor.ExecuteAsync(new ExecutionInput(claim, File.ReadAllText(claim.RequestPath), directory, timeoutSeconds), cancellationToken);
            if (outcome.Status == "missing_callback")
            {
                MarkExecutionUnknown(taskId, attemptId, outcome.ErrorCode, outcome.Message, outcome.SessionId);
                return Status(taskId);
            }
            if (outcome.Status is not ("succeeded" or "failed")) throw new InvalidOperationException("Executor returned an invalid outcome status.");
            File.WriteAllText(claim.ResultPath, outcome.ResultMarkdown, new System.Text.UTF8Encoding(false));
            try
            {
                Complete(taskId, attemptId, sender, "worker-final", claim.ResultPath, outcome.Status == "failed",
                    outcome.Status == "failed" ? outcome.ErrorCode : null, outcome.Status == "failed" ? outcome.Message : null);
            }
            catch (RelayException error) when (error.Code is "stale_attempt" or "state_conflict")
            {
                UpdateExecution(taskId, attemptId, "discarded", outcome.SessionId, error.Code);
                return Status(taskId);
            }
            UpdateExecution(taskId, attemptId, "finished", outcome.SessionId);
            await ProcessContinuationsAsync(taskId, cancellationToken: cancellationToken);
            return Status(taskId);
        }
        catch (RelayException error)
        {
            MarkExecutionUnknown(taskId, attemptId, error.Code, error.Message);
            throw;
        }
        catch (Win32Exception error)
        {
            MarkExecutionUnknown(taskId, attemptId, "executor_start_failed", error.Message);
            throw new RelayException("executor_start_failed", error.Message, 6);
        }
    }

    private void UpdateExecution(string taskId, string attemptId, string state, string? sessionId = null, string? errorCode = null) =>
        store.Write(s =>
        {
            var existing = Execution(s, taskId, attemptId)!;
            SaveExecution(s, existing with { State = state, SessionId = sessionId, ErrorCode = errorCode });
            return true;
        });

    private void MarkExecutionUnknown(string taskId, string attemptId, string code, string message, string? session = null) =>
        store.Write(s =>
        {
            var existing = Execution(s, taskId, attemptId)!;
            SaveExecution(s, existing with { State = "unknown", SessionId = session, ErrorCode = code });
            var task = s.Task(taskId);
            if (task.AttemptId == attemptId && !Terminal(task.State))
            {
                task.ErrorCode = "execution_unknown"; task.ErrorMessage = message;
                s.Save(task); s.Event(task, "execution.unknown", "relay", Now, code);
                s.Notify(task, "execution_attention", "执行结果未知，禁止自动再次启动 Agent。" + message, Now);
            }
            return true;
        });

    private void RequireExecutionIdentity(StoreSession s, TaskRecord task, string attemptId, string sender)
    {
        if (task.AttemptId != attemptId) throw new RelayException("stale_attempt", "旧 worker 不能执行新尝试。", 5);
        if (task.RecipientId != sender) throw new RelayException("execution_denied", "执行者不是任务接收方。", 3);
        RequirePaths(Agent(s, sender));
        if (!Terminal(task.State))
        {
            RequireLive(task);
        }
    }

    private static ExecutorRecord Executor(StoreSession s, string agentId)
    {
        var executor = s.Json<ExecutorRecord>("SELECT json FROM executors WHERE agent_id=$id", ("$id", agentId))
            ?? throw new RelayException("executor_unverified", "接收方执行器未验证，请先 agent verify-executor。", 3);
        var agent = Agent(s, agentId);
        if (executor.VerificationStatus != "passed" || executor.Executable != agent.CliPath || !File.Exists(executor.Executable) ||
            !string.Equals(executor.ConfigDirectory, Path.GetDirectoryName(agent.ConfigPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new RelayException("executor_stale", "执行器路径已改变，请重新验证。", 3);
        return executor;
    }

    private static ExecutionRecord? Execution(StoreSession s, string taskId, string attemptId) =>
        s.Json<ExecutionRecord>("SELECT json FROM executions WHERE task_id=$task AND attempt_id=$attempt",
            ("$task", taskId), ("$attempt", attemptId));
    private static void SaveExecution(StoreSession s, ExecutionRecord execution) =>
        s.Execute("UPDATE executions SET json=$json WHERE task_id=$task AND attempt_id=$attempt",
            ("$json", RelayJson.Write(execution)), ("$task", execution.TaskId), ("$attempt", execution.AttemptId));
    private static void ValidateExecutionTimeout(int seconds)
    {
        if (seconds is < 1 or > 1800) throw new RelayException("invalid_execution_timeout", "执行时限必须为 1–1800 秒。");
    }
}
