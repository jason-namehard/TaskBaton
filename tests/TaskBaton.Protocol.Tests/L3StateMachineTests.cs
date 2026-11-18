namespace TaskBaton.Protocol.Tests;

[TestClass]
public sealed class L3StateMachineTests
{
    [TestMethod]
    public void Duplicate_receipt_is_idempotent()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        var machine = new HandoffTaskStateMachine(state);
        var done = TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json");

        var first = machine.Apply(done);
        var duplicate = machine.Apply(TestPaths.ReceiptFromFixture("cases/edge/receipt.duplicate.json"));

        Assert.AreEqual(ReceiptApplyKind.Applied, first.Kind);
        Assert.AreEqual(ReceiptApplyKind.Duplicate, duplicate.Kind);
        Assert.AreEqual(0, duplicate.ExitCode);
        Assert.IsTrue(duplicate.HasCode(ProtocolDiagnosticCodes.ReceiptDuplicateIdempotent));
        Assert.AreEqual(1, state.AppliedReceiptCount);
        Assert.AreEqual(done.IdempotencyKey, state.LastReceipt?.IdempotencyKey);
    }

    [TestMethod]
    public void Same_key_with_different_content_is_a_conflict()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        var machine = new HandoffTaskStateMachine(state);
        var done = TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json");
        machine.Apply(done);

        var changedJson = done.ToJsonObject();
        changedJson["result"]!.AsObject()["summary"] = "A different summary.";
        var changed = ReceiptDocument.FromJson(changedJson);
        var conflict = machine.Evaluate(changed);

        Assert.AreEqual(ReceiptApplyKind.IdempotencyConflict, conflict.Kind);
        Assert.AreEqual(5, conflict.ExitCode);
        Assert.IsTrue(conflict.HasCode(ProtocolDiagnosticCodes.ReceiptIdempotencyConflict));
        Assert.AreEqual(1, state.AppliedReceiptCount);
    }

    [TestMethod]
    public void Late_receipt_is_accepted_and_recorded_as_late()
    {
        var handoff = TestPaths.HandoffFromFixture("cases/valid/full.handoff.json");
        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        var machine = new HandoffTaskStateMachine(state);
        var late = TestPaths.ReceiptFromFixture("cases/edge/receipt.late.json");

        var result = machine.Apply(late);

        Assert.AreEqual(ReceiptApplyKind.LateAccepted, result.Kind);
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.ReceiptLateAccepted));
        Assert.AreEqual(1, state.AppliedReceiptCount);
        Assert.AreSame(late, state.LastReceipt);
    }

    [TestMethod]
    public void Old_attempt_is_evidence_only()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var state = HandoffTaskState.Create(
            "task-2026-11-13-001",
            "11111111-1111-4111-8111-111111111111",
            0,
            "writer");
        var machine = new HandoffTaskStateMachine(state);
        var receipt = TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json");

        var result = machine.Apply(receipt);

        Assert.AreEqual(ReceiptApplyKind.OldAttempt, result.Kind);
        Assert.AreEqual(5, result.ExitCode);
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.ReceiptOldAttempt));
        Assert.AreEqual(0, state.AppliedReceiptCount);
        Assert.HasCount(1, state.Evidence);
        Assert.IsNull(state.LastReceipt);
    }

    [TestMethod]
    public void Conflicting_final_claim_is_rejected()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        var machine = new HandoffTaskStateMachine(state);
        machine.Apply(TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json"));

        var failed = TestPaths.ReceiptFromFixture("cases/valid/receipt.failed.json");
        var result = machine.Evaluate(failed);

        Assert.AreEqual(ReceiptApplyKind.ConflictingFinal, result.Kind);
        Assert.AreEqual(5, result.ExitCode);
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.ReceiptConflictingFinal));
        Assert.AreEqual(1, state.AppliedReceiptCount);
    }

    [TestMethod]
    public void Late_receipt_after_cancellation_is_evidence_only()
    {
        var handoff = TestPaths.HandoffFromFixture("cases/valid/full.handoff.json");
        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        state.MarkCancelled();
        var machine = new HandoffTaskStateMachine(state);
        var late = TestPaths.ReceiptFromFixture("cases/edge/receipt.late.json");

        var result = machine.Apply(late);

        Assert.AreEqual(ReceiptApplyKind.Cancelled, result.Kind);
        Assert.AreEqual(5, result.ExitCode);
        Assert.IsTrue(result.HasCode(ProtocolDiagnosticCodes.ReceiptCancelled));
        Assert.AreEqual(0, state.AppliedReceiptCount);
        Assert.HasCount(1, state.Evidence);
    }

    [TestMethod]
    public void Evaluate_does_not_advance_state()
    {
        var handoff = TestPaths.HandoffFromFixture();
        var state = HandoffTaskState.FromHandoff(handoff.Frontmatter);
        var machine = new HandoffTaskStateMachine(state);

        var result = machine.Evaluate(TestPaths.ReceiptFromFixture("cases/valid/receipt.done.json"));

        Assert.AreEqual(ReceiptApplyKind.Applied, result.Kind);
        Assert.IsTrue(result.StateChanged);
        Assert.AreEqual(0, state.AppliedReceiptCount);
        Assert.IsNull(state.LastReceipt);
    }
}
