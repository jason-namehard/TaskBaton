namespace TaskBaton.Core;

public sealed record TaskSessionBinding(string TaskId, string AttemptId, int Turn, string AgentId, string Host,
    string? SessionId, string? Cwd, string? Profile, string? SessionPath, string Binding, string Visibility,
    DateTimeOffset BoundAt, string? Evidence = null, bool IndependentReview = false, string Purpose = "work");

public sealed partial class RelayService
{
    public TaskView CreateFileHandoffFromContext(ManualTransferDraft draft)
    {
        var sessionId = Caller("SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId)) return CreateFileHandoff(draft);
        var agentId = Caller("AGENT_ID");
        var normalized = ManualTransfers.Parse(RelayJson.Write(draft), Agents());
        if (normalized.From != agentId)
            throw new RelayException("session_origin_mismatch", "调用上下文所属 Agent 与交接发送方不一致。");
        var host = Caller("HOST");
        var cwd = Caller("CWD");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(cwd))
            throw new RelayException("session_origin_incomplete", "调用适配器提供的来源会话上下文不完整。");
        return CreateFileHandoff(normalized, new OriginSession(host, sessionId, Caller("PROFILE") ?? "", cwd));
    }

    // New adapters emit TASKBATON_CALLER_*; old adapters still emit AGENT_RELAY_CALLER_*.
    private static string? Caller(string suffix) =>
        Environment.GetEnvironmentVariable("TASKBATON_CALLER_" + suffix) ??
        Environment.GetEnvironmentVariable("AGENT_RELAY_CALLER_" + suffix);

    public List<TaskSessionBinding> SessionBindings(string taskId) => store.Read(s =>
    {
        _ = s.Task(taskId);
        return s.Query("SELECT json FROM task_session_bindings WHERE task_id=$task ORDER BY attempt_id,turn,agent_id",
            row => RelayJson.Read<TaskSessionBinding>(row.GetString(0)), ("$task", taskId));
    });

    public TaskSessionBinding? TaskSession(string taskId, string agentId) => store.Read(s =>
    {
        var task = s.Task(taskId);
        return ReadTaskSession(s, task, agentId, SessionPurpose(task, agentId));
    });

    public TaskSessionBinding? TaskSession(string taskId, string agentId, string purpose) => store.Read(s =>
        ReadTaskSession(s, s.Task(taskId), agentId, purpose));

    /// <summary>
    /// A dedicated acceptance conversation is kept separate from the same Agent's work conversation, so a reviewer
    /// that is also the sender or recipient never overwrites the work binding.
    /// </summary>
    public static string SessionPurpose(TaskRecord task, string agentId) =>
        task.Collaboration is { IndependentAcceptance: true } review && review.ReviewerId == agentId &&
        (review.Phase == "review" || review.ReviewRound > 0)
            ? "review" : "work";

