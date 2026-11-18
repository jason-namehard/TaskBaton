using TaskBaton.Core;
using System.Text;

namespace TaskBaton.Core.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MigrationTests
{
    private string root = null!;
    private string target = null!;
    private RelayService service = null!;
    private static readonly AdapterSource Source = new("relay-mcp", "0.4.0", "https://github.com/jason-namehard/TaskBaton",
        "phase4", "MIT", ["stdio_mcp", "completion_callback"], [".NET 10"], ["Local stdio only"]);
    [TestInitialize]
    public void Setup()
    {
        root = Path.Combine(Path.GetTempPath(), "TaskBaton.MigrationTests", Guid.NewGuid().ToString());
        service = new RelayService(Path.Combine(root, "relay")); service.Initialize();
        target = Path.Combine(root, "config.json");
        File.WriteAllText(target, "{\r\n  \"old\": true,\r\n  \"unrelated\": [1, 2, 3]\r\n}\r\n");
    }
    [TestCleanup]
    public void Cleanup() => Directory.Delete(root, true);
    private MigrationRequest Request() => new(Source, target, "json", "\"old\": true", "\"old\": false");

    [TestMethod]
    public void PreviewDoesNotWriteTargetAndApprovalIsRequired()
    {
        var original = File.ReadAllBytes(target); var plan = service.PlanMigration(Request());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        StringAssert.Contains(File.ReadAllText(plan.PreviewPath), "\"old\": false");
        Assert.IsFalse(File.Exists(plan.BackupPath));
        Assert.AreEqual("approval_required", Assert.ThrowsExactly<RelayException>(() => service.ApplyMigration(plan.MigrationId, false, "")).Code);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        Assert.HasCount(1, service.AdapterSources());
    }

