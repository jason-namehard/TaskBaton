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
| **L2** | Semantic validity | IDs, attempt, turn, sender match; `from != to`; UTC `Z` timestamps; safe relative paths; `done` + `needs_reply=true` has `result` + `next_action`; `changes_requested` has non-empty `open_items`; `accepted` has `deliverable`; `failed` has `error`; reviewer-final approval has `review.final=true`, `approved`, no open items, and a `result`. |
| **L3** | Duplicate / conflicting / late / old attempt | Same `(task_id, attempt_id, turn, idempotency_key)` + same content → one transition; conflicting final receipts → reject; late valid receipt → accept as late; older attempt → evidence only. |
| **L4** | Session binding | `session_id` must be exact; `latest`/`recent`/`*` rejected; resume must verify the id and reject a different id in the response. |
| **L5** | Cleanup ownership | Only files created/registered by the relay, with no active reference, and inside the managed root may be cleaned. User sources and deliverables are never deleted by TTL. |

L1 is directly covered by the fixtures. L2–L5 need a small in-memory state
machine in the implementation's test project.

## Running

The shipped .NET test project copies these fixtures and schemas to its output and
runs all manifest cases plus L2–L5 checks:

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

Codes are stable. The `field` member of a diagnostic names the missing or invalid location
(for example `handoff.field_required` with `field: "task_id"`); the manifest records the
expected field when a case needs a finer assertion.

| Code | Exit | Meaning |
|---|---|---|
| `protocol.argument_invalid` / `protocol.file_not_found` | 2 | Bad argument or missing input file. |
| `protocol.invalid_frontmatter` / `protocol.invalid_json` | 2 | Input could not be parsed. |
| `protocol.schema_unavailable` | 2 | A schema file could not be loaded. |
| `handoff.version_unsupported` | 3 | `handoff_version` is not `"1"`. |
| `handoff.field_required` | 3 | A required frontmatter field is missing; `field` names it. |
| `handoff.field_invalid` / `handoff.schema_invalid` | 3 | A field has the wrong shape or violates the schema. |
| `handoff.from_equals_to` | 3 | `from` and `to` are the same agent id. |
| `handoff.timestamp_not_utc` | 3 | A timestamp does not end in `Z`. |
| `handoff.path_unsafe` | 3 | A path is absolute, uses backslashes, contains `.`/`..`, or escapes the handoff root. |
| `handoff.section_required` | 3 | A required Markdown section is missing. |
| `handoff.session_exact_required` | 3 | `session_id` is `latest`/`recent`/`*`, empty, or has surrounding whitespace. |
| `handoff.session_not_found` | 5 | The exact session id does not exist in the host store. |
| `handoff.session_mismatch` | 5 | A resume returned a different session id. |
| `receipt.field_required` | 3 | A required receipt field is missing. |
| `receipt.version_unsupported` | 3 | `handoff_version` is not `"1"`. |
| `receipt.timestamp_not_utc` | 3 | A receipt timestamp does not end in `Z`. |
| `receipt.path_unsafe` | 3 | A receipt result/artifact path escapes the handoff root. |
| `receipt.field_invalid` / `receipt.schema_invalid` | 3 | A field has the wrong shape or violates the schema. |
| `receipt.id_mismatch` | 5 | `handoff_id`/`task_id`/`attempt_id`/`turn`/`sender` does not match the request. |
| `receipt.needs_reply_required` | 3 | `status=done` without `needs_reply`. |
| `receipt.next_action_required` | 3 | `done` + `needs_reply=true` without a non-empty `next_action`. |
| `receipt.next_action_not_allowed` | 3 | `done` + `needs_reply=false` with a non-null `next_action`. |
| `receipt.outcome_requires_done` | 3 | `outcome` appears without `status=done`. |
| `receipt.result_required` | 3 | `done` + `needs_reply=true`, `outcome=submitted/accepted`, or reviewer-final approval without `result`. |
| `receipt.open_items_required` | 3 | `outcome=changes_requested` without non-empty `open_items`. |
| `receipt.open_items_not_allowed` | 3 | `outcome=approved` or reviewer-final approval with open items. |
| `receipt.deliverable_required` | 3 | `outcome=accepted` without `deliverable`. |
| `receipt.error_required` | 3 | `status=failed` without `error.code`/`error.message`. |
| `receipt.review_final_invalid` | 3 | Reviewer-final metadata is inconsistent with outcome/result/needs_reply. |
| `receipt.duplicate_idempotent` | 0 (info) | Same key + same content; return the original result. |
| `receipt.idempotency_conflict` | 5 | Same key + different content. |
| `receipt.late_accepted` | 0 (info) | Valid receipt after `deadline_at`; accept and record as late. |
| `receipt.old_attempt` | 5 | Receipt for a non-current attempt; keep as evidence, do not transition. |
| `receipt.conflicting_final` | 5 | A different final receipt would overwrite an existing final claim. |
| `receipt.cancelled` | 5 | The task was cancelled; the receipt is evidence only. |
| `cleanup.outside_root` | 5 | Cleanup candidate is outside the managed root. |
| `cleanup.not_owned` | 5 | The relay cannot prove it created or registered the file. |
| `cleanup.active_reference` | 5 | An active task references the file. |
| `cleanup.protected` | 5 | User source, deliverable, or control file; never TTL-deleted. |
| `cleanup.not_expired` | 0 (info) | Artifact has not reached its expiry. |
