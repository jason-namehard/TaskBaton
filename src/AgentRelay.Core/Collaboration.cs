namespace AgentRelay.Core;

public sealed record CollaborationOptions(string AuthorId, string? Criteria = null, int MaxReviewRounds = 5,
    bool GenerateCriteria = false, bool ReviewerFinal = false, bool IndependentAcceptance = false);
public sealed record FileHandoffOptions(CollaborationOptions? Collaboration = null, bool AutoDeliver = false,
    int? TurnTimeoutMinutes = null,bool ManualContinuous = false, RelayPlanOptions? RelayPlan = null);

public sealed record CollaborationStep(int Turn, string AgentId, string Phase, string Outcome, DateTimeOffset At,
    string? ResultPath = null, string[]? OpenItems = null, string? Note = null);

public sealed class CollaborationRecord
{
    public required string AuthorId { get; set; }
    public required string ReviewerId { get; set; }
    public string? Criteria { get; set; }
    public bool GenerateCriteria { get; set; }
    public bool ReviewerFinal { get; set; }
    public bool IndependentAcceptance { get; set; }
    public int MaxReviewRounds { get; set; }
    // implement → review → (revise → review)* → accept → delivered
    public required string Phase { get; set; }
    public int ReviewRound { get; set; }
    public string? SubmissionPath { get; set; }
    public string? ApprovedSubmissionPath { get; set; }
    public string[] OpenItems { get; set; } = [];
    public List<CollaborationStep> History { get; set; } = [];
    public string? Deliverable { get; set; }
}

/// <summary>What the board shows: who holds the task, how the turn travels and whether the user is needed.</summary>
public sealed record HandoffProgress(string? CurrentAgentId, string? Phase, string WaitingOn, string Delivery,
    int Turn, int? MaxTurns, int? ReviewRound, int? MaxReviewRounds, string? NextIfPassed, string? NextIfChanged,
    string? PauseReason, string? PauseMessage, bool Finished, string? Completion, string? DeliveryMessage = null,
    RelayStatus? Status = null);

/// <summary>
/// Separate facts, in the order they become true: Relay sent, the host confirmed the turn, the Agent declared it
/// received and working, the turn was submitted, the flow ended. An Agent's declaration is never shown as proof.
/// </summary>
public sealed record RelayStatus(string Send, string Agent, string Host, string Flow, bool NeedsUser, string? Note,
    int HandoffsDone = 0, int? HandoffsTotal = null, string? CompletionNote = null);

public sealed partial class RelayService
{
    public const int MaxReviewRounds = 20;

    internal static string[] AllowedOutcomes(string phase) => phase switch
    {
        "criteria" => ["criteria_ready"],
        "implement" or "revise" => ["submitted"],
        "review" => ["changes_requested", "approved"],
        "accept" => ["accepted", "submitted"],
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown collaboration phase.")
    };

    internal static void ValidateFileHandoffOptions(FileHandoffOptions options, string requesterId, string recipientId)
    {
        if (options.TurnTimeoutMinutes is < 1 or > 43200)
            throw new RelayException("handoff_turn_timeout", "单轮时限为 1–43200 分钟。");
        if (options.Collaboration is not null && options.RelayPlan is not null)
            throw new RelayException("handoff_options_conflict", "请在验收循环和定次接力之间选择一种交接方式。");
        if(options.ManualContinuous&&(options.Collaboration is not null||options.AutoDeliver||options.RelayPlan is not null))
            throw new RelayException("manual_options_invalid","手动连续传递不配置自动投递或预设验收角色。");
        if (options.Collaboration is not { } collaboration) return;
        if (collaboration.AuthorId != requesterId && collaboration.AuthorId != recipientId)
            throw new RelayException("collaboration_author", "作者必须是本任务的发送方或接收方。");
        if (collaboration.ReviewerFinal && !collaboration.GenerateCriteria && string.IsNullOrWhiteSpace(collaboration.Criteria))
            throw new RelayException("collaboration_criteria_required", "请填写验收标准，或选择由发起方 Agent 拟定。");
        if (collaboration.MaxReviewRounds is < 0 or > MaxReviewRounds)
            throw new RelayException("collaboration_rounds", $"验收轮数为 0–{MaxReviewRounds}，0 表示不设上限。");
    }

