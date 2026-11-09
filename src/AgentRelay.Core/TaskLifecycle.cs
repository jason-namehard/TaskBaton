namespace AgentRelay.Core;

public sealed partial class RelayService
{
    public async Task<TaskView> DispatchAsync(DispatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Mode is not ("auto" or "confirm" or "manual") || string.IsNullOrWhiteSpace(request.Content) ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new RelayException("invalid_request", "mode 必须为 auto/confirm/manual；请求内容与幂等键不能为空。");
        if (request.OriginSession is { } origin && (string.IsNullOrWhiteSpace(origin.Host) ||
            string.IsNullOrWhiteSpace(origin.SessionId) || string.IsNullOrWhiteSpace(origin.ConfigProfile) || !Path.IsPathFullyQualified(origin.Cwd)))
            throw new RelayException("invalid_origin", "origin_session 必须指定 host、session_id、config_profile 和绝对 cwd。");
        var settings = Settings();
        var task = store.Write(s =>
        {
            var input = RelayJson.Write(request);
            var previous = s.Query("SELECT input_json,json FROM tasks WHERE requester=$from AND idempotency_key=$key",
                r => (Input: r.GetString(0), Task: RelayJson.Read<TaskRecord>(r.GetString(1))),
                ("$from", request.From), ("$key", request.IdempotencyKey)).SingleOrDefault();
            if (previous.Task is not null)
            {
                if (previous.Input != input) throw new RelayException("idempotency_conflict", "同一派发幂等键对应不同内容。", 5);
                return previous.Task;
            }
            var requester = Agent(s, request.From);
            if (request.AdapterId.StartsWith("cli:", StringComparison.Ordinal) && request.AdapterId != "cli:" + request.To)
                throw new RelayException("cli_recipient_mismatch", "CLI 调用方案必须属于接收 Agent。");
            RequireDispatchAccess(s, request.From, request.To, request.Mode, request.AdapterId);
            var resumer = s.Json<ResumerRecord>("SELECT json FROM resumers WHERE agent_id=$id", ("$id", request.From));
            if (request.OriginSession is { } target && resumer is not null)
                CodexSessionResumer.RequireBinding(requester, resumer, target);
            var now = Now;
            var deadline = request.DeadlineAt ?? now.AddMinutes(settings.TaskTimeoutMinutes);
            if (deadline <= now) throw new RelayException("invalid_deadline", "截止时间必须在未来。");
            var taskId = Guid.NewGuid().ToString();
            var attemptId = Guid.NewGuid().ToString();
            var value = new TaskRecord
            {
                TaskId = taskId, RequesterId = request.From, RecipientId = request.To, AttemptId = attemptId,
                AdapterId = request.AdapterId, Mode = request.Mode, CreatedAt = now, DeadlineAt = deadline,
                State = InitialState(s, request.Mode, request.AdapterId), RequestPath = "",
                ResultPath = Path.Combine(workspace.AttemptDirectory(taskId, attemptId), "result.md"), OriginSession = request.OriginSession,
                ResumeAdapter = request.OriginSession is null ? null : resumer
            };
            var artifact = workspace.Publish(value, "request", request.Content, now, settings.ArtifactTtlDays);
            value.RequestPath = artifact.Path;
            s.Execute("INSERT INTO tasks VALUES($id,$from,$key,$input,$json)", ("$id", taskId),
                ("$from", request.From), ("$key", request.IdempotencyKey), ("$input", input), ("$json", RelayJson.Write(value)));
            if (request.OriginSession is not null) RegisterOriginSession(s, value, request.OriginSession);
            AddArtifact(s, artifact);
            Queue(s, value);
            s.Event(value, "task.created", request.From, now);
            s.Notify(value, "waiting", value.State == TaskState.AwaitingManual ? "待手工提交给接收方。" : "任务已保存，正在等待处理。", now);
            return value;
        });
        await DeliverPendingAsync(task.TaskId, cancellationToken);
        return Status(task.TaskId);
    }

