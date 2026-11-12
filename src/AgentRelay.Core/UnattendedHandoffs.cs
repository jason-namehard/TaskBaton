namespace AgentRelay.Core;

public sealed record UnattendedHandoffRun(DateTimeOffset EndsAt, int SendLimit, int SendsStarted = 0,
    bool Stopped = false, string? SessionIssue = null);

public enum UnattendedHandoffStatus { Active, Finished, Stopped, TimeLimit, SendLimit, SessionUnverified, HostUnconfirmed }

public sealed partial class RelayService
{
    public const int MaxUnattendedSends = 50;
    public const int MaxUnattendedMinutes = 24 * 60;

    public UnattendedHandoffStatus? UnattendedStatus(TaskRecord task)
    {
        if (task.UnattendedRun is not { } run) return null;
        if (Terminal(task.State)) return UnattendedHandoffStatus.Finished;
        if (run.Stopped || !task.AutoDeliver) return UnattendedHandoffStatus.Stopped;
        if (run.SessionIssue is not null) return UnattendedHandoffStatus.SessionUnverified;
        if (run.EndsAt <= Now) return UnattendedHandoffStatus.TimeLimit;
        if (run.SendsStarted >= run.SendLimit) return UnattendedHandoffStatus.SendLimit;
        // A receipt can advance the workflow even when the host never confirmed the previous turn.
        if (task.CliFileStatus is "unknown" or "failed" ||
            task.CliFileStatus == "submitted" && task.CliFileSessionId is null)
            return UnattendedHandoffStatus.HostUnconfirmed;
        return UnattendedHandoffStatus.Active;
    }

    public void StartUnattendedHandoff(string taskId, int sendLimit, int minutes)
    {
        if (sendLimit is < 1 or > MaxUnattendedSends || minutes is < 1 or > MaxUnattendedMinutes)
            throw new RelayException("unattended_limits", $"自动投递次数须为 1–{MaxUnattendedSends}，时限须为 1–{MaxUnattendedMinutes} 分钟。");
        store.Write(s =>
        {
            var task = s.Task(taskId);
            if (!task.IsFileHandoff || task.ManualContinuous || Terminal(task.State))
                throw new RelayException("unattended_unavailable", "此任务无法开启无人值守运行。");
            if (UnattendedStatus(task) is UnattendedHandoffStatus.Active or UnattendedHandoffStatus.HostUnconfirmed)
                throw new RelayException("unattended_active", "此任务已有无人值守运行，请先停止当前运行。");
            var now = Now;
            task.AutoDeliver = true;
            task.UnattendedRun = new(now.AddMinutes(minutes), sendLimit);
            s.Save(task);
            s.Event(task, "handoff.unattended_started", "operator", now, RelayJson.Write(task.UnattendedRun));
            return true;
        });
    }

    public void StopUnattendedHandoff(string taskId) => store.Write(s =>
    {
        var task = s.Task(taskId);
        if (task.UnattendedRun is not { Stopped: false } run || Terminal(task.State))
            throw new RelayException("unattended_inactive", "此任务没有可停止的无人值守运行。");
        task.UnattendedRun = run with { Stopped = true };
        s.Save(task);
        s.Event(task, "handoff.unattended_stopped", "operator", Now);
        return true;
    });

    private void RecordUnattendedSessionIssue(TaskRecord original, string message) => store.Write(s =>
    {
        var task = s.Task(original.TaskId);
        if (task.AttemptId != original.AttemptId || task.ManualTurn != original.ManualTurn ||
            task.CliFileTurn == task.ManualTurn || task.State is not (TaskState.AwaitingManual or TaskState.AwaitingReply) ||
            task.UnattendedRun != original.UnattendedRun || UnattendedStatus(task) != UnattendedHandoffStatus.Active)
            return false;
        task.UnattendedRun = task.UnattendedRun! with { SessionIssue = message };
        s.Save(task);
        s.Event(task, "handoff.unattended_session_unverified", "relay", Now, message);
        return true;
    });
}
