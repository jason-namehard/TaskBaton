using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CliTests
{
    [TestMethod]
    public async Task UnknownOptionReturnsJsonErrorAndDoesNotCreateDataDirectory()
    {
        var home = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        var result = await Invoke("--home", home, "task", "status", "--task", "missing", "--typo", "x");
        Assert.AreEqual(2, result.Code);
        Assert.AreEqual("invalid_arguments", result.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(Directory.Exists(home));
    }

    [TestMethod]
    public async Task StatusBeforeInitializationIsReadOnly()
    {
        var home = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        var result = await Invoke("task", "status", "--task", "missing", "--home", home);
        Assert.AreEqual(3, result.Code);
        Assert.AreEqual("not_initialized", result.Json.GetProperty("error").GetProperty("code").GetString());
        Assert.IsFalse(Directory.Exists(home));
    }

    [TestMethod]
    public async Task ArtifactInventoryAndCleanupPreviewDistinguishLiveCopiesFromOriginals()
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        var service = new RelayService(Path.Combine(root, "relay"));
        service.Initialize();
        try
        {
            service.RegisterManual("a", "fixture", "A");
            service.RegisterManual("b", "fixture", "B");
            var original = Path.Combine(root, "input.md");
            File.WriteAllText(original, "Review this file.");
            var task = service.CreateFileHandoff(new("a", "b", original)).Task;
            var inventory = await Invoke("--home", service.Home, "artifact", "inventory");
            Assert.AreEqual(0, inventory.Code);
            var entries = inventory.Json.GetProperty("data").EnumerateArray().ToArray();
            Assert.AreEqual("user", entries.Single(file => file.GetProperty("path").GetString() == original)
                .GetProperty("owner").GetString());
            var copy = entries.Single(file => file.GetProperty("path").GetString() == task.RequestPath);
            Assert.AreEqual("relay", copy.GetProperty("owner").GetString());
            Assert.AreEqual("in_use", copy.GetProperty("status").GetString());
            Assert.IsTrue(copy.GetProperty("references").EnumerateArray().Any(reference =>
                reference.GetProperty("task_id").GetString() == task.TaskId && reference.GetProperty("active").GetBoolean()));
            var preview = await Invoke("--home", service.Home, "cleanup", "--dry-run");
            Assert.AreEqual(0, preview.Code);
            Assert.AreEqual(0, preview.Json.GetProperty("data").GetProperty("eligible").GetInt32());
            Assert.IsTrue(File.Exists(original) && File.Exists(task.RequestPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ManualCliWorkflowReturnsActionRequiredThenAcceptsFinalCallback(bool failure)
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        var home = Path.Combine(root, "data");
        await Invoke("--home", home, "init");
        foreach (var id in new[] { "agent-a", "agent-b" })
        {
            var path = Path.Combine(root, id + ".json");
            File.WriteAllText(path, "{}");
            Assert.AreEqual(0, (await Invoke("--home", home, "agent", "register", "--id", id, "--config-path", path)).Code);
            Assert.AreEqual(0, (await Invoke("--home", home, "agent", "validate", "--id", id)).Code);
        }
        await Invoke("--home", home, "permission", "set", "--from", "agent-a", "--to", "agent-b", "--decision", "allow");
        var request = Path.Combine(root, "request.md"); File.WriteAllText(request, "Review code");
        var dispatched = await Invoke("--home", home, "task", "dispatch", "--from", "agent-a", "--to", "agent-b",
            "--request", request, "--mode", "manual", "--idempotency-key", "cli-task");
        Assert.AreEqual(4, dispatched.Code);
        Assert.IsTrue(dispatched.Json.GetProperty("ok").GetBoolean());
        var task = dispatched.Json.GetProperty("data").GetProperty("task");
        var resultPath = task.GetProperty("result_path").GetString()!;
        File.WriteAllText(resultPath, "Result ready");
        var completed = await Invoke("--home", home, "task", failure ? "fail" : "complete", "--task", task.GetProperty("task_id").GetString()!,
            "--attempt", task.GetProperty("attempt_id").GetString()!, "--sender", "agent-b", "--idempotency-key", "done",
            "--status", failure ? "failed" : "succeeded", "--result", resultPath, "--error-code", "fixture", "--message", "Fixture result");
        Assert.AreEqual(0, completed.Code);
        Assert.AreEqual(failure ? "failed" : "succeeded", completed.Json.GetProperty("state").GetString());
        Assert.IsTrue(completed.Json.GetProperty("ok").GetBoolean());
        Directory.Delete(root, recursive: true);
    }

    private static async Task<(int Code, JsonElement Json)> Invoke(params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var code = await RelayCli.RunAsync(args);
            using var json = JsonDocument.Parse(output.ToString());
            return (code, json.RootElement.Clone());
        }
        finally { Console.SetOut(original); }
    }

    [TestMethod]
    public async Task InvalidExecutionTimeoutIsRejectedBeforeCreatingATask()
    {
        var root = Path.Combine(Path.GetTempPath(), "AgentRelay.Tests", Guid.NewGuid().ToString());
        await Invoke("--home", root, "init");
        var request = Path.Combine(root, "request.md"); File.WriteAllText(request, "Do work");
        var result = await Invoke("--home", root, "task", "dispatch", "--from", "agent-a", "--to", "agent-b", "--request", request,
            "--idempotency-key", "invalid-timeout", "--execute", "--execution-timeout-seconds", "0");
        Assert.AreEqual(2, result.Code);
        Assert.AreEqual("invalid_execution_timeout", result.Json.GetProperty("error").GetProperty("code").GetString());
        var tasks = await Invoke("--home", root, "task", "list");
        Assert.AreEqual(0, tasks.Json.GetProperty("data").GetArrayLength());
        Directory.Delete(root, recursive: true);
    }
}
