using System.Globalization;
using System.Text;
using TaskBaton.Core;
using TaskBaton.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
try
{
    string[] allowed = ["--home", "--agent", "--route-id", "--worker-task", "--worker-attempt", "--timeout-seconds", "--report-path"];
    var values = new Dictionary<string, string>();
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 == args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
            throw new RelayException("invalid_arguments", "使用 taskbaton-mcp --home <目录> --agent <Agent ID> [--route-id <迁移ID>]。");
    }
    if (!values.TryGetValue("--agent", out var agent) || string.IsNullOrWhiteSpace(agent))
        throw new RelayException("invalid_arguments", "必须通过 --agent 绑定已登记的 Agent。");
    var home = TaskBatonHome.Resolve(values.GetValueOrDefault("--home"));
    var service = new RelayService(home);
    service.RequireMcpAgent(agent);
    if (values.TryGetValue("--worker-task", out var taskId))
    {
        if (!values.TryGetValue("--worker-attempt", out var attempt) || !values.TryGetValue("--report-path", out var report))
            throw new RelayException("invalid_worker", "单次 worker 缺少 attempt 或 report-path。");
        var seconds = int.Parse(values["--timeout-seconds"], CultureInfo.InvariantCulture);
        var result = await service.RunTaskAsync(taskId, attempt, agent, seconds);
        File.WriteAllText(report, RelayJson.Write(result));
        return result.Task.ErrorCode == "execution_unknown" ? 6 : 0;
    }
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, Args = [] });
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton(service);
    builder.Services.AddSingleton(new McpIdentity(agent, values.GetValueOrDefault("--route-id") ?? "direct"));
    builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "taskbaton", Version = RelayVersion.Current };
        options.ServerInstructions = "派发后结束当前轮，等待继续消息或用户提示。不要循环查询结果。完成必须显式 complete/fail。服务器身份固定，权限与 CLI 共用。";
    }).WithStdioServerTransport().WithTools<RelayTools>(serializerOptions: RelayJson.Options);
    await builder.Build().RunAsync();
    return 0;
}
catch (Exception error) when (error is RelayException or IOException or UnauthorizedAccessException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
{
    Console.Error.WriteLine(error.Message);
    return error is RelayException relay ? relay.ExitCode : 1;
}
