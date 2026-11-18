namespace TaskBaton.Core;

public sealed record BridgeRecoveryResult(bool BlockCleared,string Message,IReadOnlyList<CliHealthCheck> Checks);

public sealed partial class RelayService
{
    /// <summary>Rechecks DSH's local connection and clears the same saved authentication failure only when checks pass.</summary>
    public async Task<BridgeRecoveryResult> RecheckDesktopBridgeAsync(string agentId,CancellationToken cancellationToken=default)
    {
        var before=CliExtension(agentId)??throw new RelayException("cli_recipe_missing","未找到调用方案。");
        if(CliRecipes.OutputFormatFor(before.Recipe)!="dsh-desktop")
            throw new RelayException("bridge_recovery_unsupported","此恢复检查用于DSH桌面桥接；其他方案请使用配置检查或诊断提示词。");
        return ApplyDesktopBridgeRecovery(before,await CliHealthAsync(agentId,cancellationToken));
    }

    internal BridgeRecoveryResult ApplyDesktopBridgeRecovery(CliExtensionRecord before,IReadOnlyList<CliHealthCheck> checks)
    {
        var failures=checks.Where(c=>c.Id!="block"&&c.Status=="fail").ToArray();
        if(!checks.Any(c=>c.Id=="host.desktop.connection"&&c.Status=="pass")||failures.Length>0)
            return new(false,"连接检查尚未通过，原阻塞保留。请查看当前检查结果或复制诊断提示词。",checks);
        return store.Write(s=>
        {
            var current=ReadCliExtension(s,before.AgentId)??throw new RelayException("cli_recipe_missing","调用方案已移除。");
            if(current.Revision!=before.Revision||current.Block!=before.Block)
                throw new RelayException("cli_recipe_changed","检查期间方案或失败记录已变化，请重新检查后恢复。");
            if(current.Block is null)return new BridgeRecoveryResult(false,"当前连接检查通过，没有需要解除的阻塞。",checks);
            if(current.Block.Kind!="auth_required")
                return new BridgeRecoveryResult(false,"当前连接可用，但还有其他类型的阻塞，请处理对应问题后恢复。",checks);
            SaveCliExtension(s,current with {Block=null});
            return new BridgeRecoveryResult(true,"当前连接检查通过，历史认证阻塞已解除。已授权自动任务可以继续。"+
                (current.CanResume?"续接能力已有验证记录。":"续接能力尚未验证，暂不自动续接；可手动发送一轮并等待真实回执，或完成续接探针确认。"),checks);
        });
    }
}
