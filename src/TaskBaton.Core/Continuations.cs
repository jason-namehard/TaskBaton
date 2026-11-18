using System.ComponentModel;

namespace TaskBaton.Core;

public sealed partial class RelayService
{
    public async Task<ResumerRecord> VerifyResumerAsync(string agentId, string kind, CancellationToken cancellationToken = default)
    {
        var agent = store.Read(s => Agent(s, agentId));
        RequirePaths(agent);
        var registration = await CodexSessionResumer.VerifyAsync(agent, kind, cancellationToken);
        return store.Write(s =>
        {
            s.Execute("INSERT INTO resumers VALUES($id,$json) ON CONFLICT(agent_id) DO UPDATE SET json=excluded.json",
                ("$id", agentId), ("$json", RelayJson.Write(registration)));
            return registration;
        });
    }

    public List<ResumerRecord> Resumers() => store.Read(s => s.Query("SELECT json FROM resumers ORDER BY agent_id",
        r => RelayJson.Read<ResumerRecord>(r.GetString(0))));

    public List<ContinuationRecord> Continuations(string taskId) => store.Read(s => s.Query(
        "SELECT json FROM continuations WHERE task_id=$task ORDER BY rowid", r => RelayJson.Read<ContinuationRecord>(r.GetString(0)), ("$task", taskId)));

    private void QueueContinuation(StoreSession s, TaskRecord task, string eventName)
    {
        var entry = new ContinuationRecord(task.TaskId, task.AttemptId, eventName, "pending", "pending", null);
        s.Execute("INSERT OR IGNORE INTO continuations VALUES($task,$attempt,$event,$json)",
            ("$task", task.TaskId), ("$attempt", task.AttemptId), ("$event", eventName), ("$json", RelayJson.Write(entry)));
        task.ResumeStatus = "pending";
        s.Save(task);
    }

    public async Task ProcessContinuationsAsync(string taskId, ISessionResumer? resumer = null,
        IDesktopNotifier? notifier = null, int timeoutSeconds = 180, CancellationToken cancellationToken = default)
    {
        ValidateExecutionTimeout(timeoutSeconds);
        var keys = Continuations(taskId);
        foreach (var key in keys)
        {
            var claimed = store.Write(s =>
            {
                var current = ReadContinuation(s, key);
                if (current.State != "pending") return null;
                var task = s.Task(taskId);
                if (task.AttemptId != key.AttemptId || task.State == TaskState.Cancelled ||
                    (key.Event == "timed_out" && task.State != TaskState.TimedOut))
                {
                    SaveContinuation(s, current with { State = "superseded", DesktopState = "not_required" });
                    return null;
                }
                SaveContinuation(s, current with { State = "in_flight", LeaseUntil = Now.AddSeconds(timeoutSeconds + 45) });
                s.Event(task, "session.resume_claimed", "relay", Now, key.Event);
                return task;
            });
            if (claimed is not null)
            {
                ResumeResult result;
                try
                {
                    if (key.Event == "timed_out" || claimed.OriginSession is null || (claimed.ResumeAdapter is null && resumer is null))
                        result = new ResumeResult("manual_required", "resume_unavailable", "此任务没有可用的精确会话恢复绑定。");
                    else
                    {
                        RequireContinuationArtifacts(claimed);
                        if (resumer is null)
                        {
                            var agent = store.Read(s => Agent(s, claimed.RequesterId));
                            RequirePaths(agent);
                            CodexSessionResumer.RequireBinding(agent, claimed.ResumeAdapter!, claimed.OriginSession);
                        }
                        var directory = workspace.RequireManaged(Path.Combine(workspace.AttemptDirectory(taskId, key.AttemptId), "continuation", key.Event));
                        Directory.CreateDirectory(directory);
                        var adapter = resumer ?? new CodexSessionResumer(claimed.ResumeAdapter!);
                        result = await adapter.ResumeAsync(new ResumeInput(claimed, View(claimed).Prompt, directory, timeoutSeconds), cancellationToken);
                    }
                }
                // External CLI/config/filesystem failures keep the accepted task result intact.
                catch (RelayException error) { result = new ResumeResult("failed", error.Code, error.Message); }
                catch (Win32Exception error) { result = new ResumeResult("failed", "resumer_start_failed", error.Message); }
                catch (IOException error) { result = new ResumeResult("failed", "resume_file_error", error.Message); }
                catch (UnauthorizedAccessException error) { result = new ResumeResult("failed", "resume_access_denied", error.Message); }
                if (result.State is not ("submitted" or "completed" or "failed" or "unknown" or "manual_required"))
                    throw new InvalidOperationException("Resumer returned an invalid state.");
                store.Write(s =>
                {
                    var current = ReadContinuation(s, key);
                    if (current.State != "in_flight") return false;
                    var task = s.Task(taskId);
                    SaveContinuation(s, current with { State = result.State, LeaseUntil = null, ErrorCode = result.ErrorCode,
                        Detail = result.Detail, OutputPath = result.OutputPath,
                        DesktopState = result.State is "submitted" or "completed" ? "not_required" : "pending" });
                    if (task.AttemptId == key.AttemptId)
                    {
                        task.ResumeStatus = result.State; s.Save(task);
                        s.Event(task, "session.resume_" + result.State, "relay", Now, result.Detail ?? "");
                        if (result.State is not ("submitted" or "completed"))
                            s.Notify(task, "resume_attention:" + key.Event, "自动继续不可用，请复制提示词。" + result.Detail, Now);
                    }
                    return true;
                });
            }
            await DeliverDesktopAsync(key, notifier, cancellationToken);
        }
    }

