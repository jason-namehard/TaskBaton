using System.ComponentModel;

namespace AgentRelay.Core;

public sealed record FileHandoffSyncResult(string[] UpdatedTaskIds, ManualReceiptError[] Errors, string[] SentTaskIds,
    ManualReceiptError[] DeliveryErrors);

public sealed partial class RelayService
{
    private static readonly TimeSpan MinimumCliTurn = TimeSpan.FromMinutes(1);

    public bool CanSendFileHandoff(TaskRecord task)
    {
        if (!task.IsFileHandoff || task.ManualContinuous || Terminal(task.State) || task.CliFileTurn == task.ManualTurn ||
            task.State is not (TaskState.AwaitingManual or TaskState.AwaitingReply)) return false;
        // A receipt can arrive while Relay's own call is still running; never start the next turn on top of it.
        if (task.CliFileTurn is not null && task.CliFileStatus == "submitting") return false;
        var recipient = task.AwaitingAgentId ?? task.RecipientId;
        var sender = recipient == task.RecipientId ? task.RequesterId : task.RecipientId;
        var extension = CliExtension(recipient);
        if (extension is null || extension.Block is not null) return false;
        var binding = TaskSession(task.TaskId, recipient);
        if (task.Collaboration?.Phase == "criteria" && binding?.SessionId is null) return false;
        if (binding?.SessionId is not null && CliRecipes.ResumeCommand(extension.Recipe) is null) return false;
        if (binding?.Binding == "ephemeral" || task.ManualTurn > 0 && binding is null) return false;
        return
            ConnectionPermissions().Any(p => p.From == sender && p.To == recipient && p.Decision == "allow");
    }

    /// <summary>Unattended sends need the exact capability already proven and no unresolved host blocker.</summary>
    public bool CanAutoDeliver(TaskRecord task)
    {
        if (!CanSendFileHandoff(task)) return false;
        var recipient = task.AwaitingAgentId ?? task.RecipientId;
        var extension = CliExtension(recipient)!;
        if (extension.Block is not null) return false;
        return TaskSession(task.TaskId, recipient)?.SessionId is not null ? extension.CanResume : extension.CanCreate;
    }

    public string? FileDeliveryMessage(TaskRecord task)
    {
        var recipient = task.AwaitingAgentId ?? task.RecipientId;
        var extension = CliExtension(recipient);
        if (extension?.Block is {} block) return block.Message;
        if (task.CliFileTurn == task.ManualTurn && task.CliFileStatus is "unknown" or "failed") return task.CliFileError;
        if (task.CliFileTurn is not null && task.CliFileStatus == "submitting")
            return "上一轮的投递仍在执行，本轮结果已提交但执行结束未知；确认宿主结束后再自动开始下一步。";
        if (!task.AutoDeliver || extension is null || !CanSendFileHandoff(task)) return null;
        var resume = TaskSession(task.TaskId, recipient)?.SessionId is not null;
        if (resume ? extension.CanResume : extension.CanCreate) return null;
        return resume
            ? "自动续接尚未验证：请在桥接测试确认同一会话的续接探针，或手动发送本轮并等待真实回执。自动投递目前不会启动。"
            : "自动新建会话尚未验证：请完成新建探针并确认可见，或手动发送本轮并等待真实回执。自动投递目前不会启动。";
    }

    public IReadOnlyList<string> AutoDeliveryCandidates() => Tasks()
        .Where(v => v.Task.IsFileHandoff && v.Task.AutoDeliver && CanAutoDeliver(v.Task))
        .Select(v => v.Task.TaskId).ToArray();

    /// <summary>Imports receipts and, when asked, sends every opted-in turn that is ready. GUI, CLI and MCP share this path.</summary>
    public async Task<FileHandoffSyncResult> SyncFileHandoffsAsync(bool deliver, CancellationToken cancellationToken = default)
    {
        var imported = ImportManualReceipts();
        var sent = new List<string>(); var failures = new List<ManualReceiptError>();
        if (deliver)
            foreach (var id in AutoDeliveryCandidates())
            {
                try
                {
                    await SendFileHandoffAsync(id, FileHandoffPrompt(Status(id).Task), cancellationToken);
                    sent.Add(id);
                }
                catch (RelayException error)
                {
                    // A send is refused before any process starts (changed task, missing session); report it and keep syncing others.
                    failures.Add(new(id, ManualReceiptPath(Status(id).Task), error.Message));
                }
            }
        return new(imported.UpdatedTaskIds, imported.Errors, sent.ToArray(), failures.ToArray());
    }

