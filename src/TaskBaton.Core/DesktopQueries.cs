namespace TaskBaton.Core;

public sealed record PermissionRecord(string From, string To, string Decision);
public sealed record ConnectionView(string From, string To, string Permission, string Capability, string Display, string Reason);
public sealed record HomeSummary(int RegisteredAgents, int PendingTasks, int NeedsAttention, int RetentionDays);

public sealed partial class RelayService
{
    public List<PermissionRecord> ConnectionPermissions() => store.Read(s => s.Query(
        "SELECT sender,recipient,decision FROM permissions ORDER BY sender,recipient",
        r => new PermissionRecord(r.GetString(0), r.GetString(1), r.GetString(2))));

    public List<ConnectionView> Connections()
    {
        var agents = Agents();
        var permissions = ConnectionPermissions().ToDictionary(p => (p.From, p.To));
        return (from sender in agents from recipient in agents where sender.AgentId != recipient.AgentId
            let permission = permissions.TryGetValue((sender.AgentId, recipient.AgentId), out var p) ? p.Decision : "default_deny"
            let cli = CliExtension(recipient.AgentId) is not null
            let available = cli
            select new ConnectionView(sender.AgentId, recipient.AgentId, permission,
                available ? "registered CLI" : "manual",
                // The graph is informational: it never pauses a route. Delivery depends on the recipient's verified bridge.
                available ? "Available" : "Unavailable",
                available ? "目标 Agent 已登记调用方案；有已验证桥接时自动投递，否则复制提示词手动交接。连接图不再单独开关方向。" : "缺少调用方案；可登记调用方式，或手动复制提示词。"))
            .ToList();
    }

    public static bool NeedsAttention(TaskView view) => view.DeadlineExceeded ||
        view.Task.State is TaskState.Failed or TaskState.TimedOut or TaskState.DeliveryUnknown or TaskState.Paused ||
        view.Task.ErrorCode == "execution_unknown" ||
        (view.Task.State == TaskState.Succeeded && !view.Task.IsFileHandoff && view.Task.ResumeStatus is not ("submitted" or "completed" or "operator_completed"));

    public HomeSummary Summary()
    {
        var tasks = Tasks();
        return new(Agents().Count, tasks.Count(t => !Terminal(t.Task.State)), tasks.Count(NeedsAttention), Settings().ArtifactTtlDays);
    }

    public void MarkNotificationRead(long notificationId) => store.Write(s =>
    {
        if (s.Execute("UPDATE notifications SET is_read=1 WHERE id=$id", ("$id", notificationId)) != 1)
            throw new RelayException("notification_not_found", "未找到该通知。", 3);
        return true;
    });
}