    private void RequireContinuationArtifacts(TaskRecord task)
    {
        var valid = store.Read(s => s.Json<ArtifactRecord>("SELECT json FROM artifacts WHERE path=$path", ("$path", task.ResultPath)));
        if (valid is not { State: "available" } || valid.ExpiresAt <= Now || !File.Exists(task.ResultPath) || !File.Exists(task.RequestPath))
            throw new RelayException("continuation_expired", "继续所需产物已过期或缺失，请检查任务记录。", 3);
    }

    private async Task DeliverDesktopAsync(ContinuationRecord key, IDesktopNotifier? notifier, CancellationToken cancellationToken)
    {
        var task = store.Write(s =>
        {
            var current = ReadContinuation(s, key);
            if (current.State is "pending" or "in_flight" || current.DesktopState != "pending") return null;
            var value = s.Task(key.TaskId);
            if (value.AttemptId != key.AttemptId || value.State == TaskState.Cancelled)
            {
                SaveContinuation(s, current with { DesktopState = "not_required" });
                return null;
            }
            SaveContinuation(s, current with { DesktopState = "in_flight", LeaseUntil = Now.AddSeconds(45) });
            return value;
        });
        if (task is null) return;
        notifier ??= new DesktopNotifier(DesktopExecutable());
        ResumeResult result;
        try { result = await notifier.NotifyAsync(Home, task, cancellationToken); }
        catch (Win32Exception error) { result = new ResumeResult("failed", "desktop_start_failed", error.Message); }
        catch (IOException error) { result = new ResumeResult("failed", "desktop_file_error", error.Message); }
        catch (UnauthorizedAccessException error) { result = new ResumeResult("failed", "desktop_access_denied", error.Message); }
        catch (RelayException error) { result = new ResumeResult("failed", error.Code, error.Message); }
        catch (System.Text.Json.JsonException error) { result = new ResumeResult("failed", "desktop_report_invalid", error.Message); }
        store.Write(s =>
        {
            var current = ReadContinuation(s, key);
            if (current.DesktopState != "in_flight") return false;
            SaveContinuation(s, current with { DesktopState = result.State, LeaseUntil = null,
                Detail = current.Detail + "\n桌面通知：" + result.Detail });
            s.Event(s.Task(key.TaskId), "desktop." + result.State, "relay", Now, result.ErrorCode ?? "");
            return true;
        });
    }

    private void RecoverContinuations(StoreSession s, TaskRecord task)
    {
        var entries = s.Query("SELECT json FROM continuations WHERE task_id=$id", r => RelayJson.Read<ContinuationRecord>(r.GetString(0)), ("$id", task.TaskId));
        foreach (var entry in entries.Where(e => e.LeaseUntil <= Now && (e.State == "in_flight" || e.DesktopState == "in_flight")))
        {
            var unknown = entry with { State = entry.State == "in_flight" ? "unknown" : entry.State,
                DesktopState = entry.DesktopState == "in_flight" ? "unknown" : "pending", LeaseUntil = null,
                ErrorCode = "continuation_lease_expired", Detail = "提交期间进程中断，禁止自动重复唤醒；请检查原会话或复制提示词。" };
            SaveContinuation(s, unknown);
            if (task.AttemptId == entry.AttemptId)
            {
                task.ResumeStatus = unknown.State; s.Save(task);
                s.Event(task, entry.State == "in_flight" ? "session.resume_unknown" : "desktop.unknown", "relay", Now, unknown.Detail!);
                s.Notify(task, "resume_attention:" + entry.Event, unknown.Detail!, Now);
            }
        }
    }

    private static ContinuationRecord ReadContinuation(StoreSession s, ContinuationRecord key) => s.Json<ContinuationRecord>(
        "SELECT json FROM continuations WHERE task_id=$task AND attempt_id=$attempt AND event=$event",
        ("$task", key.TaskId), ("$attempt", key.AttemptId), ("$event", key.Event))!;
    private static ContinuationRecord? LatestContinuation(StoreSession s, TaskRecord task) => s.Json<ContinuationRecord>(
        "SELECT json FROM continuations WHERE task_id=$task AND attempt_id=$attempt ORDER BY rowid DESC LIMIT 1",
        ("$task", task.TaskId), ("$attempt", task.AttemptId));
    private static void SaveContinuation(StoreSession s, ContinuationRecord entry) => s.Execute(
        "UPDATE continuations SET json=$json WHERE task_id=$task AND attempt_id=$attempt AND event=$event",
        ("$json", RelayJson.Write(entry)), ("$task", entry.TaskId), ("$attempt", entry.AttemptId), ("$event", entry.Event));
}
