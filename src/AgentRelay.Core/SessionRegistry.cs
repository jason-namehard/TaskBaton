namespace AgentRelay.Core;

public sealed record TaskSessionBinding(string TaskId, string AttemptId, int Turn, string AgentId, string Host,
    string? SessionId, string? Cwd, string? Profile, string? SessionPath, string Binding, string Visibility,
    DateTimeOffset BoundAt, string? Evidence = null, bool IndependentReview = false);

public sealed partial class RelayService
{
    public TaskView CreateFileHandoffFromContext(ManualTransferDraft draft)
    {
        var sessionId = Environment.GetEnvironmentVariable("AGENT_RELAY_CALLER_SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId)) return CreateFileHandoff(draft);
        var agentId = Environment.GetEnvironmentVariable("AGENT_RELAY_CALLER_AGENT_ID");
        var normalized = ManualTransfers.Parse(RelayJson.Write(draft), Agents());
        if (normalized.From != agentId)
            throw new RelayException("session_origin_mismatch", "调用上下文所属 Agent 与交接发送方不一致。");
        var host = Environment.GetEnvironmentVariable("AGENT_RELAY_CALLER_HOST");
        var cwd = Environment.GetEnvironmentVariable("AGENT_RELAY_CALLER_CWD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(cwd))
            throw new RelayException("session_origin_incomplete", "调用适配器提供的来源会话上下文不完整。");
        return CreateFileHandoff(normalized, new OriginSession(host, sessionId,
            Environment.GetEnvironmentVariable("AGENT_RELAY_CALLER_PROFILE") ?? "", cwd));
    }

    public List<TaskSessionBinding> SessionBindings(string taskId) => store.Read(s =>
    {
        _ = s.Task(taskId);
        return s.Query("SELECT json FROM task_session_bindings WHERE task_id=$task ORDER BY attempt_id,turn,agent_id",
            row => RelayJson.Read<TaskSessionBinding>(row.GetString(0)), ("$task", taskId));
    });

    public TaskSessionBinding? TaskSession(string taskId, string agentId) => store.Read(s =>
        ReadTaskSession(s, s.Task(taskId), agentId));

    public TaskSessionBinding BindTaskSession(string taskId, string agentId, SessionRecord session,
        string binding = "user_selected")
    {
        if (binding is not ("user_selected" or "manual"))
            throw new RelayException("session_binding_invalid", "已有会话只能由用户明确选择或手动输入。");
        if (string.IsNullOrWhiteSpace(session.SessionId))
            throw new RelayException("session_id_required", "请选择已有会话，或填写准确的会话 ID。");
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            RequireSessionChange(task, agentId);
            RequireExistingSessionAllowed(task, agentId);
            var agent = Agent(s, agentId);
            var value = new TaskSessionBinding(task.TaskId, task.AttemptId, task.ManualTurn, agentId, agent.Host,
                session.SessionId, session.Cwd, session.Profile, session.SessionPath, binding, session.Visibility, Now,
                binding == "manual" ? "operator supplied an exact session ID" : "operator selected an existing session");
            SaveTaskSession(s, task, value);
            ResetUncertainFileDelivery(task, agentId);
            s.Save(task);
            return value;
        });
    }

    public TaskSessionBinding BindManualTaskSession(string taskId, string agentId, string sessionId,
        string? cwd = null, string? profile = null, string? sessionPath = null) =>
        BindTaskSession(taskId, agentId, new SessionRecord(sessionId, null, cwd, profile, sessionPath, "unknown", null), "manual");

    public void UseNewTaskSession(string taskId, string agentId) => store.Write(s =>
    {
        var task = s.Task(taskId); RequireSessionChange(task, agentId);
        var agent = Agent(s, agentId);
        var independentReview = UsesIndependentReviewSession(task, agentId);
        SaveTaskSession(s, task, new(task.TaskId, task.AttemptId, task.ManualTurn, agentId, agent.Host,
            null, independentReview ? ReadTaskSession(s, task, agentId)?.Cwd : null, null, null, "new", "unknown", Now,
            "operator chose a new session", independentReview));
        ResetUncertainFileDelivery(task, agentId); s.Save(task);
        return true;
    });

