# TaskBaton protocol package

The normative document is [`HANDOFF.md`](HANDOFF.md), with schemas in
[`schemas/`](schemas) and executable fixtures in
[`conformance/`](conformance).

## CLI integration

The CLI should call the verifier and map its exit code directly; it should not
duplicate semantic checks:

```csharp
var result = new HandoffVerifier().Verify(handoffPath, receiptPath, taskContext);
foreach (var diagnostic in result.Diagnostics)
    Console.Error.WriteLine($"{diagnostic.Code} {diagnostic.Field}: {diagnostic.Message}");
return result.ExitCode;
```

`Verify` accepts `(handoffFile, receiptFile?, taskContext?)`. The optional
`HandoffTaskContext` carries the current `HandoffTaskState`, a `SessionStore`, or
deadline/cancellation overrides. Exit codes are `0` pass, `2` argument/format,
`3` validation, and `5` id/state conflict. Informational diagnostics such as
`receipt.duplicate_idempotent` and `receipt.late_accepted` do not change a `0`.

L4/L5 callers can use `SessionBindingVerifier` with `ISessionStore` /
`ISessionResumer`, and `ArtifactCleanupPlanner` with `IArtifactOwnershipStore`.
The verifier is policy-only: it never writes receipts, resumes sessions, or
deletes files.
