using System.Text.Json.Nodes;

namespace AgentRelay.Protocol.Tests;

[TestClass]
public sealed class SemanticValidationTests
{
    private static HandoffVerifier Verifier => new(TestPaths.SchemaSet);

    [TestMethod]
    public void Missing_handoff_file_is_an_argument_error()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "HANDOFF.md");

        var result = Verifier.Verify(missing);

        Assert.AreEqual(2, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.FileNotFound), Describe(result));
    }

    [TestMethod]
    public void Malformed_frontmatter_is_an_argument_error()
    {
        using var temp = new TempDirectory();
        var path = temp.File("HANDOFF.md");
        File.WriteAllText(path, "This is not a handoff document.");

        var result = Verifier.Verify(path);

        Assert.AreEqual(2, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.InvalidFrontmatter), Describe(result));
    }

    [TestMethod]
    public void Handoff_rejects_equal_from_and_to()
    {
        using var temp = new TempDirectory();
        var json = TestPaths.FrontmatterJson();
        json["from"] = "writer";
        var path = temp.File("HANDOFF.md");
        HandoffWriter.WriteFile(path, TestPaths.HandoffFromJson(json));

        var result = Verifier.Verify(path);

        Assert.AreEqual(3, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffFromEqualsTo), Describe(result));
    }

    [TestMethod]
    public void Handoff_requires_utc_z_timestamps()
    {
        using var temp = new TempDirectory();
        var json = TestPaths.FrontmatterJson();
        json["created_at"] = "2026-11-13T02:00:00+00:00";
        var path = temp.File("HANDOFF.md");
        HandoffWriter.WriteFile(path, TestPaths.HandoffFromJson(json));

        var result = Verifier.Verify(path);

        Assert.AreEqual(3, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffTimestampNotUtc), Describe(result));
    }

    [TestMethod]
    public void Handoff_rejects_parent_directory_paths()
    {
        using var temp = new TempDirectory();
        var json = TestPaths.FrontmatterJson();
        json["receipt_path"] = "../receipt.json";
        var path = temp.File("HANDOFF.md");
        HandoffWriter.WriteFile(path, TestPaths.HandoffFromJson(json));

        var result = Verifier.Verify(path);

        Assert.AreEqual(3, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.HandoffPathUnsafe), Describe(result));
    }

    [TestMethod]
    public void Handoff_requires_all_four_body_sections()
    {
        using var temp = new TempDirectory();
        var path = temp.File("HANDOFF.md");
        HandoffWriter.WriteFile(path, TestPaths.HandoffFromJson(TestPaths.FrontmatterJson(), includeContext: false));

        var result = Verifier.Verify(path);

        Assert.AreEqual(3, result.ExitCode, Describe(result));
        Assert.IsTrue(
            result.Diagnostics.Any(d => d.Code == ProtocolDiagnosticCodes.HandoffSectionRequired
                                        && d.Field == "body.Context"),
            Describe(result));
    }

    [TestMethod]
    public void Receipt_id_mismatch_is_a_state_conflict()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var json = TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json").ToJsonObject();
        json["task_id"] = "task-other";
        var receipt = ReceiptDocument.FromJson(json);

        var result = Verifier.Verify(handoff, receipt);

        Assert.AreEqual(5, result.ExitCode, Describe(result));
        Assert.IsTrue(
            result.Diagnostics.Any(d => d.Code == ProtocolDiagnosticCodes.ReceiptIdMismatch
                                        && d.Field == "task_id"),
            Describe(result));
    }

    [TestMethod]
    public void Reviewer_final_approval_is_valid_and_terminal()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var json = TestPaths.ReceiptFromFixture("cases/valid/receipt.approved.json").ToJsonObject();
        json["needs_reply"] = false;
        json.Remove("next_action");
        json["review"] = new JsonObject
        {
            ["decision"] = "reviewer_final",
            ["final"] = true,
            ["reviewer"] = "reviewer",
        };
        var receipt = ReceiptDocument.FromJson(json);

        var result = Verifier.Verify(handoff, receipt);
        Assert.AreEqual(0, result.ExitCode, Describe(result));
        Assert.IsTrue(receipt.IsReviewerFinalApproval);

        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        var applied = new HandoffTaskStateMachine(state).Apply(receipt);
        Assert.AreEqual(ReceiptApplyKind.Applied, applied.Kind);
        Assert.IsTrue(state.IsTerminal);
    }

    [TestMethod]
    public void Reviewer_final_approval_requires_a_result()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var json = TestPaths.ReceiptFromFixture("cases/valid/receipt.approved.json").ToJsonObject();
        json["needs_reply"] = false;
        json.Remove("next_action");
        json.Remove("result");
        json["review"] = new JsonObject { ["decision"] = "reviewer_final", ["final"] = true };
        var receipt = ReceiptDocument.FromJson(json);

        var result = Verifier.Verify(handoff, receipt);

        Assert.AreEqual(3, result.ExitCode, Describe(result));
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.ReceiptResultRequired), Describe(result));
    }

    [TestMethod]
    public void Approved_without_review_final_remains_non_terminal()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var receipt = TestPaths.ReceiptFromFixture("cases/valid/receipt.approved.json");

        var result = Verifier.Verify(handoff, receipt);
        Assert.AreEqual(0, result.ExitCode, Describe(result));

        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        new HandoffTaskStateMachine(state).Apply(receipt);
        Assert.IsFalse(state.IsTerminal);
    }

    [TestMethod]
    public void Legacy_author_accept_remains_terminal()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var receipt = TestPaths.ReceiptFromFixture("cases/valid/receipt.accepted.json");

        var result = Verifier.Verify(handoff, receipt);
        Assert.AreEqual(0, result.ExitCode, Describe(result));

        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        new HandoffTaskStateMachine(state).Apply(receipt);
        Assert.IsTrue(state.IsTerminal);
    }

    [TestMethod]
    public void Reader_and_writer_round_trip_frontmatter_and_body()
    {
        var original = TestPaths.HandoffFromFixture("cases/valid/full.handoff.json");
        var text = HandoffWriter.Write(original);
        var reread = HandoffDocument.Parse(text);

        Assert.AreEqual(original.Frontmatter.TaskId, reread.Frontmatter.TaskId);
        Assert.AreEqual(original.Frontmatter.Session?.SessionId, reread.Frontmatter.Session?.SessionId);
        Assert.IsNotNull(reread.Frontmatter.Artifacts);
        Assert.HasCount(2, reread.Frontmatter.Artifacts);
        Assert.IsTrue(reread.HasSection("Context"));
        StringAssert.Contains(text, "created_at: \"2026-11-13T02:00:00Z\"");
    }

    [TestMethod]
    public void Writer_preserves_unknown_frontmatter_keys()
    {
        var json = TestPaths.FrontmatterJson();
        json["x_custom"] = "kept";
        var text = HandoffWriter.Write(TestPaths.HandoffFromJson(json));
        var reread = HandoffDocument.Parse(text);

        Assert.AreEqual("kept", reread.Frontmatter.Extra["x_custom"]?.GetValue<string>());
    }

    private static string Describe(VerificationResult result) =>
        $"exit={result.ExitCode}; " + string.Join(
            " | ",
            result.Diagnostics.Select(d => $"{d.Code}@{d.Field}: {d.Message}"));
}