    public async Task<TaskView> ApproveAsync(string taskId, CancellationToken cancellationToken = default)
    {
        store.Write(s =>
        {
            var task = s.Task(taskId);
            if (task.State != TaskState.AwaitingConfirmation) throw new RelayException("state_conflict", "任务未处于待批准状态。", 5);
            RequireLive(task);
            RequireDispatchAccess(s, task.RequesterId, task.RecipientId, task.Mode, task.AdapterId);
            task.State = Available(s, task.AdapterId) ? TaskState.Created : TaskState.AwaitingManual;
            Queue(s, task); s.Save(task); s.Event(task, "task.approved", "operator", Now);
            return task;
        });
        await DeliverPendingAsync(taskId, cancellationToken);
        return Status(taskId);
    }

    public TaskView Acknowledge(string taskId, string attemptId, string sender, string key, bool start = false) =>
        Callback(taskId, attemptId, sender, key, new { operation = start ? "start" : "ack" }, (s, task) =>
        {
            RequireLive(task);
            var allowed = task.State is TaskState.AwaitingManual or TaskState.Dispatching or TaskState.Dispatched or TaskState.DeliveryUnknown or TaskState.Acknowledged;
            if (!allowed) throw new RelayException("state_conflict", "当前状态不能接受确认/开始事件。", 5);
            task.State = start ? TaskState.Running : TaskState.Acknowledged;
            s.Execute("UPDATE outbox SET state='settled' WHERE task_id=$id", ("$id", task.TaskId));
            s.Save(task); s.Event(task, start ? "task.started" : "task.acknowledged", sender, Now);
        });

    public TaskView Complete(string taskId, string attemptId, string sender, string key, string resultPath,
        bool failed = false, string? errorCode = null, string? errorMessage = null, bool? sideEffectsPossible = null)
    {
        if (failed && (string.IsNullOrWhiteSpace(errorCode) || string.IsNullOrWhiteSpace(errorMessage)))
            throw new RelayException("invalid_failure", "失败回调必须提供 error-code 和 message。");
        var full = workspace.RequireManaged(resultPath);
        return Callback(taskId, attemptId, sender, key,
            new { operation = failed ? "fail" : "complete", path = full, errorCode, errorMessage, sideEffectsPossible }, (s, task) =>
        {
            if (task.IsFileHandoff)
                throw new RelayException("manual_receipt_required", "文件交接请按交接提示写回执，无需导入或生成另一份回稿快照。");
            if (task.State is not (TaskState.AwaitingManual or TaskState.Dispatching or TaskState.Dispatched or TaskState.DeliveryUnknown or
                TaskState.Acknowledged or TaskState.Running or TaskState.TimedOut or TaskState.Cancelled))
                throw new RelayException("state_conflict", "该状态不能提交最终结果。", 5);
            RequireAttemptPath(task, full);
            if (!File.Exists(full)) throw new RelayException("artifact_missing", "结果文件不存在。", 3);
            var registered = s.Json<ArtifactRecord>("SELECT json FROM artifacts WHERE path=$path", ("$path", full));
            if (registered is not null && (registered.State != "available" || registered.ExpiresAt <= Now))
                throw new RelayException("artifact_expired", "结果产物已过期或正在清理。", 3);
            var content = File.ReadAllText(full);
            if (string.IsNullOrWhiteSpace(content)) throw new RelayException("empty_result", "结果文件不能为空。");
            var settings = s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!;
            if (registered is null)
                AddArtifact(s, new ArtifactRecord(Guid.NewGuid().ToString(), taskId, attemptId, "submission", full,
                    Now, Now.AddDays(settings.ArtifactTtlDays), "available",
                    RelayOwned: SameWorkspacePath(full, Path.Combine(workspace.AttemptDirectory(taskId, attemptId), "result.md"))));
            // Snapshot the supplied result so later edits cannot change the accepted result.
            var artifact = workspace.Publish(task, failed ? "failure" : "result", content, Now, settings.ArtifactTtlDays);
            AddArtifact(s, artifact);
            if (task.State == TaskState.Cancelled)
            {
                s.Event(task, "task.late_result", sender, Now, artifact.ArtifactId);
                s.Notify(task, "late_result", "已取消任务收到晚到结果；任务仍保持取消。", Now);
                return;
            }
            var late = task.State == TaskState.TimedOut || task.DeadlineAt <= Now;
            task.ResultPath = artifact.Path;
            task.State = failed ? TaskState.Failed : TaskState.Succeeded;
            task.ErrorCode = errorCode; task.ErrorMessage = errorMessage;
            task.SideEffectsPossible = sideEffectsPossible;
            s.Execute("UPDATE outbox SET state='settled',lease_until=NULL WHERE task_id=$id", ("$id", taskId));
            s.Save(task);
            s.Event(task, failed ? "task.failed" : "task.completed", sender, Now, late ? "late_completion" : "");
            s.Notify(task, failed ? "failed" : "ready", failed ? "接收方报告失败，请查看失败文件。" : "结果已就绪，可复制继续提示词。", Now);
            QueueContinuation(s, task, failed ? "failed" : "ready");
        });
    }