    public TaskSessionBinding RegisterOriginSession(string taskId, OriginSession origin) => store.Write(s =>
    {
        var task = s.Task(taskId);
        return RegisterOriginSession(s, task, origin);
    });

    private TaskSessionBinding RegisterOriginSession(StoreSession s, TaskRecord task, OriginSession origin)
    {
        RequireExistingSessionAllowed(task, task.RequesterId);
        RequireOriginContext(origin);
        _ = Agent(s, task.RequesterId);
        var value = new TaskSessionBinding(task.TaskId, task.AttemptId, task.ManualTurn, task.RequesterId,
            origin.Host, origin.SessionId, origin.Cwd, origin.ConfigProfile, null, "automatic", "unknown", Now,
            "origin session supplied by the calling adapter context");
        task.OriginSession = origin;
        SaveTaskSession(s, task, value);
        s.Save(task);
        return value;
    }

    private static void RequireOriginContext(OriginSession origin)
    {
        if (string.IsNullOrWhiteSpace(origin.Host) || string.IsNullOrWhiteSpace(origin.SessionId) ||
            string.IsNullOrWhiteSpace(origin.Cwd) || !Path.IsPathFullyQualified(origin.Cwd))
            throw new RelayException("invalid_origin", "来源调用上下文需要准确的宿主、会话 ID 和完整工作目录。");
    }

    private static TaskSessionBinding? ReadTaskSession(StoreSession s, TaskRecord task, string agentId) =>
        s.Query("SELECT json FROM task_session_bindings WHERE task_id=$task AND attempt_id=$attempt AND agent_id=$agent ORDER BY turn DESC LIMIT 1",
            row => RelayJson.Read<TaskSessionBinding>(row.GetString(0)), ("$task", task.TaskId),
            ("$attempt", task.AttemptId), ("$agent", agentId)).SingleOrDefault();

    private void SaveTaskSession(StoreSession s, TaskRecord task, TaskSessionBinding value)
    {
        s.Execute("""
            INSERT INTO task_session_bindings(task_id,attempt_id,turn,agent_id,json) VALUES($task,$attempt,$turn,$agent,$json)
            ON CONFLICT(task_id,attempt_id,turn,agent_id) DO UPDATE SET json=excluded.json
            """, ("$task", value.TaskId), ("$attempt", value.AttemptId), ("$turn", value.Turn),
            ("$agent", value.AgentId), ("$json", RelayJson.Write(value)));
        s.Event(task, "session.bound", value.AgentId, Now, RelayJson.Write(value));
    }

    private static void RequireSessionChange(TaskRecord task, string agentId)
    {
        if (agentId != task.RequesterId && agentId != task.RecipientId)
            throw new RelayException("session_agent_mismatch", "会话必须属于本任务的发送方或接收方。");
        if (Terminal(task.State)) throw new RelayException("session_task_finished", "已结束任务不能更换会话。");
        if (task.CliFileTurn == task.ManualTurn && task.CliFileStatus is "submitting" or "submitted")
            throw new RelayException("session_delivery_active", "本轮已投递，请先等待结果，不要在处理期间更换会话。");
    }

    private static void ResetUncertainFileDelivery(TaskRecord task, string agentId)
    {
        if ((task.AwaitingAgentId ?? task.RecipientId) != agentId || task.CliFileTurn != task.ManualTurn) return;
        task.CliFileTurn = null; task.CliFileStatus = null; task.CliFileSessionId = null; task.CliFileError = null;
    }

    private static void RequireExistingSessionAllowed(TaskRecord task, string agentId)
    {
        if (task.ParallelGroupId is not null)
            throw new RelayException("parallel_new_session_required", "并行分工和汇合需要使用独立工作目录的新会话。");
        if (UsesIndependentReviewSession(task, agentId))
            throw new RelayException("review_session_required", "独立验收需要任务专用会话，请选择新建会话。");
    }
}
