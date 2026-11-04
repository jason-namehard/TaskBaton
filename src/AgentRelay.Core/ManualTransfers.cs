using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRelay.Core;

public sealed record ManualTransferDraft(string From, string To, string File, string Message = "");
public sealed record ManualReceipt(string TaskId, string AttemptId, string Sender, int Turn, string Status,
    bool? NeedsReply = null, string? ResultFile = null, string? NextAction = null, string? Outcome = null,
    string[]? OpenItems = null, string? Deliverable = null,bool? TaskComplete = null,string? AcceptanceCriteria = null);
public sealed record ManualReceiptError(string TaskId, string Path, string Message);
public sealed record ManualReceiptImportResult(string[] UpdatedTaskIds, ManualReceiptError[] Errors);

public static class ManualTransfers
{
    private static readonly JsonSerializerOptions InputJson = new(RelayJson.Options)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ManualTransferDraft Parse(string json, IReadOnlyList<AgentRecord> agents)
    {
        var draft = Read<ManualTransferDraft>(json);
        var from = Resolve(draft.From, agents); var to = Resolve(draft.To, agents);
        if (from == to) throw new RelayException("manual_same_agent", "发送方与接收方不能相同。");
        RequireFile(draft.File);
        return draft with { From = from, To = to, File = Path.GetFullPath(draft.File) };
    }

    public static ManualReceipt ParseReceipt(string json) => Read<ManualReceipt>(json);

    private static T Read<T>(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024)
            throw new RelayException("manual_json_too_large", "交接或回执 JSON 不能超过 64 KB。");
        var trimmed = json.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("```", StringComparison.Ordinal))
            trimmed = trimmed[7..^3].Trim();
        try
        {
            return JsonSerializer.Deserialize<T>(trimmed, InputJson) ?? throw new JsonException("Expected JSON object.");
        }
        catch (JsonException error)
        {
            var failure = new RelayException("manual_json_invalid", "交接或回执 JSON 不完整，或字段名称、类型不符合格式。请重新复制完整 JSON；回执请按当前任务上下文中的模板重写。");
            failure.Data["technical_detail"] = error.Message;
            throw failure;
        }
    }

    private static string Resolve(string value, IReadOnlyList<AgentRecord> agents)
    {
        var byId = agents.SingleOrDefault(a => a.AgentId == value);
        if (byId is not null) return byId.AgentId;
        var byName = agents.Where(a => a.DisplayName == value).ToArray();
        return byName.Length switch
        {
            1 => byName[0].AgentId,
            0 => throw new RelayException("manual_agent_unknown", "未找到已登记的 Agent：" + value),
            _ => throw new RelayException("manual_agent_ambiguous", "同名 Agent 不止一个，请使用节点 ID：" + value)
        };
    }

    internal static void RequireFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !System.IO.File.Exists(path))
            throw new RelayException("manual_file_missing", "请提供存在的完整文件路径：" + path);
        using var file = System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length == 0) throw new RelayException("manual_file_empty", "交接文件或回稿不能为空。");
    }
}

public sealed partial class RelayService
{
    public const int MaxManualTurns = 4;

