namespace AgentRelay.Core;

public static class DemoWorkspace
{
    public static async Task<RelayService> CreateAsync(string home)
    {
        var root = Path.GetFullPath(home);
        // Demo data must never replace an existing registry or host configuration.
        if (Directory.Exists(root) || File.Exists(root))
            throw new RelayException("demo_directory_exists", "演示目录必须是新路径；现有数据未被改动。", 3);
        var service = new RelayService(root);
        service.Initialize();
        var configs = Path.Combine(root, "sample-agents");
        Directory.CreateDirectory(configs);
        foreach (var id in new[] { "agent-a", "agent-b", "agent-c" })
        {
            var path = Path.Combine(configs, id + ".json");
            File.WriteAllText(path, RelayJson.Write(new { name = id, demo = true }));
            service.Register(id, "demo", path);
        }
        service.ValidateAgent("agent-a");
        service.ValidateAgent("agent-b");
        service.SetPermission("agent-a", "agent-b", "allow");
        service.SetPermission("agent-b", "agent-a", "deny");
        service.SetPermission("agent-a", "agent-c", "allow");

        var ready = (await service.DispatchAsync(new("agent-a", "agent-b",
            "# Demo: Result Ready\n\n计算 18 × 3。此任务和结果均为本地演示样本，没有调用模型。",
            "manual", "manual", "demo-ready"))).Task;
        service.Acknowledge(ready.TaskId, ready.AttemptId, "agent-b", "demo-ack", start: true);
        File.WriteAllText(ready.ResultPath, "# Demo Result\n\n18 × 3 = 54。\n\n这是预设演示结果，不代表真实 Agent 执行。\n");
        service.Complete(ready.TaskId, ready.AttemptId, "agent-b", "demo-done", ready.ResultPath);

        var failed = (await service.DispatchAsync(new("agent-a", "agent-b",
            "# Demo: Explicit Failure\n\n演示如何保留接收方失败报告；未调用模型。",
            "manual", "manual", "demo-failed"))).Task;
        File.WriteAllText(failed.ResultPath, "# Demo Failure\n\n示例输入文件缺失。此错误是预设演示数据。\n");
        service.Complete(failed.TaskId, failed.AttemptId, "agent-b", "demo-fail", failed.ResultPath,
            failed: true, errorCode: "demo_input_missing", errorMessage: "演示：输入文件缺失。", sideEffectsPossible: false);

        await service.DispatchAsync(new("agent-a", "agent-b", "# Demo: Manual Handoff\n\n复制此请求提示词，查看显式 ack/complete 协议。未启动 Agent。",
            "manual", "manual", "demo-waiting"));
        File.WriteAllText(Path.Combine(root, "DEMO.md"), "# TaskBaton Demo\n\n仅包含虚构 Agent、本地配置和预设任务。未使用模型、认证或宿主会话。\n\n关闭演示窗口后，可删除这个独立目录。不要在此登记真实认证。\n");
        return service;
    }
}
