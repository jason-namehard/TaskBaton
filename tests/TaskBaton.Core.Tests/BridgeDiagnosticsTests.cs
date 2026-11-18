using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class BridgeDiagnosticsTests
{
    private string root = null!;
    private RelayService service = null!;

    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.Tests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        service.RegisterManual("agent-a", "fixture", "发送方");
        service.RegisterManual("agent-b", "fixture", "接收方");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);

    private CliExtensionRecord Import()
    {
        var command = new CliCommand("not-launched", ["--secret", "argument-private-value", "{prompt}"],
            "input-private-value", root, new() { ["PRIVATE_TOKEN"] = "environment-private-value" });
        return service.ImportCliRecipe("agent-b", RelayJson.Write(new CliRecipe(1, "fixture", "fixture", command,
            null, [], "", "", "", "", "text")));
    }

    private void Save(CliExtensionRecord extension) => new RelayStore(service.Home).Write(s =>
        s.Execute("UPDATE cli_extensions SET json=$json WHERE agent_id=$id", ("$json", RelayJson.Write(extension)), ("$id", extension.AgentId)));

    [TestMethod]
    public void RepairPromptTargetsCurrentNodeAndNeverCopiesPrivateRecipeOrTranscriptValues()
    {
        var extension = Import();
        Save(extension with
        {
            Block = new("auth_required", "private-error-value", DateTimeOffset.Parse("2026-10-28T00:00:00Z")),
            CreateProbe = new("probe", "create", "private-probe-marker", null, false, false, "private-probe-error", DateTimeOffset.UtcNow,
                "private-transcript-value", false)
        });
        var file = Path.Combine(root, "work.md"); File.WriteAllText(file, "private-task-body");
        var task = service.CreateFileHandoff(new("agent-a", "agent-b", file, "private-task-message")).Task;
        var prompt = service.BridgeDiagnosticPrompt("agent-b", "& 'E:\\Relay App\\relay.exe'");
        StringAssert.Contains(prompt, "& 'E:\\Relay App\\relay.exe' --home");
        StringAssert.Contains(prompt, "cli-recipe doctor --agent 'agent-b'");
        StringAssert.Contains(prompt, "cli-recipe import --agent 'agent-b'");
        StringAssert.Contains(prompt, "cli-recipe unblock --agent 'agent-b'");
        StringAssert.Contains(prompt, task.TaskId);
        StringAssert.Contains(prompt, "auth_required");
        StringAssert.Contains(prompt, "PRIVATE_TOKEN");
        StringAssert.Contains(prompt, typeof(RelayService).Assembly.Location);
        StringAssert.Contains(prompt, service.Home);
        foreach (var secret in new[] { "argument-private-value", "input-private-value", "environment-private-value", "private-error-value",
            "private-probe-marker", "private-probe-error", "private-transcript-value", "private-task-body", "private-task-message" })
            Assert.DoesNotContain(secret, prompt, StringComparison.Ordinal);
        Assert.AreEqual("auth_required", service.CliExtension("agent-b")!.Block!.Kind);
        Assert.AreEqual(TaskState.AwaitingManual, service.Status(task.TaskId).Task.State);
    }

    [TestMethod]
    public void EveryCopyReadsFreshBlockAndDoesNotCreateDiagnosticFilesOrModifyEvidence()
    {
        var extension = Import();
        Save(extension with { Block = new("auth_required", "not printed", DateTimeOffset.UtcNow) });
        // SQLite manages its WAL/shm sidecars when connections open; these are not generated diagnostic artifacts.
        string[] Files()=>Directory.GetFiles(service.Home,"*",SearchOption.AllDirectories)
            .Where(path=>path!=Path.Combine(service.Home,"taskbaton.db-wal")&&path!=Path.Combine(service.Home,"taskbaton.db-shm")).Order().ToArray();
        var files = Files();
        StringAssert.Contains(service.BridgeDiagnosticPrompt("agent-b"), "\"block_kind\": \"auth_required\"");
        service.ClearCliBlock("agent-b");
        StringAssert.Contains(service.BridgeDiagnosticPrompt("agent-b"), "\"block_kind\": null");
        Assert.IsFalse(service.CliExtension("agent-b")!.CanCreate);
        CollectionAssert.AreEqual(files, Files());
    }

    [TestMethod]
    public void MissingCliIsExplicitAndUnknownNodeIsRejected()
    {
        var prompt = service.BridgeDiagnosticPrompt("agent-b", null);
        StringAssert.Contains(prompt, "没有找到可确认的 Relay CLI");
        Assert.DoesNotContain("cli-recipe import --agent", prompt, StringComparison.Ordinal);
        StringAssert.Contains(prompt, "本次请求不授权任何真实模型调用");
        StringAssert.Contains(prompt, "不直接 UPDATE 账本");
        Assert.ThrowsExactly<RelayException>(() => service.BridgeDiagnosticPrompt("missing"));
    }
}
