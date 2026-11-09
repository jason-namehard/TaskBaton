using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRelay.Core;

public sealed record ManualTransferDraft(string From, string To, string File, string Message = "");
public sealed record ManualReceipt(string TaskId, string AttemptId, string Sender, int Turn, string Status,
    bool? NeedsReply = null, string? ResultFile = null, string? NextAction = null, string? Outcome = null,
    string[]? OpenItems = null, string? Deliverable = null,bool? TaskComplete = null,string? AcceptanceCriteria = null);
/// <summary>One applied status of one turn. Kept as history so quick or repeated updates are not collapsed into the last one.</summary>
public sealed record TurnStatusEntry(int Turn, string Sender, string Status, DateTimeOffset At, string Source,
    string? Hash = null, bool Accepted = true);
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

    private static readonly string[] ReceiptFields =
    [
        "task_id", "attempt_id", "sender", "turn", "status", "needs_reply", "result_file", "next_action",
        "outcome", "open_items", "deliverable", "task_complete", "acceptance_criteria"
    ];
    private static readonly string[] DraftFields = ["from", "to", "file", "message"];

    /// <summary>What each field accepts, used to explain a rejected receipt instead of one generic message.</summary>
    private static readonly Dictionary<string, string> ReceiptHints = new(StringComparer.Ordinal)
    {
        ["task_id"] = "字符串，保持任务 ID 不变",
        ["attempt_id"] = "字符串，保持尝试 ID 不变",
        ["sender"] = "字符串，必须是当前等待的 Agent",
        ["turn"] = "整数，等于当前轮次",
        ["status"] = "字符串：received / working / done / failed",
        ["needs_reply"] = "true 或 false",
        ["result_file"] = "字符串，真实回稿文件的完整路径",
        ["next_action"] = "字符串，写给下一步的具体工作",
        ["outcome"] = "字符串，取当前上下文列出的 outcome 之一",
        ["open_items"] = "字符串数组，逐条列出未解决问题",
        ["deliverable"] = "字符串，最终交付物的路径或一句话交付说明（不能写成数组或对象）",
        ["task_complete"] = "true 或 false",
        ["acceptance_criteria"] = "字符串"
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
            var receipt = typeof(T) == typeof(ManualReceipt);
            var failure = new RelayException("manual_json_invalid", Explain(trimmed, error, receipt));
            failure.Data["technical_detail"] = error.Message;
            failure.Data["field"] = error.Path;
            throw failure;
        }
    }

    /// <summary>Turns a System.Text.Json failure into a message naming the field, the value found and the accepted form.</summary>
    internal static string Explain(string json, JsonException error, bool receipt)
    {
        // The generic line keeps its existing translation; the specific line names the field and the value found.
        var generic = receipt
            ? "交接或回执 JSON 不完整，或字段名称、类型不符合格式。请重新复制完整 JSON；回执请按当前任务上下文中的模板重写。"
            : "交接或回执 JSON 不完整，或字段名称、类型不符合格式。请重新复制完整 JSON；回执请按当前任务上下文中的模板重写。";
        return generic + Environment.NewLine + Detail(json, error, receipt);
    }

    private static string Detail(string json, JsonException error, bool receipt)
    {
        var allowed = receipt ? ReceiptFields : DraftFields;
        var label = receipt ? "回执" : "交接";
        var message = error.Message;
        var unknown = System.Text.RegularExpressions.Regex.Match(message,
            "property '([^']+)' could not be mapped", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!unknown.Success)
            unknown = System.Text.RegularExpressions.Regex.Match(message, "Member '([^']+)'");
        if (unknown.Success)
        {
            var name = unknown.Groups[1].Value;
            var advice = name == "notes" ? "补充说明请写入 next_action，不要新增字段。"
                : receipt ? "回执只接受契约内的字段。" : "交接清单只接受 from/to/file/message。";
            return $"{label}格式不正确：字段 {name} 不是{label}契约的一部分，未导入。{advice}可用字段：{string.Join(", ", allowed)}。";
        }
        var missing = System.Text.RegularExpressions.Regex.Match(message,
            @"missing required properties,?\s+including(?: the following)?:\s*(.+)$");
        if (missing.Success)
            return $"{label}格式不正确：缺少必填字段 {missing.Groups[1].Value.Trim().TrimEnd('.')}，请按模板补齐。";
        var path = error.Path;
        var field = FieldName(path);
        var actual = ActualKind(json, path);
        if (field is not null)
        {
            var expected = ReceiptHints.TryGetValue(field, out var hint) ? hint : "契约要求的类型";
            var found = actual is null ? "无法识别的值" : actual;
            var advice = field == "deliverable"
                ? "例如 \"deliverable\": \"D:\\\\project\\\\out\\\\report.md\"；不要写成数组或对象。"
                : "请按当前任务上下文中的回执模板重写该字段。";
            return $"{label}格式不正确：字段 {field} 应为{expected}，实际是{found}，未导入。{advice}";
        }
        return $"{label}格式不正确：JSON 不完整或类型不符合契约，未导入。请重新复制完整 JSON，并按当前任务上下文中的模板只填写值。";
    }

    private static string? FieldName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = path.TrimStart('$').TrimStart('.');
        var bracket = trimmed.IndexOf('[');
        if (bracket >= 0) trimmed = trimmed[..bracket];
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? ActualKind(string json, string? path)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var current = document.RootElement;
            foreach (var segment in (path ?? "").TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = segment;
                var indexes = new List<int>();
                var bracket = name.IndexOf('[');
                if (bracket >= 0)
                {
                    foreach (var part in name[(bracket + 1)..].TrimEnd(']').Split(']', StringSplitOptions.RemoveEmptyEntries))
                        if (int.TryParse(part.Trim('[', ']'), out var index)) indexes.Add(index);
                    name = name[..bracket];
                }
                if (name.Length > 0)
                {
                    if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current)) return null;
                }
                foreach (var index in indexes)
                {
                    if (current.ValueKind != JsonValueKind.Array || current.GetArrayLength() <= index) return null;
                    current = current[index];
                }
            }
            return current.ValueKind switch
            {
                JsonValueKind.Array => "数组", JsonValueKind.Object => "对象", JsonValueKind.String => "字符串",
                JsonValueKind.Number => "数字", JsonValueKind.True or JsonValueKind.False => "布尔值",
                JsonValueKind.Null => "null", _ => null
            };
        }
        catch (JsonException) { return null; }
    }

    internal static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json.Trim())))[..16];

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

    /// <summary>Control files live in the attempt's .relay directory, one subdirectory per turn.</summary>
    public string AttemptControlDirectory(TaskRecord task) =>
        Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId), ".relay");

    public string TurnControlDirectory(TaskRecord task) =>
        Path.Combine(AttemptControlDirectory(task), "turn-" + task.ManualTurn.ToString(CultureInfo.InvariantCulture));

    public string ManualReceiptPath(TaskRecord task) => Path.Combine(TurnControlDirectory(task), "receipt.json");

    public string ManualContextPath(TaskRecord task) => Path.Combine(TurnControlDirectory(task), "context.json");

    /// <summary>0.3.0 and earlier wrote one receipt for the whole attempt; still read it for a first turn.</summary>
    public string LegacyReceiptPath(TaskRecord task) => Path.Combine(AttemptControlDirectory(task), "receipt.json");

    /// <summary>The receiver needs a writable directory before it can publish a receipt.</summary>
    internal void EnsureTurnControl(TaskRecord task)
    {
        var directory = TurnControlDirectory(task);
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) return;
        // Mark .relay and every turn directory hidden; attributes are not inherited.
        foreach (var path in new[] { AttemptControlDirectory(task), directory })
            if (Directory.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }

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
                EnsureTurnControl(task);
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

    /// <summary>Per-turn receipt files first, then the single attempt-level file that 0.3.0 prompts named.</summary>
    private IEnumerable<(FileInfo File, int? Turn)> ReceiptCandidates(TaskRecord task)
    {
        var attempt = AttemptControlDirectory(task);
        foreach (var directory in Directory.Exists(attempt) ? Directory.GetDirectories(attempt, "turn-*") : [])
        {
            var turn = int.TryParse(Path.GetFileName(directory).AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
            if (turn < 0) continue;
            foreach (var file in ReceiptFiles(directory)) yield return (file, turn);
        }
        var legacy = new FileInfo(LegacyReceiptPath(task));
        if (legacy.Exists) yield return (legacy, null);
    }

    /// <summary>Receipt files the current turn may publish; a turn can register several statuses in order.</summary>
    private static IEnumerable<FileInfo> ReceiptFiles(string directory) =>
        !Directory.Exists(directory) ? [] : new DirectoryInfo(directory).EnumerateFiles("*.json")
            .Where(file => !string.Equals(file.Name, "context.json", StringComparison.OrdinalIgnoreCase))
            // Status files are applied in the order the Agent wrote them; the name only breaks a timestamp tie.
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase);

    private readonly record struct ReceiptStamp(DateTime ModifiedUtc, long Length);
    // Only files already accounted for as evidence are cached. New or rewritten files are still read.
    private Dictionary<string, ReceiptStamp> evidenceFiles = new(StringComparer.Ordinal);

    public ManualReceiptImportResult ImportManualReceipts()
    {
        var tasks = store.Read(s => s.Query("SELECT json FROM tasks", r => RelayJson.Read<TaskRecord>(r.GetString(0))))
            .Where(t => t.IsFileHandoff).ToArray();
        var changed = new List<string>(); var errors = new List<ManualReceiptError>();
        var previousEvidence = Volatile.Read(ref evidenceFiles);
        var scannedEvidence = new Dictionary<string, ReceiptStamp>(StringComparer.Ordinal);
        foreach (var stored in tasks)
        {
            var task = stored;
            var applied = false;
            var pendingEvidence = new List<(TurnStatusEntry Entry, string Path, ReceiptStamp Stamp)>();
            HashSet<(int Turn, string? Hash)>? recorded = null;
            // Old turns keep their files as evidence; only the current turn may change the task.
            foreach (var (file, knownTurn) in ReceiptCandidates(task))
            {
                var path = file.FullName;
                try
                {
                    var stamp = new ReceiptStamp(file.LastWriteTimeUtc, file.Length);
                    if ((Terminal(task.State) || knownTurn < task.ManualTurn) &&
                        previousEvidence.TryGetValue(path, out var previousStamp) && previousStamp == stamp)
                    {
                        scannedEvidence.Add(path, stamp);
                        continue;
                    }
                    var json = File.ReadAllText(path);
                    var receipt = ManualTransfers.ParseReceipt(json);
                    // A 0.3.0 prompt named one receipt for the whole attempt; the receipt itself declares its turn.
                    var turn = knownTurn ?? receipt.Turn;
                    if (knownTurn is not null && receipt.Turn != knownTurn)
                    {
                        errors.Add(new(task.TaskId, path, $"回执声明第 {receipt.Turn} 轮，但文件位于第 {knownTurn} 轮的目录，未导入。"));
                        continue;
                    }
                    if (Terminal(task.State) || turn < task.ManualTurn)
                    {
                        // Evidence only: it never advances, rewinds or re-opens the flow.
                        var hash = ManualTransfers.Hash(json);
                        recorded ??= task.TurnStatuses.Select(entry => (entry.Turn, entry.Hash)).ToHashSet();
                        if (recorded.Contains((turn, hash))) scannedEvidence.Add(path, stamp);
                        else pendingEvidence.Add((new(turn, receipt.Sender, "evidence", Now, file.Name, hash, Accepted: false),
                            path, stamp));
                        continue;
                    }
                    if (turn > task.ManualTurn)
                    {
                        errors.Add(new(task.TaskId, path, $"回执轮次 {turn} 晚于当前第 {task.ManualTurn} 轮，已保留但未导入；请确认是否属于旧任务或未来轮次。"));
                        continue;
                    }
                    var outcome = ApplyManualReceipt(task.TaskId, receipt, json, path);
                    if (outcome == ReceiptOutcome.Applied) { applied = true; changed.Add(task.TaskId); }
                    else if (outcome == ReceiptOutcome.Backwards)
                        errors.Add(new(task.TaskId, path,
                            "回执把本轮从处理中退回 received，已保留记录但不改变状态；请按当前轮次的真实进度重写。"));
                    if (outcome != ReceiptOutcome.None) task = store.Read(s => s.Task(task.TaskId));
                }
                catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException)
                {
                    // Agents may still be publishing; report it without changing the ledger.
                    errors.Add(new(task.TaskId, path, error.Message));
                }
            }
            if (pendingEvidence.Count > 0)
            {
                try
                {
                    RecordEvidence(stored, pendingEvidence.Select(item => item.Entry));
                    foreach (var item in pendingEvidence) scannedEvidence.Add(item.Path, item.Stamp);
                }
                catch (RelayException error)
                {
                    errors.Add(new(task.TaskId, pendingEvidence[0].Path, error.Message));
                }
            }
            if (applied || Terminal(task.State)) continue;
            if (task.DeadlineAt > Now) continue;
            var paused = store.Write(s =>
            {
                var latest = s.Task(task.TaskId);
                if (Terminal(latest.State) || latest.DeadlineAt > Now) return false;
                if (latest.State == TaskState.Paused)
                {
                    if (!RecoverExpiredFileDelivery(s, latest)) return false;
                }
                else PauseForTimeout(s, latest);
                s.Save(latest);
                return true;
            });
            if (paused) changed.Add(task.TaskId);
        }
        Volatile.Write(ref evidenceFiles, scannedEvidence);
        return new(changed.Distinct().ToArray(), errors.ToArray());
    }

    /// <summary>Records a task's new late receipts together, with deduplication inside the write transaction.</summary>
    private void RecordEvidence(TaskRecord task, IEnumerable<TurnStatusEntry> entries) => store.Write(s =>
    {
        var latest = s.Task(task.TaskId);
        if (latest.AttemptId != task.AttemptId) return false;
        var recorded = latest.TurnStatuses.Select(entry => (entry.Turn, entry.Hash)).ToHashSet();
        var changed = false;
        foreach (var entry in entries)
        {
            if (!recorded.Add((entry.Turn, entry.Hash))) continue;
            latest.TurnStatuses.Add(entry);
            s.Event(latest, "manual.receipt_evidence", entry.Sender, entry.At,
                $"turn={entry.Turn} file={entry.Source} task_state={latest.State} accepted=false");
            changed = true;
        }
        if (changed) s.Save(latest);
        return changed;
    });

    private enum ReceiptOutcome { None, Applied, Backwards }

    private ReceiptOutcome ApplyManualReceipt(string id, ManualReceipt receipt, string json, string source) =>
        store.Write(s => ApplyManualReceipt(s, s.Task(id), receipt, json, source));

    private ReceiptOutcome ApplyManualReceipt(StoreSession s, TaskRecord task, ManualReceipt receipt, string json, string? source)
    {
        var hash = ManualTransfers.Hash(json);
        if (Terminal(task.State)) return ReceiptOutcome.None;
        if (receipt.TaskId != task.TaskId || receipt.AttemptId != task.AttemptId)
            throw new RelayException("manual_receipt_identity", "回执的任务或尝试标识不匹配。");
        if (receipt.Turn < task.ManualTurn) return ReceiptOutcome.None;
        if (receipt.Turn != task.ManualTurn)
            throw new RelayException("manual_receipt_turn", "回执轮次与当前交接不匹配。");
        if (receipt.Sender != task.AwaitingAgentId)
            throw new RelayException("manual_receipt_sender", "此回执必须由当前等待的 Agent 提交。");
        if (receipt.Status is not ("received" or "working" or "done" or "failed"))
            throw new RelayException("manual_receipt_status", "回执状态应为 received、working、done 或 failed。");
        var alreadyRecorded = task.TurnStatuses.Any(entry => entry.Turn == receipt.Turn && entry.Hash == hash);
        if (receipt.Status == "received" && task.TurnStatuses.Any(entry => entry.Turn == receipt.Turn &&
                entry.Accepted && entry.Status is "working" or "done" or "failed"))
        {
            if (source is null)
                throw new RelayException("manual_receipt_order", "本轮已在处理中，不能退回 received。", 5);
            var fileName = Path.GetFileName(source);
            // An unchanged earlier receipt is harmless. A file that replaced its own working receipt is a new error.
            var lastFromFile = task.TurnStatuses.LastOrDefault(entry => entry.Turn == receipt.Turn && entry.Source == fileName);
            if (alreadyRecorded && (lastFromFile?.Status is not ("working" or "done" or "failed") ||
                task.TurnStatuses.Any(entry => entry.Turn == receipt.Turn && entry.Hash == hash && !entry.Accepted)))
                return ReceiptOutcome.None;
            task.TurnStatuses.Add(new(receipt.Turn, receipt.Sender, receipt.Status, Now, fileName, hash, Accepted: false));
            s.Save(task);
            s.Event(task, "manual.receipt_duplicate", receipt.Sender, Now, "turn=" + receipt.Turn + " status=received");
            return ReceiptOutcome.Backwards;
        }
        // The same status reported again (same file or a byte-identical copy) changes nothing.
        if (alreadyRecorded) return ReceiptOutcome.None;
        // Single handoffs are decided by needs_reply; review-loop fields an Agent adds there carry no decision.
        if (task.Collaboration is null) receipt = receipt with { Outcome = null, OpenItems = null, Deliverable = null };
        if (task.State == TaskState.Paused)
        {
            // A late receipt for the timed-out or unconfirmed-send turn is still the Agent's real result.
            if (task.PauseReason is not ("turn_timeout" or "delivery_failed"))
                throw new RelayException("handoff_paused", "交接已暂停，等待用户决定；暂停期间不接收新的回执。");
            task.State = task.PausedFrom ?? throw new RelayException("handoff_state_inconsistent",
                "账本缺少此交接暂停前的状态，无法导入该回执。其他任务仍可同步。", 5);
            task.PauseReason = null; task.PauseMessage = null; task.PausedFrom = null;
            task.DeadlineAt = Now + TurnTimeout(s, task);
            s.Event(task, "handoff.late_receipt", receipt.Sender, Now, "turn=" + receipt.Turn + " reason=resumed");
        }
        RecordCliDeliveryEvidence(s, task, receipt);
        if (receipt.Status is "received" or "working")
        {
            if (receipt.NeedsReply == true || receipt.Outcome is not null)
                throw new RelayException("manual_receipt_reply", "只有完成当前工作后才能要求回传或给出结论。");
            task.State = receipt.Status == "received" ? TaskState.Acknowledged : TaskState.Running;
            if (receipt.Status == "received") task.AcknowledgedAt ??= Now; else task.WorkingAt ??= Now;
        }
        else if (task.Collaboration is { } collaboration) ApplyCollaborationOutcome(s, task, collaboration, receipt);
        else ApplyPlainOutcome(s, task, receipt);
        var recordedSource = source is null ? "handoff status" : Path.GetFileName(source);
        task.TurnStatuses.Add(new(receipt.Turn, receipt.Sender, receipt.Status, Now, recordedSource, hash));
        if (task.ManualResendTurn == receipt.Turn) task.ManualResendTurn = null;
        task.ManualReceiptJson = json;
        s.Save(task);
        s.Event(task, "manual." + receipt.Status, receipt.Sender, Now,
            "turn=" + receipt.Turn + (receipt.Outcome is null ? "" : " outcome=" + receipt.Outcome));
        s.Notify(task, "manual_turn_" + receipt.Turn + "_" + receipt.Status,
            task.State == TaskState.AwaitingReply ? "请将交接交给 " + (Agent(s, task.AwaitingAgentId!).DisplayName ?? task.AwaitingAgentId) + " 继续处理。" :
            task.State == TaskState.Paused ? task.PauseMessage! :
            task.State == TaskState.Succeeded ? task.Collaboration is null ? "本次交接已结束，无需继续传递。" : "验收已完成，协作已结束。" :
            task.ErrorMessage ?? "交接进度已更新。", Now);
        return ReceiptOutcome.Applied;
    }

    private void ApplyPlainOutcome(StoreSession s, TaskRecord task, ManualReceipt receipt)
    {
        if (receipt.NeedsReply is not { } needsReply)
            throw new RelayException("manual_receipt_reply", "完成或失败回执需要填写 needs_reply。");
        // A user-defined relay plan decides who works next and where the flow ends. Legacy fixed plans keep their old rules.
        var relay = task.RelaySteps is { Length: > 0 } ? task.RelaySteps : null;
        var steps = relay ?? [];
        var finalStep = relay is not null && task.ManualTurn + 1 >= relay.Length;
        var plannedNext = receipt.Status == "done" && (relay is not null ? !finalStep
            : task.HandoffPlan is { } legacy && task.ManualTurn + 1 < legacy.Length);
        if (receipt.Status == "failed" && needsReply)
            throw new RelayException("manual_receipt_reply", "失败回执不能自动开启下一轮。");
        if (receipt.Status == "done" && finalStep && needsReply)
            throw new RelayException("relay_final_step",
                "这是接力计划的最后一步，不能自动开启下一轮。整项工作已完成请设置 needs_reply=false；仍需继续请先由用户追加步骤。");
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
        if (continues && (string.IsNullOrWhiteSpace(receipt.ResultFile) || string.IsNullOrWhiteSpace(receipt.NextAction) && !plannedNext))
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
        if (!continues)
        {
            task.State = TaskState.Succeeded;
            task.CompletionNote = CompletionNote(task);
            if (task.CompletionNote is not null)
                s.Event(task, "handoff.modified_not_retested", receipt.Sender, Now, "plan ended on a modify step after a test step");
            return;
        }
        var next = plannedNext ? relay is not null ? relay[task.ManualTurn + 1].AgentId : task.HandoffPlan![task.ManualTurn + 1]
            : receipt.Sender == task.RequesterId ? task.RecipientId : task.RequesterId;
        var action = plannedNext && relay is not null ? PlannedAction(task, receipt.NextAction) : receipt.NextAction;
        var limit = task.MaxTurns ?? MaxManualTurns;
        AdvanceFileHandoff(s, task, next, action);
        if (relay is not null)
        {
            // A relay plan is bounded by its own steps; reaching the end is normal, not a round-limit pause.
            if (task.NewSessionEachTurn) StartFreshSession(s, task, next);
            return;
        }
        if (!task.ManualContinuous && task.ManualTurn >= limit)
            PauseFileHandoff(s, task, "round_limit", $"交接已往返 {limit} 轮，已暂停继续传递，避免重复往返。请决定追加轮数继续、取消或人工标记完成。", TaskState.AwaitingReply);
    }

    /// <summary>The declared duty of the next step leads; the finished step's hand-over note follows it.</summary>
    private static string? PlannedAction(TaskRecord task, string? note)
    {
        // The duty that leads is the duty of the step that is starting now.
        var duty = StepDuty(task, task.ManualTurn + 1);
        if (string.IsNullOrWhiteSpace(duty)) return note;
        return string.IsNullOrWhiteSpace(note) ? duty : duty + "\n\n上一步交接说明：" + note.Trim();
    }

    /// <summary>Chosen per task: every later turn for the same Agent starts a new session instead of resuming.</summary>
    private void StartFreshSession(StoreSession s, TaskRecord task, string agentId)
    {
        var existing = ReadTaskSession(s, task, agentId);
        if (existing is null) return;
        var agent = Agent(s, agentId);
        SaveTaskSession(s, task, new(task.TaskId, task.AttemptId, task.ManualTurn, agentId, agent.Host,
            null, null, null, null, "new", "unknown", Now, "operator chose a new session for every turn"));
        task.CliFileTurn = null; task.CliFileStatus = null; task.CliFileSessionId = null; task.CliFileError = null;
        s.Event(task, "session.new_each_turn", agentId, Now, "turn=" + task.ManualTurn);
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

    /// <summary>
    /// Lightweight registration: the Agent names its status and Relay fills the identity fields, so nobody has to
    /// hand-write the receipt JSON. Only the current waiting Agent and turn are accepted.
    /// </summary>
    public TaskView RegisterTurnStatus(string taskId, string status, string? note = null) => store.Write(s =>
    {
        var task = s.Task(taskId);
        if (!task.IsFileHandoff) throw new RelayException("file_handoff_required", "此接口只用于文件或 CLI 工作交接。");
        if (status is not ("received" or "working"))
            throw new RelayException("manual_receipt_status", "轻量登记只接受 received 或 working；完成或失败请写完整回执。");
        if (Terminal(task.State)) throw new RelayException("session_task_finished", "任务已结束，不能再登记状态。");
        var receipt = new ManualReceipt(task.TaskId, task.AttemptId, task.AwaitingAgentId ?? task.RecipientId,
            task.ManualTurn, status, NextAction: string.IsNullOrWhiteSpace(note) ? null : note.Trim());
        // The ledger is the receipt for this command. The Agent owns the on-disk receipt path.
        ApplyManualReceipt(s, task, receipt, RelayJson.Write(receipt), source: null);
        return ManualView(s.Task(taskId));
    });

    public string TurnReplyPath(TaskRecord task) => Path.Combine(workspace.AttemptDirectory(task.TaskId, task.AttemptId),
        task.ManualTurn == 0 ? "reply.md" : $"reply-{task.ManualTurn}.md");

    private TaskView ManualView(TaskRecord task)
    {
        var paused = task.State == TaskState.Paused;
        var overdue = !Terminal(task.State) && !paused && task.DeadlineAt <= Now;
        var finished = Terminal(task.State);
        var done = task.State == TaskState.Succeeded
            ? task.CompletionNote == "modified_not_retested" ? "修改后未复测：修改步骤之后没有复测步骤，未声称测试通过。" :
              task.Collaboration?.Deliverable is { } delivered ? "验收完成，已交付：" + delivered : "交接已完成，无需继续传递。"
            : task.ErrorMessage ?? "交接已结束。";
        return new(task, overdue, finished ? "inspect" : paused ? "decide_paused_handoff" : "copy_file_handoff", finished ? done :
            paused ? task.PauseMessage! :
            $"有文件交接给你。请读取 {ManualContextPath(task)}，完成其中要求并写回执，无需额外寒暄或确认消息。");
    }
}