    public string ManualReceiptPath(TaskRecord task) => Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId), ".relay", "receipt.json");

    public TaskView CreateFileHandoff(ManualTransferDraft draft, OriginSession? origin = null, bool reuseExisting = true)
        => CreateFileHandoff(draft, origin, reuseExisting, null);

    private TaskView CreateFileHandoff(ManualTransferDraft draft, OriginSession? origin, bool reuseExisting, ImageInput[]? images)
    {
        if (origin is not null) RequireOriginContext(origin);
        var normalized = ManualTransfers.Parse(RelayJson.Write(draft), Agents());
        return store.Write(s =>
        {
            Agent(s, normalized.From); Agent(s, normalized.To);
            if (reuseExisting)
            {
                var active = s.Query("SELECT json,input_json FROM tasks", r =>
                        (Task: RelayJson.Read<TaskRecord>(r.GetString(0)), Input: RelayJson.Read<DispatchRequest>(r.GetString(1))))
                    .FirstOrDefault(entry => entry.Task.IsFileHandoff && !Terminal(entry.Task.State) &&
                        entry.Task.RequesterId == normalized.From && entry.Task.RecipientId == normalized.To &&
                        entry.Task.ManualSourceFile == normalized.File && entry.Input.Content == normalized.Message &&
                        SameContent(entry.Task.RequestPath, normalized.File)).Task;
                if (active is not null) return ManualView(active);
            }
            var now = Now;
            var settings = s.Json<RelaySettings>("SELECT json FROM settings WHERE id=1")!;
            var task = new TaskRecord
            {
                TaskId = Guid.NewGuid().ToString(), AttemptId = Guid.NewGuid().ToString(),
                RequesterId = normalized.From, RecipientId = normalized.To, AdapterId = "manual-file", Mode = "manual",
                RequestPath = "", ResultPath = "", CreatedAt = now, DeadlineAt = now.AddMinutes(settings.TaskTimeoutMinutes),
                State = TaskState.AwaitingManual, IsFileHandoff = true, AwaitingAgentId = normalized.To,
                ManualSourceFile = normalized.File, ManualNextAction = normalized.Message
            };
            try
            {
                var artifact = KeepManualFile(s, task, normalized.File, "request", now, settings.ArtifactTtlDays);
                task.RequestPath = artifact.Path; task.ManualCurrentFile = artifact.Path;
                if (images is { Length: > 0 }) task.Images = KeepTaskImages(s, task, images, now, settings.ArtifactTtlDays);
                task.ResultPath = Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId), "reply.md");
                var control = Path.GetDirectoryName(ManualReceiptPath(task))!;
                Directory.CreateDirectory(control);
                if (OperatingSystem.IsWindows()) File.SetAttributes(control, File.GetAttributes(control) | FileAttributes.Hidden);
                var input = new DispatchRequest(task.RequesterId, task.RecipientId, normalized.Message, "manual", "manual-file", task.TaskId);
                s.Execute("INSERT INTO tasks VALUES($id,$from,$key,$input,$json)", ("$id", task.TaskId), ("$from", task.RequesterId),
                    ("$key", task.TaskId), ("$input", RelayJson.Write(input)), ("$json", RelayJson.Write(task)));
                if (origin is not null) RegisterOriginSession(s, task, origin);
                s.Event(task, "task.created", task.RequesterId, now);
                s.Notify(task, "waiting", "请复制交接提示词给接收方，等待读取并回报。", now);
                return ManualView(task);
            }
            catch
            {
                if (images is { Length: > 0 })
                {
                    var directory = workspace.AttemptDirectory(task.TaskId, task.AttemptId);
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }
                throw;
            }
        });
    }

    public ManualReceiptImportResult ImportManualReceipts()
    {
        var tasks = store.Read(s => s.Query("SELECT json FROM tasks", r => RelayJson.Read<TaskRecord>(r.GetString(0))))
            .Where(t => t.IsFileHandoff && !Terminal(t.State)).ToArray();
        var changed = new List<string>(); var errors = new List<ManualReceiptError>();
        foreach (var task in tasks)
        {
            var path = ManualReceiptPath(task);
            var applied = false;
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    if (json != task.ManualReceiptJson)
                        applied = ApplyManualReceipt(task.TaskId, ManualTransfers.ParseReceipt(json), json);
                }
                catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException)
                {
                    // Agents may still be publishing an external receipt; report it without changing the ledger.
                    errors.Add(new(task.TaskId, path, error.Message));
                }
            }
            if (applied) { changed.Add(task.TaskId); continue; }
            if (task.DeadlineAt > Now) continue;
            var paused = store.Write(s =>
            {
                var current = s.Task(task.TaskId);
                if (Terminal(current.State) || current.DeadlineAt > Now) return false;
                if (current.State == TaskState.Paused)
                {
                    if (!RecoverExpiredFileDelivery(s, current)) return false;
                }
                else PauseForTimeout(s, current);
                s.Save(current);
                return true;
            });
            if (paused) changed.Add(task.TaskId);
        }
        return new(changed.ToArray(), errors.ToArray());
    }

    private bool ApplyManualReceipt(string id, ManualReceipt receipt, string json) => store.Write(s =>
    {
        var task = s.Task(id);
        if (Terminal(task.State) || task.ManualReceiptJson == json) return false;
        if (task.ManualReceiptJson is { } prior && RelayJson.Write(RelayJson.Read<ManualReceipt>(prior)) == RelayJson.Write(receipt)) return false;
        if (receipt.TaskId != task.TaskId || receipt.AttemptId != task.AttemptId)
            throw new RelayException("manual_receipt_identity", "回执的任务或尝试标识不匹配。");
        if (receipt.Turn < task.ManualTurn) return false;
        if (receipt.Turn != task.ManualTurn)
            throw new RelayException("manual_receipt_turn", "回执轮次与当前交接不匹配。");
        if (receipt.Sender != task.AwaitingAgentId)
            throw new RelayException("manual_receipt_sender", "此回执必须由当前等待的 Agent 提交。");
        if (receipt.Status is not ("received" or "working" or "done" or "failed"))
            throw new RelayException("manual_receipt_status", "回执状态应为 received、working、done 或 failed。");
        // Single handoffs are decided by needs_reply; review-loop fields an Agent adds there carry no decision.
        if (task.Collaboration is null) receipt = receipt with { Outcome = null, OpenItems = null, Deliverable = null };
        if (task.State == TaskState.Paused)
        {
            // A late receipt for the timed-out turn is still the Agent's real result; other pauses wait for the user.
            if (task.PauseReason != "turn_timeout")
                throw new RelayException("handoff_paused", "交接已暂停，等待用户决定；暂停期间不接收新的回执。");
            task.State = task.PausedFrom!.Value;
            task.PauseReason = null; task.PauseMessage = null; task.PausedFrom = null;
            task.DeadlineAt = Now + TurnTimeout(s, task);
            s.Event(task, "handoff.late_receipt", receipt.Sender, Now, "turn=" + receipt.Turn);
        }
        RecordCliDeliveryEvidence(s, task, receipt);
        if (receipt.Status is "received" or "working")
        {
            if (receipt.NeedsReply == true || receipt.Outcome is not null)
                throw new RelayException("manual_receipt_reply", "只有完成当前工作后才能要求回传或给出结论。");
            if (receipt.Status == "received" && task.State == TaskState.Running)
                throw new RelayException("manual_receipt_order", "处理中任务不能退回已接收状态。");
            task.State = receipt.Status == "received" ? TaskState.Acknowledged : TaskState.Running;
        }
        else if (task.Collaboration is { } collaboration) ApplyCollaborationOutcome(s, task, collaboration, receipt);
        else ApplyPlainOutcome(s, task, receipt);
        task.ManualReceiptJson = json;
        s.Save(task);
        s.Event(task, "manual." + receipt.Status, receipt.Sender, Now,
            "turn=" + receipt.Turn + (receipt.Outcome is null ? "" : " outcome=" + receipt.Outcome));
        s.Notify(task, "manual_turn_" + receipt.Turn + "_" + receipt.Status,
            task.State == TaskState.AwaitingReply ? "请将交接交给 " + (Agent(s, task.AwaitingAgentId!).DisplayName ?? task.AwaitingAgentId) + " 继续处理。" :
            task.State == TaskState.Paused ? task.PauseMessage! :
            task.State == TaskState.Succeeded ? task.Collaboration is null ? "本次交接已结束，无需继续传递。" : "验收已完成，协作已结束。" :
            task.ErrorMessage ?? "交接进度已更新。", Now);
        return true;
    });

    private void ApplyPlainOutcome(StoreSession s, TaskRecord task, ManualReceipt receipt)
    {
        if (receipt.NeedsReply is not { } needsReply)
            throw new RelayException("manual_receipt_reply", "完成或失败回执需要填写 needs_reply。");
        // Fixed plans exist only on tasks created before condition-driven collaboration replaced them.
        var plannedNext = receipt.Status == "done" && task.HandoffPlan is { } plan && task.ManualTurn + 1 < plan.Length;
        if (receipt.Status == "failed" && needsReply)
            throw new RelayException("manual_receipt_reply", "失败回执不能自动开启下一轮。");
        if (receipt.Status == "done" && !needsReply && !plannedNext && !string.IsNullOrWhiteSpace(receipt.NextAction))
            throw new RelayException("manual_receipt_contradiction", "回执声明无需继续，却同时给出了下一步工作。请明确是否需要继续；有待办时应设置 needs_reply=true 并提供回稿文件。");
        var continues = needsReply || plannedNext;
        if(task.ManualContinuous&&receipt.Status=="done")
        {
            if(continues&&receipt.TaskComplete==true)
                throw new RelayException("manual_receipt_contradiction","仍需继续传递时，task_complete不能为true。");
            if(!continues&&receipt.TaskComplete!=true)
                throw new RelayException("manual_completion_required","本轮完成不等于整项任务结束。仍有工作请needs_reply=true并提供回稿与下一步；整项工作已结束请明确task_complete=true。");
        }
        if (continues && (string.IsNullOrWhiteSpace(receipt.ResultFile) || string.IsNullOrWhiteSpace(receipt.NextAction)))
            throw new RelayException("manual_receipt_reply", plannedNext
                ? "本轮处理完成，但交接计划仍有后续步骤，不能结束任务。请提供真实回稿 result_file 和具体 next_action，再按当前上下文重写回执。"
                : "需要对方继续时，必须给出回稿文件和具体的下一步工作；已确认或寒暄无需回传。");
        KeepReceiptResult(s, task, receipt.ResultFile);
        if (receipt.Status == "failed")
        {
            task.State = TaskState.Failed; task.ErrorCode = "manual_agent_failed";
            task.ErrorMessage = receipt.NextAction ?? "Agent 报告处理失败，请检查回执或回稿。";
            return;
        }
        if (!continues) { task.State = TaskState.Succeeded; return; }
        var next = plannedNext ? task.HandoffPlan![task.ManualTurn + 1]
            : receipt.Sender == task.RequesterId ? task.RecipientId : task.RequesterId;
        var limit = task.MaxTurns ?? MaxManualTurns;
        AdvanceFileHandoff(s, task, next, receipt.NextAction);
        if (!task.ManualContinuous && task.ManualTurn >= limit)
            PauseFileHandoff(s, task, "round_limit", $"交接已往返 {limit} 轮，已暂停继续传递，避免重复往返。请决定追加轮数继续、取消或人工标记完成。", TaskState.AwaitingReply);
    }

    private ArtifactRecord KeepManualFile(StoreSession s, TaskRecord task, string source, string kind, DateTimeOffset now, int ttl)
    {
        var full = Path.GetFullPath(source);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var existing = s.Json<ArtifactRecord>("SELECT json FROM artifacts WHERE path=$path", ("$path", full));
        if (existing is not null && existing.TaskId == task.TaskId && existing.AttemptId == task.AttemptId)
        {
            if (existing.State != "available" || existing.ExpiresAt <= now)
                throw new RelayException("artifact_expired", "此交接文件已过期，请重新生成。");
            return existing;
        }
        var path = full;
        if (!full.StartsWith(workspace.Root + Path.DirectorySeparatorChar, comparison) || existing is not null)
        {
            var directory = workspace.AttemptDirectory(task.TaskId, task.AttemptId);
            Directory.CreateDirectory(directory);
            path = workspace.RequireManaged(Path.Combine(directory, Path.GetFileName(source)));
            if (File.Exists(path)) path = workspace.RequireManaged(Path.Combine(directory,
                Path.GetFileNameWithoutExtension(source) + "-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(source)));
            File.Copy(full, path);
        }
        else path = workspace.RequireManaged(full);
        var artifact = new ArtifactRecord(Guid.NewGuid().ToString(), task.TaskId, task.AttemptId, kind, path, now, now.AddDays(ttl), "available");
        AddArtifact(s, artifact);
        return artifact;
    }

    public string TurnReplyPath(TaskRecord task) => Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId),
        task.ManualTurn == 0 ? "reply.md" : $"reply-{task.ManualTurn}.md");

    private TaskView ManualView(TaskRecord task)
    {
        var paused = task.State == TaskState.Paused;
        var overdue = !Terminal(task.State) && !paused && task.DeadlineAt <= Now;
        var finished = Terminal(task.State);
        return new(task, overdue, finished ? "inspect" : paused ? "decide_paused_handoff" : "copy_file_handoff", finished ?
            task.State == TaskState.Succeeded ? task.Collaboration?.Deliverable is { } delivered ? "验收完成，已交付：" + delivered : "交接已完成，无需继续传递。" :
            task.ErrorMessage ?? "交接已结束。" : paused ? task.PauseMessage! :
            $"有文件交接给你。请读取 {Path.Combine(Path.GetDirectoryName(ManualReceiptPath(task))!, "context.json")}，完成其中要求并写回执，无需额外寒暄或确认消息。");
    }
}