    public TaskView ConfigureFileHandoff(string taskId, FileHandoffOptions options) => store.Write(s =>
    {
        var task = s.Task(taskId);
        if (!task.IsFileHandoff || task.State != TaskState.AwaitingManual || task.ManualTurn != 0 ||
            task.ManualReceiptJson is not null || task.CliFileTurn is not null || task.HandoffPlan is not null ||
            task.RelaySteps is not null || task.ParallelGroupId is not null)
            throw new RelayException("handoff_options_started", "协作方式只能在首次投递和回执之前设置。");
        ValidateFileHandoffOptions(options, task.RequesterId, task.RecipientId);
        if (options.RelayPlan is { } relayPlan)
            return ConfigureRelayPlan(s, task, relayPlan with
            {
                TurnTimeoutMinutes = relayPlan.TurnTimeoutMinutes ?? options.TurnTimeoutMinutes
            });
        task.AwaitingAgentId = task.RecipientId;
        if (options.Collaboration is { } collaboration)
        {
            var reviewer = collaboration.AuthorId == task.RequesterId ? task.RecipientId : task.RequesterId;
            var reviewFirst = task.RecipientId == reviewer;
            task.Collaboration = new CollaborationRecord
            {
                AuthorId = collaboration.AuthorId, ReviewerId = reviewer,
                Criteria = string.IsNullOrWhiteSpace(collaboration.Criteria) ? null : collaboration.Criteria.Trim(),
                GenerateCriteria = collaboration.GenerateCriteria, ReviewerFinal = collaboration.ReviewerFinal,
                IndependentAcceptance = collaboration.IndependentAcceptance,
                MaxReviewRounds = collaboration.MaxReviewRounds,
                Phase = collaboration.GenerateCriteria ? "criteria" : reviewFirst ? "review" : "implement", ReviewRound = reviewFirst ? 1 : 0,
                // When the sender already holds the work, the original request file is the first submission.
                SubmissionPath = reviewFirst ? task.RequestPath : null
            };
            if (collaboration.GenerateCriteria) task.AwaitingAgentId = task.RequesterId;
            EnsureIndependentReviewSession(s, task, task.Collaboration);
        }
        else task.Collaboration = null;
        task.AutoDeliver = options.AutoDeliver;
        task.ManualContinuous = options.ManualContinuous;
        task.TurnTimeoutMinutes = options.TurnTimeoutMinutes;
        task.DeadlineAt = Now + TurnTimeout(s, task);
        s.Save(task);
        s.Event(task, "handoff.configured", "operator", Now, RelayJson.Write(options));
        return ManualView(task);
    });

    public TaskView ContinueFileHandoff(string taskId, string? note = null, int extraRounds = 0) => store.Write(s =>
        ContinueFileHandoff(s, s.Task(taskId), note, extraRounds));

    private TaskView ContinueFileHandoff(StoreSession s, TaskRecord task, string? note, int extraRounds)
    {
        if (task.State != TaskState.Paused || task.PauseReason is null || task.PausedFrom is null)
            throw new RelayException("handoff_not_paused", "只有已暂停的交接可以继续。", 5);
        if (extraRounds is < 0 or > MaxReviewRounds)
            throw new RelayException("handoff_extra_rounds", $"追加轮数为 0–{MaxReviewRounds}。");
        var reason = task.PauseReason;
        switch (reason)
        {
            case "round_limit":
                if (extraRounds < 1) throw new RelayException("handoff_extra_rounds", "轮数已用完，继续前请追加至少 1 轮。");
                if (task.Collaboration is { } collaboration) collaboration.MaxReviewRounds += extraRounds;
                else task.MaxTurns = (task.MaxTurns ?? MaxManualTurns) + extraRounds;
                break;
            case "agent_failed":
                // The failed turn's delivery is spent; the retry is a new turn for the same Agent.
                task.ManualTurn++;
                EnsureTurnControl(task);
                task.SendStartedAt = null; task.DeliveredAt = null; task.AcknowledgedAt = null; task.WorkingAt = null;
                task.HostTurnEnded = null;
                break;
        }
        if (!string.IsNullOrWhiteSpace(note))
            task.ManualNextAction = "用户说明：" + note.Trim() + (string.IsNullOrWhiteSpace(task.ManualNextAction) ? "" : "\n" + task.ManualNextAction);
        RecoverExpiredFileDelivery(s, task);
        task.State = task.PausedFrom.Value;
        task.PauseReason = null; task.PauseMessage = null; task.PausedFrom = null;
        task.DeadlineAt = Now + TurnTimeout(s, task);
        s.Save(task);
        s.Event(task, "handoff.continued", "operator", Now, RelayJson.Write(new { reason, note, extra_rounds = extraRounds }));
        s.Notify(task, "continued_" + task.ManualTurn + "_" + reason, "用户已决定继续交接。", Now);
        return ManualView(task);
    }

