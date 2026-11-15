# Conformance fixtures — handoff/1

These fixtures are the executable part of [`../HANDOFF.md`](../HANDOFF.md).
They are intentionally implementation-neutral: any relay, CLI, or desktop app can
run them without adopting TaskBaton.

## Layout

```
conformance/
├─ manifest.json          # case id → file, kind, level, expected result
└─ cases/
   ├─ valid/              # must validate
   ├─ invalid/            # must be rejected, with the listed error code
   └─ edge/               # valid shape, but semantic behavior is required
```

Each `cases/**/*.json` file is either a handoff frontmatter object or a receipt
object. The body of `HANDOFF.md` is Markdown and is not represented in these
fixtures; body-section checks are listed as L2 below.

## Levels

| Level | Check | How to test |
|---|---|---|
| **L1** | JSON Schema validity | Validate each case against `schemas/handoff.schema.json` or `schemas/receipt.schema.json`. |
| **L2** | Semantic validity | IDs, attempt, turn, sender match; `done` + `needs_reply=true` has `result` + `next_action`; `changes_requested` has non-empty `open_items`; `accepted` has `deliverable`; `failed` has `error`. |
| **L3** | Duplicate / conflicting / late / old attempt | Same `(task_id, attempt_id, turn, idempotency_key)` + same content → one transition; conflicting final receipts → reject; late valid receipt → accept as late; older attempt → evidence only. |
| **L4** | Session binding | `session_id` must be exact; `latest`/`recent`/`*` rejected; resume must verify the id and reject a different id in the response. |
| **L5** | Cleanup ownership | Only files created/registered by the relay, with no active reference, and inside the managed root may be cleaned. User sources and deliverables are never deleted by TTL. |

L1 is directly covered by the fixtures. L2–L5 need a small in-memory state
machine in the implementation's test project.

## Running

Once the protocol test project exists:

```powershell
dotnet test tests/AgentRelay.Protocol.Tests -c Release
```

A throwaway reference check for L1 (Python + `jsonschema`) is acceptable during
development, but the product MUST ship the .NET test project; Python is not a
runtime dependency.

## Adding a case

1. Add the JSON file under `cases/valid`, `cases/invalid`, or `cases/edge`.
2. Add an entry to `manifest.json` with `id`, `file`, `kind`, `level`, `expect`,
   and `error` (the stable error code the implementation must return).
3. If the case is semantic, add one line to the test that constructs the prior
   state (for example, "same idempotency key as `receipt-done`").
4. Do not encode implementation-specific fields; unknown keys are allowed by the
   protocol and must be ignored.

## Error codes (stable)

| Code | Meaning |
|---|---|
| `handoff.version_unsupported` | `handoff_version` is not `"1"`. |
| `handoff.field_required` | A required frontmatter field is missing. |
| `handoff.session_exact_required` | `session_id` is `latest`/`recent`/`*` or empty. |
| `receipt.id_mismatch` | `handoff_id`/`task_id`/`attempt_id`/`turn`/`sender` does not match the request. |
| `receipt.next_action_required` | `done` + `needs_reply=true` without a non-empty `next_action`. |
| `receipt.result_required` | `done` + `needs_reply=true`, or `outcome=submitted/accepted`, without `result`. |
| `receipt.open_items_required` | `outcome=changes_requested` without non-empty `open_items`. |
| `receipt.deliverable_required` | `outcome=accepted` without `deliverable`. |
| `receipt.error_required` | `status=failed` without `error.code`/`error.message`. |
| `receipt.duplicate_idempotent` | Same key + same content; return the original result. |
| `receipt.idempotency_conflict` | Same key + different content. |
| `receipt.late_accepted` | Valid receipt after `deadline_at`; accept and record as late. |
| `receipt.old_attempt` | Receipt for a non-current attempt; keep as evidence, do not transition. |