    public async Task<TaskView> SendFileHandoffAsync(string taskId, string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new RelayException("file_prompt_required", "文件交接提示不能为空。");
        var task = Status(taskId).Task;
        if (CliExtension(task.AwaitingAgentId ?? task.RecipientId)?.Block is {} blocked)
            throw new RelayException("cli_blocked", blocked.Message);
        if (!CanSendFileHandoff(task)) throw new RelayException("file_cli_unavailable", "当前方向未启用 CLI、本轮已发送，或没有可续接的明确会话。请检查连接图和会话选择，必要时手动传递。");
        var recipient = task.AwaitingAgentId ?? task.RecipientId;
        var extension = CliExtension(recipient)!;
        var binding = TaskSession(taskId, recipient);
        var resume = binding?.SessionId is not null;
        SessionRecord? validated = null;
        if (resume)
        {
            var check = await ValidateDiscoveredSessionAsync(recipient, binding!.SessionId!, cancellationToken);
            // A session this task created was reported by the host itself; resume still has to return the same ID.
            var created = check.Status == "unsupported" && binding.Binding == "created";
            if (check.Status != "exists" && !created)
                throw new RelayException("session_rebind_required", check.Message ??
                    "无法确认绑定会话仍然存在。请重新选择已有会话；不支持会话发现时仍可手动复制提示词。");
            validated = check.Session;
        }
        var command = resume ? CliRecipes.ResumeCommand(extension.Recipe)! : extension.Recipe.Create;
        if(!resume&&task.CliNewSessionCwd is {} newCwd)command=command with {WorkingDirectory=newCwd};
        if (resume) command = command with { WorkingDirectory = validated?.Cwd ?? binding!.Cwd ?? command.WorkingDirectory };
        if (resume)
        {
            var bound = binding!;
            var context = command.Environment is null ? new Dictionary<string,string>() : new(command.Environment);
            context["AGENT_RELAY_CALLER_AGENT_ID"] = recipient;
            context["AGENT_RELAY_CALLER_SESSION_ID"] = bound.SessionId!;
            context["AGENT_RELAY_CALLER_HOST"] = bound.Host;
            context["AGENT_RELAY_CALLER_CWD"] = command.WorkingDirectory ?? validated?.Cwd ?? Directory.GetCurrentDirectory();
            if (bound.Profile is not null) context["AGENT_RELAY_CALLER_PROFILE"] = bound.Profile;
            command = command with { Environment = context };
        }
        RequireTaskImages(task);
        // Only the first turn carries the original images; a later turn carries what the user selected for it.
        var images = await ImageDelivery.PrepareAsync(ImagesForTurn(task), cancellationToken);
        var startedAt = Now;
        store.Write(s =>
        {
            var current = s.Task(taskId);
            if (current.ManualTurn != task.ManualTurn || current.CliFileTurn == current.ManualTurn ||
                current.State is not (TaskState.AwaitingManual or TaskState.AwaitingReply) ||
                (current.AwaitingAgentId ?? current.RecipientId) != recipient || ReadCliExtension(s,recipient)?.Revision != extension.Revision ||
                ReadTaskSession(s,current,recipient) != binding)
                throw new RelayException("file_cli_changed", "任务已经变化，本次未重复发送。");
            RequirePermission(s,recipient==current.RecipientId?current.RequesterId:current.RecipientId,recipient);
            if (ReadCliExtension(s,recipient)?.Block is {} currentBlock)
                throw new RelayException("cli_blocked",currentBlock.Message);
            current.CliFileTurn = current.ManualTurn; current.CliFileStatus = "submitting";
            current.CliFileError = null; current.CliFileSessionId = null; current.Mode = "cli"; current.AdapterId = "cli:" + recipient;
            current.CliFileOperation = resume ? "resume" : "create"; current.CliFileRevision = extension.Revision;
            current.SendStartedAt = Now;
            s.Save(current); s.Event(current, "file.cli_submitting", "operator", Now, resume ? "resume " + binding!.SessionId : "create");
            return true;
        });
        // Print-mode hosts return only after the Agent finishes its turn, so the call may run until the turn deadline.
        var budget = task.DeadlineAt - Now;
        string status; string? session = null; string? error; CliFailure? failure = null; CliSidebarSyncResult? sidebarSync = null;
        var observed = new List<string>();
        void Observe(string id)
        {
            lock (observed)
            {
                if (string.IsNullOrWhiteSpace(id) || observed.Contains(id)) return;
                observed.Add(id);
            }
            // Persist immediately: a turn that later times out must still keep the exact conversation id.
            PersistObservedSession(task, recipient, id, command.WorkingDirectory);
        }
        try
        {
            var process = await CliRecipes.InvokePreparedAsync(command, CliRecipes.OutputFormatFor(extension.Recipe), prompt, binding?.SessionId, Home,
                budget > MinimumCliTurn ? budget : MinimumCliTurn, cancellationToken, images, Observe);
            var receipt = CliRecipes.Receipt(process, CliRecipes.OutputFormatFor(extension.Recipe));
            if (receipt is { } received && CliExtension(recipient) is { } current && current.Revision == extension.Revision)
                sidebarSync = ZcodeTaskIndexVisibility.Sync(current, received.SessionId, ZcodeUserDirectory, Home);
            var matching = receipt is not null && (!resume || receipt.Value.SessionId == binding!.SessionId);
            status = matching ? "submitted" : "unknown";
            session = receipt?.SessionId;
            if (!matching && receipt is null) failure = CliFailureDiagnostics.Classify(process, extension.Recipe);
            error = matching ? null : resume && receipt is not null
                ? "续接返回了不同的会话 ID，本轮结果未确认。原绑定保持不变，请检查目标会话，不要重复发送。" : process.TimedOut
                ? "CLI 已启动但到达本轮时限仍未结束，已停止等待，可能已提交。请检查目标会话，不要重复发送。"
                : failure!.Message;
        }
        catch (Win32Exception launch)
        {
            status = "failed"; error = "无法启动 CLI：" + launch.Message;
        }
        catch (RelayException rejected) when (rejected.Code == "cli_image_unsupported")
        {
            status = "failed"; error = rejected.Message;
        }
        // Nothing reached the Agent: the time went to starting or connecting to the host, so the turn keeps its budget.
        var reachedAgent = resume || observed.Count > 0 || status == "submitted";
        var hostWait = reachedAgent ? TimeSpan.Zero : Now - startedAt;
        store.Write(s =>
        {
            var current = s.Task(taskId);
            if (current.AttemptId == task.AttemptId && sidebarSync is { } sidebar &&
                (sidebar.Status == "failed" || sidebar.WindowActivated == false))
                s.Event(current, "zcode.sidebar_sync_failed", "relay", Now, sidebar.Message ?? sidebar.Status);
            if (current.AttemptId == task.AttemptId && status == "submitted")
            {
                var agent = Agent(s, recipient);
                var value = new TaskSessionBinding(taskId, task.AttemptId, task.ManualTurn, recipient, agent.Host,
                    session, validated?.Cwd ?? binding?.Cwd ?? command.WorkingDirectory,
                    validated?.Profile ?? binding?.Profile, validated?.SessionPath ?? binding?.SessionPath,
                    resume ? binding!.Binding : session is null ? "ephemeral" : "created",
                    validated?.Visibility ?? binding?.Visibility ?? "unknown", Now,
                    resume ? "adapter resumed the exact bound session" : session is null
                        ? "CLI completed without a persistent session ID" : "adapter parsed the created session ID");
                SaveTaskSession(s, current, value);
            }
            if (failure is { Blocking: true } && ReadCliExtension(s, recipient) is { } stored && stored.Revision == extension.Revision)
                SaveCliExtension(s, stored with { Block = new(failure.Kind, failure.Message, Now) });
            // Print-mode Agents usually write the turn's receipt before the CLI returns.
            if (status == "submitted" && current.AttemptId == task.AttemptId && (current.ManualTurn > task.ManualTurn ||
                current.ManualReceiptJson is { } imported && RelayJson.Read<ManualReceipt>(imported).Turn == task.ManualTurn))
                RecordCliDeliveryEvidence(s, current, recipient, task.ManualTurn, resume ? "resume" : "create", extension.Revision, session);
            // A receipt may have finished this turn while the CLI was returning its output.
            if (current.AttemptId == task.AttemptId && current.CliFileTurn == task.ManualTurn &&
                (current.ManualTurn == task.ManualTurn || current.CliFileStatus == "submitting"))
            {
                current.CliFileStatus = status; current.CliFileError = error; current.CliFileSessionId = session;
                // A matching turn result proves this host's turn ended; a timeout or failure proves nothing.
                current.HostTurnEnded = status == "submitted" ? true : status == "failed" ? false : null;
                if (hostWait > TimeSpan.Zero)
                {
                    current.DeadlineAt += hostWait;
                    current.HostWaitSeconds += (int)Math.Round(hostWait.TotalSeconds);
                    s.Event(current, "file.host_wait", "relay", Now,
                        $"waited {current.HostWaitSeconds}s for the host; the turn keeps its work time");
                }
                if (!Terminal(current.State) && current.ManualTurn == task.ManualTurn && status == "submitted" &&
                    current.State is TaskState.AwaitingManual or TaskState.AwaitingReply)
                    current.State = TaskState.Dispatched;
                if (status == "submitted") current.DeliveredAt = Now;
                s.Save(current); s.Event(current, "file.cli_" + status, "operator", Now, error ?? session ?? "");
            }
            return true;
        });
        return Status(taskId);
    }