    [TestMethod]
    public void ApplyAndRollbackPreserveBomNewlinesAndUnrelatedBytes()
    {
        var text = File.ReadAllText(target); var original = new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        File.WriteAllBytes(target, original); var plan = service.PlanMigration(Request());
        service.ApplyMigration(plan.MigrationId, true, "reviewed exact fragment");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(plan.BackupPath));
        CollectionAssert.AreEqual(new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes(text.Replace("\"old\": true", "\"old\": false", StringComparison.Ordinal))).ToArray(), File.ReadAllBytes(target));
        Assert.AreEqual("applied", service.ApplyMigration(plan.MigrationId, true, "duplicate approval").State);
        service.RollbackMigration(plan.MigrationId, true, "restore approved change");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        Assert.AreEqual("rolled_back", service.Migration(plan.MigrationId).State);
    }

    [TestMethod]
    public void LaterUserEditBlocksApplyAndRollback()
    {
        var original = File.ReadAllText(target); var plan = service.PlanMigration(Request());
        File.AppendAllText(target, " ");
        Assert.AreEqual("migration_target_changed", Assert.ThrowsExactly<RelayException>(() => service.ApplyMigration(plan.MigrationId, true, "approve")).Code);
        Assert.AreEqual(original + " ", File.ReadAllText(target));
        File.WriteAllText(target, original); service.ApplyMigration(plan.MigrationId, true, "approve unchanged baseline");
        File.AppendAllText(target, " "); var edited = File.ReadAllBytes(target);
        Assert.AreEqual("migration_target_changed", Assert.ThrowsExactly<RelayException>(() => service.RollbackMigration(plan.MigrationId, true, "rollback")).Code);
        CollectionAssert.AreEqual(edited, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void NewFileCanBeApprovedAndRemovedByRollback()
    {
        File.Delete(target);
        var plan = service.PlanMigration(Request() with { Before = "", After = "{\"mcpServers\":{}}" });
        Assert.IsFalse(File.Exists(target)); service.ApplyMigration(plan.MigrationId, true, "new config approved");
        Assert.IsTrue(File.Exists(target)); service.RollbackMigration(plan.MigrationId, true, "remove new config");
        Assert.IsFalse(File.Exists(target)); Assert.IsTrue(File.Exists(plan.BackupPath));
    }

    [TestMethod]
    public void InvalidSyntaxAmbiguousFragmentsAndSourceReuseAreRejected()
    {
        Assert.AreEqual("configuration_invalid", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(Request() with { After = "broken" })).Code);
        Assert.AreEqual("migration_fragment_ambiguous", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(Request() with { Before = "x" })).Code);
        Assert.AreEqual("migration_fragment_ambiguous", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(Request() with { Before = " " })).Code);
        service.PlanMigration(Request());
        Assert.AreEqual("adapter_source_conflict", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(Request() with { Source = Source with { Revision = "different" } })).Code);
    }

    [TestMethod]
    public void JsonAndTomlInventoryReportsCommandsWithoutEnvironmentValues()
    {
        File.WriteAllText(target, """{"mcpServers":{"relay":{"command":"dotnet","args":["relay-mcp.dll"],"env":{"TOKEN":"private"}}}}""");
        var json = service.InspectConfiguration(target, "json");
        Assert.AreEqual("dotnet", json.McpEntries.Single().Command);
        Assert.IsFalse(RelayJson.Write(json).Contains("private", StringComparison.Ordinal));
        var toml = Path.Combine(root, "config.toml");
        File.WriteAllText(toml, "# Keep comment\r\nmodel = 'example'\r\n[mcp_servers.relay]\r\ncommand = 'dotnet'\r\nargs = ['relay-mcp.dll']\r\n");
        Assert.AreEqual("dotnet", service.InspectConfiguration(toml, "toml").McpEntries.Single().Command);
        var plan = service.PlanMigration(Request() with { TargetPath = toml, Format = "toml", Before = "", After = "\r\n[mcp_servers.other]\r\nurl = 'https://example.com/mcp'\r\n" });
        var before = File.ReadAllText(toml); service.ApplyMigration(plan.MigrationId, true, "append approved entry");
        StringAssert.StartsWith(File.ReadAllText(toml), before);
        Assert.HasCount(2, service.InspectConfiguration(toml, "toml").McpEntries);
    }

    [TestMethod]
    public void InstallCannotSilentlyReplaceRemoveOrDisableExistingMcpEntry()
    {
        var path = Path.Combine(root, "config.toml");
        const string original = "[mcp_servers.legacy]\ncommand = 'old'\nenabled = true\n";
        File.WriteAllText(path, original);
        foreach (var after in new[] { "# removed\n", original.Replace("'old'", "'new'", StringComparison.Ordinal), original.Replace("true", "false", StringComparison.Ordinal) })
            Assert.AreEqual("migration_disables_old_entry", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(
                Request() with { TargetPath = path, Format = "toml", Before = original, After = after })).Code);
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public async Task OldEntryRequiresValidatedNewRouteAndSeparateApproval()
    {
        var plan = service.PlanMigration(Request()); service.ApplyMigration(plan.MigrationId, true, "new route");
        var old = Path.Combine(root, "old.json"); File.WriteAllText(old, "{\"enabled\":true}");
        var disable = Request() with { TargetPath = old, Before = "true", After = "false", Purpose = "disable_old", ValidatedMigrationId = plan.MigrationId };
        Assert.AreEqual("migration_validation_required", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(disable)).Code);
        var task = await WorkflowProof(plan.MigrationId);
        service.ValidateMigration(plan.MigrationId, task.TaskId);
        var oldPlan = service.PlanMigration(disable);
        Assert.AreEqual("{\"enabled\":true}", File.ReadAllText(old));
        Assert.ThrowsExactly<RelayException>(() => service.ApplyMigration(oldPlan.MigrationId, false, ""));
        service.ApplyMigration(oldPlan.MigrationId, true, "new route verified; disable reviewed old entry");
        Assert.AreEqual("{\"enabled\":false}", File.ReadAllText(old));
        Assert.AreEqual("migration_rollback_order", Assert.ThrowsExactly<RelayException>(() => service.RollbackMigration(plan.MigrationId, true, "restore new route")).Code);
        service.RollbackMigration(oldPlan.MigrationId, true, "restore old entry");
        Assert.AreEqual("{\"enabled\":true}", File.ReadAllText(old));
    }

    [TestMethod]
    public async Task ProofFromAnotherRouteCannotValidateMigration()
    {
        var plan = service.PlanMigration(Request()); service.ApplyMigration(plan.MigrationId, true, "approve");
        var task = await WorkflowProof("different-route");
        Assert.AreEqual("migration_proof_incomplete", Assert.ThrowsExactly<RelayException>(() => service.ValidateMigration(plan.MigrationId, task.TaskId)).Code);
    }

    [TestMethod]
    public async Task DisableOldProposalCannotRemoveTheVerifiedNewFragment()
    {
        var plan = service.PlanMigration(Request()); service.ApplyMigration(plan.MigrationId, true, "approve");
        var task = await WorkflowProof(plan.MigrationId); service.ValidateMigration(plan.MigrationId, task.TaskId);
        Assert.AreEqual("migration_target_changed", Assert.ThrowsExactly<RelayException>(() => service.PlanMigration(
            Request() with { Before = "\"old\": false", After = "\"old\": true", Purpose = "disable_old", ValidatedMigrationId = plan.MigrationId })).Code);
    }

    [TestMethod]
    public async Task MultipleOldEntriesInSameFileCanBeDisabledAndRolledBackInOrder()
    {
        const string original = "{\"route\":false,\"old1\":true,\"old2\":true}";
        File.WriteAllText(target, original);
        var plan = service.PlanMigration(Request() with { Before = "\"route\":false", After = "\"route\":true" });
        service.ApplyMigration(plan.MigrationId, true, "install route");
        var task = await WorkflowProof(plan.MigrationId); service.ValidateMigration(plan.MigrationId, task.TaskId);
        var first = service.PlanMigration(Request() with { Before = "\"old1\":true", After = "\"old1\":false", Purpose = "disable_old", ValidatedMigrationId = plan.MigrationId });
        service.ApplyMigration(first.MigrationId, true, "disable first old route");
        var second = service.PlanMigration(Request() with { Before = "\"old2\":true", After = "\"old2\":false", Purpose = "disable_old", ValidatedMigrationId = plan.MigrationId });
        service.ApplyMigration(second.MigrationId, true, "disable second old route");
        service.RollbackMigration(second.MigrationId, true, "restore second");
        service.RollbackMigration(first.MigrationId, true, "restore first");
        service.RollbackMigration(plan.MigrationId, true, "restore baseline");
        Assert.AreEqual(original, File.ReadAllText(target));
    }

    [TestMethod]
    public async Task ProofFromOldAttemptDoesNotValidateDirectCliRetry()
    {
        var plan = service.PlanMigration(Request()); service.ApplyMigration(plan.MigrationId, true, "approve");
        var old = await WorkflowProof(plan.MigrationId, failed: true);
        var task = (await service.RetryAsync(old.TaskId, "approve fresh attempt", true)).Task;
        File.WriteAllText(task.ResultPath, "## Result\n54"); service.Complete(task.TaskId, task.AttemptId, "agent-b", "new-final", task.ResultPath);
        await service.ProcessContinuationsAsync(task.TaskId, resumer: new Submitted());
        Assert.AreEqual("migration_proof_incomplete", Assert.ThrowsExactly<RelayException>(() => service.ValidateMigration(plan.MigrationId, task.TaskId)).Code);
    }

    [TestMethod]
    public void InterruptedWriteRecoveryOnlyClassifiesTargetWithoutOverwriting()
    {
        var plan = service.PlanMigration(Request());
        File.Copy(plan.OriginalPath, plan.BackupPath); SetState(plan, "applying");
        Assert.AreEqual("planned", service.RecoverMigration(plan.MigrationId).State);
        service.ApplyMigration(plan.MigrationId, true, "reapprove after known no-write");
        SetState(plan, "applying"); Assert.AreEqual("applied", service.RecoverMigration(plan.MigrationId).State);
        File.WriteAllText(target, "{\"unrelated\":42}"); SetState(plan, "rolling_back");
        Assert.AreEqual("unknown", service.RecoverMigration(plan.MigrationId).State);
        Assert.AreEqual("{\"unrelated\":42}", File.ReadAllText(target));
    }

    private void SetState(MigrationPlan plan, string state) => new RelayStore(service.Home).Write(s => s.Execute(
        "UPDATE migrations SET json=$json WHERE id=$id", ("$json", RelayJson.Write(plan with { State = state })), ("$id", plan.MigrationId)));
    private async Task<TaskRecord> WorkflowProof(string route, bool failed = false)
    {
        foreach (var id in new[] { "agent-a", "agent-b" })
        {
            var path = Path.Combine(root, id + ".toml"); File.WriteAllText(path, "fixture=true");
            service.Register(id, "fixture", path); service.ValidateAgent(id);
        }
        service.SetPermission("agent-a", "agent-b", "allow");
        var task = (await service.DispatchAsync(new DispatchRequest("agent-a", "agent-b", "work", "manual", "manual", "proof",
            OriginSession: new OriginSession("fixture", Guid.NewGuid().ToString(), "fixture", root)))).Task;
        service.RecordMcpOperation(task.TaskId, "agent-a", "dispatch", route);
        File.WriteAllText(task.ResultPath, "## Result\n54"); service.Complete(task.TaskId, task.AttemptId, "agent-b", "done", task.ResultPath,
            failed, failed ? "fixture_failure" : null, failed ? "fixture failure" : null);
        await service.ProcessContinuationsAsync(task.TaskId, resumer: new Submitted()); return task;
    }
    private sealed class Submitted : ISessionResumer
    {
        public Task<ResumeResult> ResumeAsync(ResumeInput input, CancellationToken cancellationToken) => Task.FromResult(new ResumeResult("submitted"));
    }
}