    private TaskView Callback(string taskId, string attemptId, string sender, string key, object input,
        Action<StoreSession, TaskRecord> apply)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new RelayException("missing_key", "回调幂等键不能为空。");
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            if (task.RecipientId != sender) throw new RelayException("callback_denied", "只有任务接收方可以提交回调。", 3);
            if (task.AttemptId != attemptId) throw new RelayException("stale_attempt", "旧尝试不能覆盖当前任务。", 5);
            var json = RelayJson.Write(input);
            var previous = s.Query("SELECT input_json,result_json FROM callbacks WHERE task_id=$task AND attempt_id=$attempt AND sender=$sender AND key=$key",
                r => (Input: r.GetString(0), Result: r.GetString(1)), ("$task", taskId), ("$attempt", attemptId), ("$sender", sender), ("$key", key)).SingleOrDefault();
            if (previous.Result is not null)
            {
                if (previous.Input != json) throw new RelayException("idempotency_conflict", "回调幂等键已被不同内容使用。", 5);
                return View(RelayJson.Read<TaskView>(previous.Result).Task, s);
            }
            apply(s, task);
            var result = View(task, s);
            s.Execute("INSERT INTO callbacks VALUES($task,$attempt,$sender,$key,$input,$result)",
                ("$task", taskId), ("$attempt", attemptId), ("$sender", sender), ("$key", key),
                ("$input", json), ("$result", RelayJson.Write(result)));
            return result;
        });
    }

    public ArtifactRecord RegisterArtifact(string taskId, string attemptId, string kind, string path)
    {
        if (kind is not ("result" or "failure")) throw new RelayException("invalid_kind", "kind 必须为 result 或 failure。");
        var content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content)) throw new RelayException("empty_artifact", "产物不能为空。");
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            if (task.AttemptId != attemptId) throw new RelayException("stale_attempt", "尝试已失效。", 5);
            if (task.State is TaskState.Cancelled or TaskState.Succeeded or TaskState.Failed)
                throw new RelayException("state_conflict", "终态任务不能登记新结果。", 5);
            var settings = s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!;
            var artifact = workspace.Publish(task, kind, content, Now, settings.ArtifactTtlDays);
            AddArtifact(s, artifact); s.Event(task, "artifact.updated", "operator", Now, artifact.ArtifactId);
            return artifact;
        });
    }

    public TaskView Cancel(string taskId, string reason)
    {
        RequireReason(reason);
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            if (task.State == TaskState.Succeeded || task.State == TaskState.Cancelled)
                throw new RelayException("state_conflict", "任务已成功或已取消。", 5);
            task.State = TaskState.Cancelled;
            task.ResumeStatus = "manual_required";
            s.Save(task); s.Event(task, "task.cancelled", "operator", Now, reason);
            s.Execute("UPDATE outbox SET state='settled',lease_until=NULL WHERE task_id=$id", ("$id", taskId));
            s.Notify(task, "cancelled", "已停止 Relay 后续派发；宿主执行未被终止。", Now);
            return View(task, s);
        });
    }

    public async Task<TaskView> RetryAsync(string taskId, string reason, bool approved, CancellationToken cancellationToken = default)
    {
        RequireReason(reason);
        if (!approved) throw new RelayException("approval_required", "重新执行可能产生重复副作用，请明确使用 --approve 后重试。", 4);
        var settings = Settings();
        store.Write(s =>
        {
            var task = s.Task(taskId);
            if (task.IsFileHandoff)
                throw new RelayException("manual_new_handoff_required", "文件交接不会自动重发，请确认结果后另建交接。");
            if (task.State is not (TaskState.Failed or TaskState.TimedOut or TaskState.DeliveryUnknown))
                throw new RelayException("state_conflict", "只有失败、超时或投递未知的任务可重新执行。", 5);
            RequireDispatchAccess(s, task.RequesterId, task.RecipientId, task.Mode, task.AdapterId);
            var input = s.Query("SELECT input_json FROM tasks WHERE id=$id", r => RelayJson.Read<DispatchRequest>(r.GetString(0)), ("$id", taskId)).Single();
            task.AttemptId = Guid.NewGuid().ToString(); task.ExecutionAttempt++;
            task.DeadlineAt = Now.AddMinutes(settings.TaskTimeoutMinutes);
            task.ErrorCode = null; task.ErrorMessage = null; task.SideEffectsPossible = null;
            task.ResumeStatus = "manual_required";
            var artifact = workspace.Publish(task, "request", input.Content, Now, settings.ArtifactTtlDays);
            AddArtifact(s, artifact); task.RequestPath = artifact.Path;
            task.ResultPath = Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId), "result.md");
            task.State = InitialState(s, task.Mode, task.AdapterId);
            s.Execute("DELETE FROM outbox WHERE task_id=$id", ("$id", taskId));
            Queue(s, task); s.Save(task); s.Event(task, "task.retry_requested", "operator", Now, reason);
            s.Notify(task, "waiting", "新执行尝试已创建。", Now);
            return task;
        });
        await DeliverPendingAsync(taskId, cancellationToken);
        return Status(taskId);
    }

    public TaskView Status(string id) => store.Read(s =>
    {
        var task = s.Task(id);
        return View(task, s) with { Execution = Execution(s, id, task.AttemptId), Continuation = LatestContinuation(s, task),
            Progress = task.IsFileHandoff ? FileHandoffProgress(task) : null };
    });
    public List<TaskView> Tasks() => store.Read(s => s.Query("SELECT json FROM tasks ORDER BY rowid DESC", r => View(RelayJson.Read<TaskRecord>(r.GetString(0)), s)));
    public List<InboxItem> Inbox() => store.Read(s => s.Query("SELECT id,task_id,event,message,created_at,is_read FROM notifications ORDER BY id DESC",
        r => new InboxItem(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), DateTimeOffset.Parse(r.GetString(4)), r.GetBoolean(5))));
    public List<AuditEvent> Audit(string? taskId = null) => store.Read(s => s.Query(
        "SELECT seq,json FROM events WHERE $task IS NULL OR task_id=$task ORDER BY seq",
        r => RelayJson.Read<AuditEvent>(r.GetString(1)) with { Sequence = r.GetInt64(0) }, ("$task", taskId)));
    public List<ArtifactRecord> Artifacts(string taskId) => store.Read(s => s.Query("SELECT json FROM artifacts WHERE task_id=$id",
        r => RelayJson.Read<ArtifactRecord>(r.GetString(0)), ("$id", taskId)));

    private TaskView View(TaskRecord task, StoreSession? session = null)
    {
        if (task.ResumeStatus == "operator_completed")
            return new TaskView(task, false, "inspect", "用户已标记交接完成，未据此验证 Agent 执行结果。无需继续传递。");
        if (task.IsFileHandoff) return ManualView(task);
        var overdue = !Terminal(task.State) && task.DeadlineAt <= Now;
        var ready = task.State is TaskState.Succeeded or TaskState.Failed;
        ArtifactRecord? ResultArtifact(StoreSession s) => s.Json<ArtifactRecord>("SELECT json FROM artifacts WHERE path=$path", ("$path", task.ResultPath));
        var artifact = ready ? session is null ? store.Read(ResultArtifact) : ResultArtifact(session) : null;
        var resultExists = artifact is { State: "available" } && artifact.ExpiresAt > Now && File.Exists(task.ResultPath);
        var action = overdue ? "recover" : task.State switch
        {
            TaskState.AwaitingConfirmation => "approve",
            TaskState.AwaitingManual => "copy_request_prompt",
            TaskState.DeliveryUnknown => "inspect_delivery_evidence",
            TaskState.Failed or TaskState.TimedOut => "inspect_or_retry",
            TaskState.Succeeded => "copy_continuation_prompt",
            TaskState.Cancelled => "inspect",
            _ => "end_turn_and_wait"
        };
        var prompt = task.State == TaskState.AwaitingManual
            ? $"任务 {task.TaskId} 尚待手工提交给 {task.RecipientId}。请求：{task.RequestPath}；尝试：{task.AttemptId}；结果应写入：{task.ResultPath}。接收后显式 ack，完成后显式 complete/fail。"
            : ready
                ? resultExists
                    ? $"继续处理 Agent Relay 任务 {task.TaskId}，状态 {task.State}。请求：{task.RequestPath}；结果：{task.ResultPath}。请读取结果和验证信息，再继续原任务；无需重复派发。"
                    : $"任务 {task.TaskId} 的结果文件已过期或缺失，请检查任务记录并决定重新生成。"
                : $"任务 {task.TaskId} 已保存，状态 {task.State}，正在等待结果文件 {task.ResultPath}。请结束当前轮；收到继续消息或用户粘贴提示词后再处理。截止时间：{task.DeadlineAt:O}。";
        if (overdue || task.State == TaskState.TimedOut) prompt = $"任务 {task.TaskId} 已超过截止时间，未获得有效完成结果。请运行 recover 或检查证据；不要自动重复派发。";
        if (task.State == TaskState.DeliveryUnknown) prompt = $"任务 {task.TaskId} 的投递状态未知，可能已被接收。请检查运输证据，禁止自动重发。";
        if (task.State == TaskState.Cancelled) prompt = $"任务 {task.TaskId} 已取消。请检查审计与可能的晚到结果，不要自动继续或重复派发。";
        if (!Terminal(task.State) && task.ErrorCode == "execution_unknown")
        {
            action = "inspect_execution_evidence";
            prompt = $"任务 {task.TaskId} 的执行结果未知。请检查 task execution 证据；不能自动再次启动 Agent。";
        }
        return new TaskView(task, overdue, action, prompt);
    }

    private TaskState InitialState(StoreSession s, string mode, string adapter) => mode switch
    {
        "manual" => TaskState.AwaitingManual,
        "confirm" => TaskState.AwaitingConfirmation,
        _ => Available(s, adapter) ? TaskState.Created : TaskState.AwaitingManual
    };

    private bool Available(StoreSession s, string id)
    {
        if (adapters.ContainsKey(id)) return true;
        if (id.StartsWith("cli:", StringComparison.Ordinal)) return ReadCliExtension(s, id[4..]) is { CanCreate: true };
        var adapter = s.Json<AdapterRecord>("SELECT json FROM adapters WHERE id=$id", ("$id", id));
        return adapter is { VerificationStatus: "passed" } && File.Exists(adapter.Executable);
    }

    private static void RequireDispatchAccess(StoreSession s, string from, string to, string mode, string adapter)
    {
        var sender = Agent(s, from);
        var recipient = Agent(s, to);
        if (mode == "manual") return;
        RequirePermission(s, from, to);
        if (!adapter.StartsWith("cli:", StringComparison.Ordinal))
        {
            RequirePaths(sender);
            RequirePaths(recipient);
        }
    }

    private static void Queue(StoreSession s, TaskRecord task)
    {
        if (task.State == TaskState.Created)
            s.Execute("INSERT INTO outbox VALUES($task,$attempt,'pending',0,NULL,NULL)", ("$task", task.TaskId), ("$attempt", task.AttemptId));
    }

    private void RequireLive(TaskRecord task)
    {
        if (task.DeadlineAt <= Now) throw new RelayException("deadline_exceeded", "任务已超过截止时间，请先 recover。", 5);
    }

    private void RequireAttemptPath(TaskRecord task, string full)
    {
        var directory = workspace.AttemptDirectory(task.TaskId, task.AttemptId) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new RelayException("artifact_mismatch", "产物不属于当前任务尝试。", 3);
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new RelayException("missing_reason", "需要填写操作原因。");
    }
}
