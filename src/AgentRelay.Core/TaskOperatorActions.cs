namespace AgentRelay.Core;

public sealed partial class RelayService
{
    public void DeleteTask(string taskId, bool approved)
    {
        if (!approved) throw new RelayException("approval_required", "删除任务前需要明确确认。", 4);
        store.Write(s =>
        {
            PreserveWorkspaceOwnership(s, s.Task(taskId));
            foreach (var table in new[] { "callbacks", "events", "outbox", "notifications", "executions", "continuations", "task_session_bindings" })
                s.Execute($"DELETE FROM {table} WHERE task_id=$id", ("$id", taskId));
            s.Execute("DELETE FROM tasks WHERE id=$id", ("$id", taskId));
            return true;
        });
    }

    public TaskView ForceCompleteTask(string taskId, string reason, bool approved)
    {
        if (!approved) throw new RelayException("approval_required", "人工标记完成前需要明确确认。", 4);
        RequireReason(reason);
        return store.Write(s =>
        {
            var task = s.Task(taskId);
            if (task.State == TaskState.Succeeded) return View(task, s);
            task.State = TaskState.Succeeded;
            task.ErrorCode = null; task.ErrorMessage = null;
            task.ResumeStatus = "operator_completed";
            s.Execute("UPDATE outbox SET state='settled',lease_until=NULL WHERE task_id=$id", ("$id", taskId));
            foreach (var continuation in s.Query("SELECT json FROM continuations WHERE task_id=$id",
                r => RelayJson.Read<ContinuationRecord>(r.GetString(0)), ("$id", taskId)))
                SaveContinuation(s, continuation with { State = "superseded", DesktopState = "not_required", LeaseUntil = null });
            s.Save(task);
            s.Event(task, "task.operator_completed", "operator", Now, reason);
            s.Notify(task, "operator_completed", "用户已标记交接完成，未据此验证 Agent 执行结果。", Now);
            return View(task, s);
        });
    }
}