    /// <summary>The host named the conversation before the turn ended; keep it now, not after the turn returns.</summary>
    private void PersistObservedSession(TaskRecord task, string agentId, string sessionId, string? cwd) => store.Write(s =>
    {
        var current = s.Task(task.TaskId);
        if (current.AttemptId != task.AttemptId || current.ManualTurn != task.ManualTurn ||
            (current.AwaitingAgentId ?? current.RecipientId) != agentId) return false;
        var existing = ReadTaskSession(s, current, agentId);
        if (existing?.SessionId is not null) return false;
        var agent = Agent(s, agentId);
        var value = new TaskSessionBinding(current.TaskId, current.AttemptId, current.ManualTurn, agentId, agent.Host,
            sessionId, cwd ?? existing?.Cwd, existing?.Profile, existing?.SessionPath, "created",
            existing?.Visibility ?? "unknown", Now, "host reported this session id before the turn finished");
        SaveTaskSession(s, current, value);
        s.Event(current, "session.observed_early", agentId, Now, sessionId);
        return true;
    });

    /// <summary>The user checked the target conversation and confirms this turn never arrived, so one new send is allowed.</summary>
    public TaskView AllowFileHandoffResend(string taskId, string reason, bool approved)
    {
        if (!approved) throw new RelayException("approval_required", "重新发送可能让 Agent 重复处理，请确认目标会话确实没有收到后再批准。", 4);
        RequireReason(reason);
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            if (Terminal(task.State) || task.CliFileTurn != task.ManualTurn || task.CliFileStatus is not ("unknown" or "failed"))
                throw new RelayException("file_resend_not_needed", "本轮没有待确认的 CLI 投递。", 5);
            task.CliFileTurn = null; task.CliFileStatus = null; task.CliFileError = null; task.CliFileSessionId = null;
            task.CliFileOperation = null; task.CliFileRevision = null;
            s.Save(task); s.Event(task, "file.cli_resend_allowed", "operator", Now, reason);
            return View(task, s);
        });
    }

    private void RecordCliDeliveryEvidence(StoreSession s, TaskRecord task, ManualReceipt receipt)
    {
        if (task.CliFileTurn == receipt.Turn && task.CliFileStatus == "submitted" && task.CliFileOperation is not null)
            RecordCliDeliveryEvidence(s, task, receipt.Sender, receipt.Turn, task.CliFileOperation, task.CliFileRevision, task.CliFileSessionId);
    }

    /// <summary>A CLI-delivered turn whose Agent wrote the receipt proves the recipe end to end for that operation.</summary>
    private void RecordCliDeliveryEvidence(StoreSession s, TaskRecord task, string agentId, int turn, string operation,
        string? revision, string? sessionId)
    {
        var extension = ReadCliExtension(s, agentId);
        if (extension is null || extension.Revision != revision) return;
        var evidence = new CliDeliveryEvidence(task.TaskId, turn, sessionId, Now);
        var updated = operation == "create"
            ? extension.CreateEvidence is null ? extension with { CreateEvidence = evidence } : extension
            : extension.ResumeEvidence is null ? extension with { ResumeEvidence = evidence } : extension;
        if (updated.Block is not null) updated = updated with { Block = null };
        if (updated == extension) return;
        SaveCliExtension(s, updated);
        s.Event(task, "cli.delivery_verified", agentId, Now, operation);
    }
}
