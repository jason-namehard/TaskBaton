namespace AgentRelay.Core;

public sealed partial class RelayService
{
    public async Task<List<TaskView>> RecoverAsync(string? taskId = null, CancellationToken cancellationToken = default)
    {
        var ids = store.Write(s =>
        {
            var tasks = taskId is null
                ? s.Query("SELECT json FROM tasks", r => RelayJson.Read<TaskRecord>(r.GetString(0)))
                : [s.Task(taskId)];
            foreach (var task in tasks)
            {
                RecoverContinuations(s, task);
                if (Terminal(task.State)) continue;
                var execution = Execution(s, task.TaskId, task.AttemptId);
                if (execution is { State: "scheduled" or "receiving" or "running" } && execution.LeaseUntil <= Now)
                {
                    SaveExecution(s, execution with { State = "unknown", ErrorCode = "execution_lease_expired" });
                    task.ErrorCode = "execution_unknown";
                    task.ErrorMessage = "单次 worker 未在时限内留下最终声明。";
                    s.Save(task); s.Event(task, "execution.unknown", "relay", Now, "execution_lease_expired");
                    s.Notify(task, "execution_attention", "执行结果未知，不会自动重新启动 Agent。", Now);
                }
                var box = Outbox(s, task.TaskId);
                if (task.IsFileHandoff)
                {
                    // File handoffs keep their sessions and files when a turn runs over; the user decides how to go on.
                    if (task.State != TaskState.Paused && task.DeadlineAt <= Now) { PauseForTimeout(s, task); s.Save(task); }
                    else if (RecoverExpiredFileDelivery(s, task)) s.Save(task);
                }
                else if (task.DeadlineAt <= Now)
                {
                    task.State = TaskState.TimedOut; s.Save(task);
                    s.Event(task, "task.timed_out", "relay", Now);
                    s.Notify(task, "timed_out", "截止时间已过，未收到有效完成结果；宿主可能仍在执行。", Now);
                    if (!task.IsFileHandoff) QueueContinuation(s, task, "timed_out");
                    s.Execute("UPDATE outbox SET state='settled',lease_until=NULL WHERE task_id=$id", ("$id", task.TaskId));
                }
                else if (box is { State: "in_flight", LeaseUntil: not null } && box.LeaseUntil <= Now)
                {
                    task.State = TaskState.DeliveryUnknown; s.Save(task);
                    s.Execute("UPDATE outbox SET state='unknown',lease_until=NULL WHERE task_id=$id", ("$id", task.TaskId));
                    s.Event(task, "task.delivery_unknown", "relay", Now, "expired in-flight lease; do not resend");
                    s.Notify(task, "delivery_attention", "运输进程未留下确定结果，禁止自动重发。", Now);
                }
            }
            return tasks.Select(t => t.TaskId).ToArray();
        });
        foreach (var id in ids)
        {
            await DeliverPendingAsync(id, cancellationToken);
            await ProcessContinuationsAsync(id, cancellationToken: cancellationToken);
        }
        return ids.Select(Status).ToList();
    }
}
