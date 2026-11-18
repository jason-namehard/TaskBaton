using System.Text.Json;
using TaskBaton.Core;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace TaskBaton.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class McpTests
{
    private string root = null!;
    private RelayService service = null!;
    private static string Dotnet => Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..")),
        OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    private static string Server => Path.Combine(AppContext.BaseDirectory, "taskbaton-mcp.dll");

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.McpTests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        foreach (var id in new[] { "agent-a", "agent-b", "agent-c" })
        {
            var path = Path.Combine(root, id + ".toml"); File.WriteAllText(path, "fixture=true");
            service.Register(id, "fixture", path); service.ValidateAgent(id);
        }
        service.SetPermission("agent-a", "agent-b", "allow");
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private Task<McpClient> Connect(string agent, string protocol = "2025-11-25") => McpClient.CreateAsync(
        new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = Dotnet, Arguments = [Server, "--home", service.Home, "--agent", agent, "--route-id", "fixture-route"],
            ShutdownTimeout = TimeSpan.FromSeconds(5)
        }), new McpClientOptions { ProtocolVersion = protocol, InitializationTimeout = TimeSpan.FromSeconds(15) });
    private static async Task<JsonElement> Call(McpClient client, string name, Dictionary<string, object?> args, bool error = false)
    {
        var result = await client.CallToolAsync(name, args);
        Assert.AreEqual(error, result.IsError == true);
        var json = result.StructuredContent ?? throw new AssertFailedException("Structured result missing.");
        Assert.AreEqual(!error, json.GetProperty("ok").GetBoolean());
        return json;
    }
    private static Task<JsonElement> Dispatch(McpClient client, string to = "agent-b", string key = "one") => Call(client, "relay_task_dispatch",
        new() { ["to"] = to, ["content"] = "请计算 4+9+16+25，并说明结果。", ["idempotency_key"] = key, ["mode"] = "manual" });

    [TestMethod]
    [DataRow("2025-11-25")]
    [DataRow("2026-07-28")]
    public async Task NativeStdioNegotiatesProtocolsAndExposesOnlyTaskTools(string protocol)
    {
        await using var client = await Connect("agent-a", protocol);
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToArray();
        Assert.HasCount(7, names);
        CollectionAssert.Contains(names, "relay_task_dispatch"); CollectionAssert.Contains(names, "relay_task_complete");
        Assert.IsFalse(names.Any(n => n.Contains("migration", StringComparison.Ordinal) || n.Contains("permission", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task TwoBoundMcpProcessesSharePermissionsCallbacksAndContinuation()
    {
        await using var a = await Connect("agent-a"); await using var b = await Connect("agent-b");
        var created = await Dispatch(a); var task = created.GetProperty("data").GetProperty("task");
        var id = task.GetProperty("task_id").GetString()!; var attempt = task.GetProperty("attempt_id").GetString()!;
        var resultPath = task.GetProperty("result_path").GetString()!;
        File.WriteAllText(resultPath, "## 结果\n54");
        var callback = new Dictionary<string, object?> { ["task_id"] = id, ["attempt_id"] = attempt, ["idempotency_key"] = "done", ["result_path"] = resultPath };
        var denied = await Call(a, "relay_task_complete", callback, true);
        Assert.AreEqual("callback_denied", denied.GetProperty("error").GetProperty("code").GetString());
        await Call(b, "relay_task_ack", new() { ["task_id"] = id, ["attempt_id"] = attempt, ["idempotency_key"] = "ack" });
        await Call(b, "relay_task_start", new() { ["task_id"] = id, ["attempt_id"] = attempt, ["idempotency_key"] = "start" });
        var completed = await Call(b, "relay_task_complete", callback);
        Assert.AreEqual("succeeded", completed.GetProperty("state").GetString());
        Assert.AreEqual("manual_required", service.Status(id).Task.ResumeStatus);
        var prompt = await Call(a, "relay_prompt_show", new() { ["task_id"] = id });
        StringAssert.Contains(prompt.GetProperty("data").GetProperty("text").GetString()!, service.Status(id).Task.ResultPath);
        var reverse = await Call(b, "relay_task_dispatch", new() { ["to"] = "agent-a", ["content"] = "reverse", ["idempotency_key"] = "reverse" });
        Assert.AreEqual("awaiting_manual", reverse.GetProperty("data").GetProperty("task").GetProperty("state").GetString());
    }

    [TestMethod]
    public async Task Repeated124McpDispatchesAndCallbacksDoNotRepeatBusinessWork()
    {
        await using var a = await Connect("agent-a"); await using var b = await Connect("agent-b");
        var created = await Dispatch(a); var id = created.GetProperty("task_id").GetString()!;
        var task = service.Status(id).Task; File.WriteAllText(task.ResultPath, "## 结果\n54");
        for (var i = 0; i < 124; i++)
        {
            await Dispatch(a);
            await Call(b, "relay_task_complete", new() { ["task_id"] = id, ["attempt_id"] = task.AttemptId, ["idempotency_key"] = "done", ["result_path"] = task.ResultPath });
            await Call(a, "relay_task_status", new() { ["task_id"] = id });
        }
        Assert.HasCount(1, service.Tasks()); Assert.HasCount(1, service.Continuations(id));
        Assert.AreEqual(1, service.Audit(id).Count(e => e.Event == "task.created"));
        Assert.AreEqual(1, service.Audit(id).Count(e => e.Event == "task.completed"));
        Assert.AreEqual(1, service.Audit(id).Count(e => e.Event == "session.resume_claimed"));
    }

    [TestMethod]
    public async Task ThirdAgentCannotReadAnotherTask()
    {
        await using var a = await Connect("agent-a"); await using var c = await Connect("agent-c");
        var id = (await Dispatch(a)).GetProperty("task_id").GetString()!;
        var denied = await Call(c, "relay_task_status", new() { ["task_id"] = id }, true);
        Assert.AreEqual("task_access_denied", denied.GetProperty("error").GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task MissingExecutorAndInvalidTimeoutAreRejectedBeforeDispatch()
    {
        await using var a = await Connect("agent-a");
        var arguments = new Dictionary<string, object?> { ["to"] = "agent-b", ["content"] = "work", ["idempotency_key"] = "execute", ["execute"] = true };
        var denied = await Call(a, "relay_task_dispatch", arguments, true);
        Assert.AreEqual("executor_unverified", denied.GetProperty("error").GetProperty("code").GetString());
        arguments["timeout_seconds"] = 0;
        await Call(a, "relay_task_dispatch", arguments, true);
        Assert.HasCount(0, service.Tasks());
    }

    [TestMethod]
    public async Task ExplicitBusinessFailureIsAnAcceptedCallback()
    {
        await using var a = await Connect("agent-a"); await using var b = await Connect("agent-b");
        var id = (await Dispatch(a)).GetProperty("task_id").GetString()!;
        var task = service.Status(id).Task; File.WriteAllText(task.ResultPath, "## 失败\n缺少输入。");
        var failed = await Call(b, "relay_task_fail", new() { ["task_id"] = id, ["attempt_id"] = task.AttemptId, ["idempotency_key"] = "fail",
            ["result_path"] = task.ResultPath, ["error_code"] = "missing_input", ["message"] = "缺少输入" });
        Assert.AreEqual("failed", failed.GetProperty("state").GetString());
        Assert.AreEqual("missing_input", service.Status(id).Task.ErrorCode);
    }

    [TestMethod]
    public async Task StdinEofExitsWithoutProtocolNoise()
    {
        var result = await RelayProcess.RunAsync(Dotnet, [Server, "--home", service.Home, "--agent", "agent-a"], "", TimeSpan.FromSeconds(15));
        Assert.IsFalse(result.TimedOut); Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("", result.Output);
    }
}
