using TaskBaton.Core;

namespace TaskBaton.Core.Tests;

[TestClass]
public sealed class CodexContractTests
{
    private const string Events = """
        {"type":"thread.started","thread_id":"contract-session"}
        {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":5}}
        """;
    private const string Declaration = """
        {"status":"succeeded","result_markdown":"结果是 54。","error_code":"","message":"verified"}
        """;

    [TestMethod]
    public void StructuredDeclarationAndTurnEvidenceProduceAnOutcome()
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(0, Events, "", false), Declaration);
        Assert.AreEqual("succeeded", result.Status);
        Assert.AreEqual("contract-session", result.SessionId);
        Assert.AreEqual("结果是 54。", result.ResultMarkdown);
    }

    [TestMethod]
    public void ExitZeroWithoutDeclarationDoesNotClaimSuccess()
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(0, Events, "", false), null);
        Assert.AreEqual("failed", result.Status);
        Assert.AreEqual("completion_missing", result.ErrorCode);
    }

    [TestMethod]
    public void DeclarationWithoutSessionAndTurnEvidenceIsRejected()
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(0, "", "", false), Declaration);
        Assert.AreEqual("completion_unverified", result.ErrorCode);
    }

    [TestMethod]
    public void TimeoutLeavesMissingCallbackInsteadOfFabricatingAResult()
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(-1, "", "", true), null);
        Assert.AreEqual("missing_callback", result.Status);
        Assert.AreEqual("", result.ResultMarkdown);
    }

    [TestMethod]
    public void NonzeroExitCannotBeOverriddenByASuccessDeclaration()
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(1, Events, "process failed", false), Declaration);
        Assert.AreEqual("executor_exited", result.ErrorCode);
    }

    [TestMethod]
    public void MalformedCliEventIsReportedAsAContractFailure()
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(0, "not JSONL", "", false), Declaration);
        Assert.AreEqual("invalid_event_stream", result.ErrorCode);
    }

    [TestMethod]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"type\":\"thread.started\"}")]
    public void IncompleteEventFieldsCannotBecomeSessionEvidence(string events)
    {
        var result = CodexExecutor.ParseOutcome(new ProcessResult(0, events, "", false), Declaration);
        Assert.AreEqual("invalid_event_stream", result.ErrorCode);
    }

    [TestMethod]
    public async Task NativeChildStdinAndStdoutPreserveChineseUtf8()
    {
        const string original = "交接任务：计算结果为 54。";
        const string script = "[Console]::InputEncoding=[Text.UTF8Encoding]::new($false);[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);[Console]::Out.Write([Console]::In.ReadToEnd())";
        var result = await RelayProcess.RunAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", script],
            original, TimeSpan.FromSeconds(20));
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(original, result.Output);
    }

    [TestMethod]
    public async Task NativeChildTimeoutIsBounded()
    {
        var result = await RelayProcess.RunAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 30"],
            null, TimeSpan.FromMilliseconds(300));
        Assert.IsTrue(result.TimedOut);
    }
}