    public TaskSessionBinding BindTaskSession(string taskId, string agentId, SessionRecord session,
        string binding = "user_selected", string? purpose = null)
    {
        if (binding is not ("user_selected" or "manual"))
            throw new RelayException("session_binding_invalid", "已有会话只能由用户明确选择或手动输入。");
        if (string.IsNullOrWhiteSpace(session.SessionId))
            throw new RelayException("session_id_required", "请选择已有会话，或填写准确的会话 ID。");
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            RequireSessionChange(task, agentId);
            var resolvedPurpose = purpose ?? SessionPurpose(task, agentId);
            RequireExistingSessionAllowed(task, agentId, resolvedPurpose);
            var agent = Agent(s, agentId);
            var value = new TaskSessionBinding(task.TaskId, task.AttemptId, task.ManualTurn, agentId, agent.Host,
                session.SessionId, session.Cwd, session.Profile, session.SessionPath, binding, session.Visibility, Now,
                binding == "manual" ? "operator supplied an exact session ID" : "operator selected an existing session",
                resolvedPurpose == "review", resolvedPurpose);
            SaveTaskSession(s, task, value);
            ResetUncertainFileDelivery(task, agentId, resolvedPurpose);
            s.Save(task);
            return value;
        });
    }

    public TaskSessionBinding BindManualTaskSession(string taskId, string agentId, string sessionId,
        string? cwd = null, string? profile = null, string? sessionPath = null) =>
        BindTaskSession(taskId, agentId, new SessionRecord(sessionId, null, cwd, profile, sessionPath, "unknown", null), "manual");

    public void UseNewTaskSession(string taskId, string agentId, string? purpose = null) => store.Write(s =>
    {
        var task = s.Task(taskId); RequireSessionChange(task, agentId);
        var agent = Agent(s, agentId);
        purpose ??= SessionPurpose(task, agentId);
        var independentReview = purpose == "review";
        SaveTaskSession(s, task, new(task.TaskId, task.AttemptId, task.ManualTurn, agentId, agent.Host,
            null, independentReview ? ReadTaskSession(s, task, agentId, purpose)?.Cwd : null, null, null, "new", "unknown", Now,
            "operator chose a new session", independentReview, purpose));
        ResetUncertainFileDelivery(task, agentId, purpose); s.Save(task);
        return true;
    });

    public TaskSessionBinding RegisterOriginSession(string taskId, OriginSession origin) => store.Write(s =>
    {
        var task = s.Task(taskId);
        return RegisterOriginSession(s, task, origin);
    });

    private TaskSessionBinding RegisterOriginSession(StoreSession s, TaskRecord task, OriginSession origin)
    {
        RequireExistingSessionAllowed(task, task.RequesterId, SessionPurpose(task, task.RequesterId));
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

    private static TaskSessionBinding? ReadTaskSession(StoreSession s, TaskRecord task, string agentId, string purpose = "work") =>
        s.Query("SELECT json FROM task_session_bindings WHERE task_id=$task AND attempt_id=$attempt AND agent_id=$agent AND purpose=$purpose ORDER BY turn DESC LIMIT 1",
            row => RelayJson.Read<TaskSessionBinding>(row.GetString(0)), ("$task", task.TaskId),
            ("$attempt", task.AttemptId), ("$agent", agentId), ("$purpose", purpose)).SingleOrDefault();

    private void SaveTaskSession(StoreSession s, TaskRecord task, TaskSessionBinding value)
    {
        s.Execute("""
            INSERT INTO task_session_bindings(task_id,attempt_id,turn,agent_id,purpose,json) VALUES($task,$attempt,$turn,$agent,$purpose,$json)
            ON CONFLICT(task_id,attempt_id,turn,agent_id,purpose) DO UPDATE SET json=excluded.json
            """, ("$task", value.TaskId), ("$attempt", value.AttemptId), ("$turn", value.Turn),
            ("$agent", value.AgentId), ("$purpose", value.Purpose), ("$json", RelayJson.Write(value)));
        s.Event(task, "session.bound", value.AgentId, Now, RelayJson.Write(value));
    }

    private static void RequireSessionChange(TaskRecord task, string agentId)
    {
        var participant = agentId == task.RequesterId || agentId == task.RecipientId ||
            task.Collaboration is { } collaboration &&
            (agentId == collaboration.AuthorId || agentId == collaboration.ReviewerId);
        if (!participant)
            throw new RelayException("session_agent_mismatch", "会话必须属于本任务的发送方、接收方或验收方。");
        if (Terminal(task.State)) throw new RelayException("session_task_finished", "已结束任务不能更换会话。");
        if (task.CliFileTurn == task.ManualTurn && task.CliFileStatus is "submitting" or "submitted")
            throw new RelayException("session_delivery_active", "本轮已投递，请先等待结果，不要在处理期间更换会话。");
    }

    private static void ResetUncertainFileDelivery(TaskRecord task, string agentId, string purpose)
    {
        if ((task.AwaitingAgentId ?? task.RecipientId) != agentId || task.CliFileTurn != task.ManualTurn ||
            SessionPurpose(task, agentId) != purpose) return;
        task.CliFileTurn = null; task.CliFileStatus = null; task.CliFileSessionId = null; task.CliFileError = null;
    }

    private static void RequireExistingSessionAllowed(TaskRecord task, string agentId, string purpose)
    {
        if (task.ParallelGroupId is not null)
            throw new RelayException("parallel_new_session_required", "并行分工和汇合需要使用独立工作目录的新会话。");
        if (purpose == "review" || UsesIndependentReviewSession(task, agentId))
            throw new RelayException("review_session_required", "独立验收需要任务专用会话，请选择新建会话。");
    }
}