    public HandoffProgress FileHandoffProgress(TaskRecord task)
    {
        if (!task.IsFileHandoff) throw new RelayException("file_handoff_required", "此接口只用于文件或 CLI 工作交接。");
        var finished = Terminal(task.State);
        var current = finished ? null : task.AwaitingAgentId ?? task.RecipientId;
        var collaboration = task.Collaboration;
        var completion = task.State switch
        {
            _ when task.ResumeStatus == "operator_completed" => "operator_completed",
            TaskState.Succeeded => collaboration?.Deliverable is not null ? "delivered" : "completed",
            TaskState.Cancelled => "cancelled",
            TaskState.Failed => "failed",
            TaskState.TimedOut => "timed_out",
            _ => null
        };
        var cliTurn = task.CliFileTurn == task.ManualTurn;
        var blocked = current is not null && CliExtension(current)?.Block is not null;
        var delivery = finished || task.State == TaskState.Paused ? "none"
            : cliTurn && task.CliFileStatus is "submitting" ? "cli_in_flight"
            : blocked && task.State is TaskState.AwaitingManual or TaskState.AwaitingReply ? "cli_blocked"
            : cliTurn && task.CliFileStatus is "unknown" or "failed" ? "cli_unconfirmed"
            : cliTurn && task.CliFileStatus == "submitted" || task.State is TaskState.Dispatched or TaskState.Acknowledged or TaskState.Running ? "agent_working"
            : CanSendFileHandoff(task) ? task.AutoDeliver && CanAutoDeliver(task) ? "cli_auto" : "cli_ready"
            : "copy_prompt";
        var waiting = finished ? "none" : task.State == TaskState.Paused || delivery is "cli_blocked" or "cli_unconfirmed" or "cli_ready" or "copy_prompt" ? "user" : "agent";
        string? passed = null, changed = null;
        if (collaboration is not null && !finished)
            (passed, changed) = collaboration.Phase switch
            {
                "review" => (collaboration.ReviewerFinal ? null : collaboration.AuthorId, collaboration.AuthorId),
                "accept" => (null, collaboration.ReviewerId),
                _ => (collaboration.ReviewerId, collaboration.ReviewerId)
            };
        return new(current, collaboration?.Phase, waiting, delivery, task.ManualTurn,
            collaboration is null && !task.ManualContinuous ? task.MaxTurns ?? MaxManualTurns : null,
            collaboration?.ReviewRound, collaboration?.MaxReviewRounds, passed, changed,
            task.PauseReason, task.PauseMessage, finished, completion, finished ? null : FileDeliveryMessage(task),
            RelayStatusOf(task, finished, delivery));
    }

