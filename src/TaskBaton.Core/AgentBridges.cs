namespace TaskBaton.Core;

/// <param name="Kind">acp, desktop (the running desktop app's own API), cli, or none when only manual copy works.</param>
/// <param name="State">verified, unverified, blocked or manual.</param>
/// <param name="HostNote">Why this host cannot have a bridge at all, when that is known.</param>
public sealed record AgentBridge(string AgentId, string Kind, string State, bool CanCreate, bool CanResume, string? BlockMessage,
    string? HostNote = null);

public sealed partial class RelayService
{
    /// <summary>How turns reach this Agent: the registered bridge and how far it has been proven.</summary>
    public AgentBridge Bridge(string agentId)
    {
        var host = Agents().Single(a => a.AgentId == agentId).Host;
        if (CliExtension(agentId) is not { } extension) return new(agentId, "none", "manual", false, false, null, HostLimitation(host));
        var kind = CliRecipes.OutputFormatFor(extension.Recipe) switch { "acp" => "acp", "dsh-desktop" => "desktop", _ => "cli" };
        var state = extension.Block is not null ? "blocked" : extension.CanCreate || extension.CanResume ? "verified" : "unverified";
        return new(agentId, kind, state, extension.CanCreate, extension.CanResume, extension.Block?.Message, HostLimitation(host));
    }

    /// <summary>Hosts known to expose no usable external interface, so users see the reason instead of suspecting Relay.</summary>
    public static string? HostLimitation(string host) =>
        host.Contains("WorkBuddy", StringComparison.OrdinalIgnoreCase) || host.Contains("CodeBuddy", StringComparison.OrdinalIgnoreCase)
            ? "WorkBuddy 桌面版没有对外开放的调用接口：它自带的命令行单独启动时用不了桌面登录，打开中的对话只接受桌面自己写入（同一对话只允许一个写入方），外部消息会被忽略。这是 WorkBuddy 的设计限制，不是配置错误；交给它的任务请复制提示词手动交接，回执照常推进。" : null;
}