    /// <summary>Delivery, host turn, Agent declarations and flow state are reported separately, never merged.</summary>
    private RelayStatus RelayStatusOf(TaskRecord task, bool finished, string delivery)
    {
        var cliTurn = task.CliFileTurn == task.ManualTurn;
        // Relay's own call for a previous turn may still be running; that is a send in flight, not "not sent".
        var inFlight = task.CliFileTurn is not null && task.CliFileStatus == "submitting";
        var send = inFlight ? "sending" : !cliTurn ? "not_sent" : task.CliFileStatus switch
        {
            "submitted" => "delivered", "unknown" => "sent_unknown", "failed" => "failed", _ => "not_sent"
        };
        var last = task.TurnStatuses.Where(entry => entry.Turn == task.ManualTurn && entry.Accepted).LastOrDefault();
        // Older ledgers could mark a late "received" as accepted even after work started.
        if (last?.Status == "received")
            last = task.TurnStatuses.LastOrDefault(entry => entry.Turn == task.ManualTurn && entry.Accepted &&
                entry.Status is "working" or "done" or "failed") ?? last;
        var agent = last?.Status switch
        {
            "received" => "received", "working" => "working", "done" => "submitted", "failed" => "failed", _ => "none"
        };
        var host = inFlight ? "running" : !cliTurn ? "not_started" : task.CliFileStatus switch
        {
            "submitted" => task.HostTurnEnded == true ? "ended" : "unknown",
            _ => task.HostTurnEnded == true ? "ended" : "unknown"
        };
        var flow = finished ? task.State switch
            {
                TaskState.Succeeded => "finished", TaskState.Cancelled => "cancelled",
                TaskState.TimedOut => "timed_out", _ => "failed"
            }
            : task.State == TaskState.Paused ? "paused" : "active";
        var needsUser = task.State == TaskState.Paused || delivery is "cli_blocked" or "cli_unconfirmed" or "cli_ready" or "copy_prompt";
        // A submitted receipt does not prove the host stopped. Say so instead of implying the next step is safe.
        var note = agent == "submitted" && host == "unknown"
            ? "结果已提交，但无法确认宿主轮次已结束；自动开始下一步会等待确认或由你决定。"
            : agent == "submitted" && host == "running" && !finished
                ? "结果已提交，当前投递仍在执行；不会在宿主结束前自动开始下一步。"
                : null;
        return new(send, agent, host, flow, needsUser, note, HandoffsDone(task), HandoffsTotal(task), task.CompletionNote);
    }

    private TimeSpan TurnTimeout(StoreSession s, TaskRecord task) => TimeSpan.FromMinutes(
        task.TurnTimeoutMinutes ?? s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!.TaskTimeoutMinutes);

    private void PauseFileHandoff(StoreSession s, TaskRecord task, string reason, string message, TaskState resumeAs)
    {
        task.PausedFrom = resumeAs; task.State = TaskState.Paused;
        task.PauseReason = reason; task.PauseMessage = message;
        s.Event(task, "handoff.paused", "relay", Now, reason);
        s.Notify(task, "paused_" + task.ManualTurn + "_" + reason, message, Now);
    }

    private void PauseForTimeout(StoreSession s, TaskRecord task)
    {
        var recovered = RecoverExpiredFileDelivery(s, task);
        var recipient = task.AwaitingAgentId ?? task.RecipientId;
        var blocked = ReadCliExtension(s, recipient)?.Block;
        var deliveryError = blocked?.Message ?? (!recovered && task.CliFileTurn == task.ManualTurn ? task.CliFileError : null);
        if (deliveryError is not null)
        {
            PauseFileHandoff(s, task, "delivery_failed", deliveryError, task.State);
            return;
        }
        var minutes = (int)TurnTimeout(s, task).TotalMinutes;
        PauseFileHandoff(s, task, "turn_timeout",
            $"本轮已超过设定时限（{minutes} 分钟），已暂停等待你的决定。Agent 可能仍在处理，晚到的回执仍会被接收；也可以继续等待、取消或人工标记完成。",
            task.State);
    }

    private bool RecoverExpiredFileDelivery(StoreSession s, TaskRecord task)
    {
        if (task.CliFileTurn is null || task.CliFileStatus != "submitting" || task.DeadlineAt > Now ||
            task.CliFileTurn != task.ManualTurn && task.UnattendedRun is null)
            return false;
        // A stopped sender cannot report its outcome; the turn deadline bounds the wait without resending it.
        task.CliFileStatus = "unknown";
        task.CliFileError = "桥接未在本轮时限内留下投递结果。请先检查目标会话；确认未收到后再允许重发。";
        s.Event(task, "file.cli_unknown", "relay", Now, task.CliFileError);
        return true;
    }

    private void AdvanceFileHandoff(StoreSession s, TaskRecord task, string next, string? action)
    {
        task.AwaitingAgentId = next; task.ManualTurn++; task.State = TaskState.AwaitingReply;
        task.ManualNextAction = action;
        // The new turn gets its own writable control directory before anyone is asked to write a receipt.
        EnsureTurnControl(task);
        // A new turn starts its own clock; the previous turn's delivery evidence does not carry over.
        task.SendStartedAt = null; task.DeliveredAt = null; task.AcknowledgedAt = null; task.WorkingAt = null;
        task.HostTurnEnded = null;
        task.DeadlineAt = Now + TurnTimeout(s, task);
    }

    private void ApplyCollaborationOutcome(StoreSession s, TaskRecord task, CollaborationRecord c, ManualReceipt receipt)
    {
        var items = (receipt.OpenItems ?? []).Select(i => i.Trim()).Where(i => i.Length > 0).ToArray();
        var phase = c.Phase;
        void Record(string outcome, string? result) => c.History.Add(new(receipt.Turn, receipt.Sender, phase, outcome, Now,
            result, items.Length == 0 ? null : items, receipt.NextAction));
        if (receipt.Status == "failed")
        {
            if (receipt.Outcome is not null)
                throw new RelayException("collaboration_outcome", "失败回执不填写 outcome，请在 next_action 说明失败原因。");
            Record("failed", KeepReceiptResult(s, task, receipt.ResultFile)?.Path);
            PauseFileHandoff(s, task, "agent_failed", receipt.NextAction ?? "Agent 报告本轮处理失败，请查看回稿后决定是否让它重试。", TaskState.AwaitingReply);
            return;
        }
        var allowed = AllowedOutcomes(phase);
        if (receipt.Outcome is null || !allowed.Contains(receipt.Outcome))
            throw new RelayException("collaboration_outcome", "当前阶段的 outcome 只能是：" + string.Join(" / ", allowed) + "。");
        // The outcome decides who works next; Agents often copy needs_reply from the single-handoff template, so it is ignored here.
        if (items.Length > 0 && receipt.Outcome is not ("changes_requested" or "submitted"))
            throw new RelayException("collaboration_open_items", receipt.Outcome == "approved"
                ? "仍有未解决问题时不能通过验收，请改为 changes_requested。" : "最终接受前不能保留未解决问题；提交和要求修改时可以填写 open_items。");
        var author = c.AuthorId;
        switch (receipt.Outcome)
        {
            case "criteria_ready":
            {
                if (string.IsNullOrWhiteSpace(receipt.AcceptanceCriteria))
                    throw new RelayException("collaboration_criteria_required", "请在 acceptance_criteria 中填写具体、可检查的验收标准。");
                c.Criteria = receipt.AcceptanceCriteria.Trim();
                Record("criteria_ready", null);
                c.Phase = c.ReviewerId == task.RecipientId ? "review" : "implement";
                AdvanceFileHandoff(s, task, task.RecipientId, "验收标准已由发起方拟定，请继续处理原任务。");
                EnsureIndependentReviewSession(s, task, c);
                break;
            }
            case "submitted":
            {
                if (string.IsNullOrWhiteSpace(receipt.ResultFile))
                    throw new RelayException("collaboration_submission", "提交时必须在 result_file 给出真实产物或修改说明文件。");
                var previous = c.SubmissionPath;
                var artifact = KeepReceiptResult(s, task, receipt.ResultFile)!;
                Record("submitted", artifact.Path);
                var unchanged = previous is not null && File.Exists(previous) && SameContent(previous, artifact.Path);
                c.SubmissionPath = artifact.Path; c.ApprovedSubmissionPath = null; c.OpenItems = items;
                c.ReviewRound++; c.Phase = "review";
                AdvanceFileHandoff(s, task, c.ReviewerId, receipt.NextAction ?? "请按验收标准检查作者的最新提交。");
                EnsureIndependentReviewSession(s, task, c);
                if (c.MaxReviewRounds > 0 && c.ReviewRound > c.MaxReviewRounds)
                    PauseFileHandoff(s, task, "round_limit", $"已用完 {c.MaxReviewRounds} 轮验收，最新提交尚未复验。请决定追加轮数继续、取消或人工标记完成。", TaskState.AwaitingReply);
                else if (unchanged)
                    PauseFileHandoff(s, task, "no_progress", "作者本轮提交的文件与上一版完全相同，自动传递已暂停。请确认是否仍交给验收者复验。", TaskState.AwaitingReply);
                break;
            }
            case "changes_requested":
            {
                if (items.Length == 0)
                    throw new RelayException("collaboration_open_items", "要求修改时必须在 open_items 逐条列出具体未解决问题。");
                var notes = KeepReceiptResult(s, task, receipt.ResultFile);
                var lastReview = c.History.LastOrDefault(h => h.Outcome == "changes_requested");
                Record("changes_requested", notes?.Path);
                var repeated = lastReview?.OpenItems is { } prior && prior.SequenceEqual(items);
                c.OpenItems = items; c.Phase = "revise";
                // Authors revise the submission; review notes are attached only when the reviewer wrote one.
                if (notes is null && c.SubmissionPath is not null) task.ManualCurrentFile = c.SubmissionPath;
                AdvanceFileHandoff(s, task, author, receipt.NextAction ?? "请逐条处理 open_items 中的问题后重新提交。");
                if (c.ReviewerFinal && c.MaxReviewRounds > 0 && c.ReviewRound >= c.MaxReviewRounds)
                    PauseFileHandoff(s, task, "round_limit", $"已完成 {c.MaxReviewRounds} 轮验收，仍有未解决问题。请决定追加轮数、取消或人工标记完成。", TaskState.AwaitingReply);
                else if (repeated)
                    PauseFileHandoff(s, task, "no_progress", "验收者提出的问题与上一轮完全相同，说明上次修改未解决。自动传递已暂停，请判断是否继续。", TaskState.AwaitingReply);
                break;
            }
            case "approved":
            {
                Record("approved", KeepReceiptResult(s, task, receipt.ResultFile)?.Path);
                c.ApprovedSubmissionPath = c.SubmissionPath ?? task.RequestPath; c.OpenItems = []; c.Phase = "accept";
                task.ManualCurrentFile = c.ApprovedSubmissionPath;
                if (c.ReviewerFinal)
                {
                    c.Deliverable = c.ApprovedSubmissionPath;
                    task.State = TaskState.Succeeded;
                    s.Event(task, "collaboration.delivered", receipt.Sender, Now, c.Deliverable);
                    break;
                }
                AdvanceFileHandoff(s, task, author, receipt.NextAction ?? "验收已通过。请核对通过的版本，确认接受并写明交付物。");
                break;
            }
            case "accepted":
            {
                if (string.IsNullOrWhiteSpace(receipt.Deliverable))
                    throw new RelayException("collaboration_deliverable", "确认接受时必须在 deliverable 写明最终交付物的位置或交付说明。");
                Record("accepted", KeepReceiptResult(s, task, receipt.ResultFile)?.Path);
                c.Deliverable = receipt.Deliverable.Trim();
                task.State = TaskState.Succeeded;
                s.Event(task, "collaboration.delivered", receipt.Sender, Now, c.Deliverable);
                break;
            }
        }
    }

    public static bool UsesIndependentReviewSession(TaskRecord task, string agentId) =>
        task.Collaboration is { IndependentAcceptance: true, Phase: "review" } review && review.ReviewerId == agentId;

    private void EnsureIndependentReviewSession(StoreSession s, TaskRecord task, CollaborationRecord c)
    {
        if (!UsesIndependentReviewSession(task, c.ReviewerId) ||
            ReadTaskSession(s, task, c.ReviewerId) is { IndependentReview: true }) return;
        StartFreshSession(s, task, c.ReviewerId, independentReview: true);
        s.Event(task, "collaboration.independent_session", c.ReviewerId, Now, "Reserved a dedicated review conversation.");
    }

    private ArtifactRecord? KeepReceiptResult(StoreSession s, TaskRecord task, string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        ManualTransfers.RequireFile(file);
        var settings = s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!;
        var artifact = KeepManualFile(s, task, file, "result", Now, settings.ArtifactTtlDays);
        task.ResultPath = artifact.Path; task.ManualCurrentFile = artifact.Path;
        return artifact;
    }

    private static bool SameContent(string first, string second)
    {
        if (string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return true;
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        return File.ReadAllBytes(first).AsSpan().SequenceEqual(File.ReadAllBytes(second));
    }
}
