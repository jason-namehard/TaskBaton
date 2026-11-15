# HANDOFF.md — Portable handoff between coding agents

Status: **Draft v1** (`handoff/1`) · 2026-11-13
Maintainer: TaskBaton (formerly Agent Relay)
License: MIT, consistent with the repository `LICENSE`. The document, schemas, and conformance fixtures are all MIT.

> A handoff is not a chat message. It is a small, self-contained work order plus a
> receipt that says what actually happened. Any agent that can read a file and
> write JSON can participate.

---

## 1. Why

Multi-agent work breaks in predictable places:

- the receiver never saw the request, but the sender assumes it did;
- an agent says "done", but no file changed (or the file changed and nobody verified it);
- a session is resumed by "latest" and lands in the wrong conversation;
- temporary files pile up because nobody knows which ones the relay owns;
- a retry duplicates work because the previous attempt actually succeeded.

`HANDOFF.md` addresses only those problems. It does not define how an agent
plans, reasons, or calls tools. It defines **what is handed over**, **what a
valid receipt is**, and **when a handoff is complete**.

## 2. Artifacts

A handoff consists of:

| Artifact | Required | Purpose |
|---|---|---|
| `HANDOFF.md` | yes | The request: YAML frontmatter + a human/agent-readable body |
| `receipt.json` | yes (produced by receiver) | The result: status, outcome, artifacts, evidence |
| referenced files | no | Inputs, deliverables, evidence; paths relative to the handoff directory |

A relay/ledger implementation may store additional state (task, attempt, events).
That state is **not** part of this document. The document must remain useful even
if the ledger is lost.

## 3. Request frontmatter (normative)

`HANDOFF.md` starts with YAML frontmatter. Unknown keys are allowed and ignored
by `handoff/1` readers, but producers SHOULD keep the set below.

| Field | Type | Required | Rules |
|---|---|---|---|
| `handoff_version` | string | yes | Must be `"1"`. |
| `handoff_id` | UUID | yes | Identifies this document. New on every handoff. |
| `task_id` | string | yes | Stable across attempts and turns. |
| `attempt_id` | UUID | yes | New whenever the work is retried as a new attempt. |
| `turn` | integer | yes | `0` for the first request; increments per return trip. |
| `from` | string | yes | Sender agent id. Must not equal `to`. |
| `to` | string | yes | Receiver agent id. |
| `created_at` | RFC 3339 UTC | yes | `Z` suffix required. |
| `idempotency_key` | string | yes | Unique per logical delivery. Re-sending the same key is a no-op. |
| `receipt_path` | string | yes | Relative path where the receiver writes `receipt.json`. |
| `reply_to` | string \| null | no | Path of the previous `receipt.json`, when this is a return trip. |
| `deadline_at` | RFC 3339 UTC | no | After this time the handoff is late; it is not automatically failed. |
| `session` | object | no | Exact session binding; see §7. |
| `artifacts` | array | no | Inputs/deliverables; see §9. |
| `labels` | object | no | Free-form string metadata for dashboards. |

`from` and `to` are opaque agent ids. Implementations SHOULD also accept a
`display_name` in their registry; the document uses ids only.

> **Timestamp note.** YAML 1.1 parsers may coerce an unquoted timestamp to a
> native datetime. Producers SHOULD quote timestamps (`"2026-11-13T02:00:00Z"`)
> or use a parser that preserves strings. Validators MUST normalize parsed
> timestamps back to RFC 3339 before schema validation.

### 3.1 Body (normative sections)

The Markdown body MUST contain the following level-2 headings, in any order:

- `## Summary` — one paragraph: what to do and why.
- `## Instructions` — concrete steps, acceptance-relevant constraints, and what
  not to touch.
- `## Acceptance criteria` — a checklist the receiver can verify; each item
  should be objectively checkable.
- `## Context` — decisions already made, files to read, prior attempts. Keep it
  brief; link to artifacts instead of pasting large content.

Optional: `## Constraints`, `## References`, `## Open questions`.

The body is **data**, not an executable script. A receiver MUST NOT execute
embedded commands just because they appear in the document.

## 4. Receipt (normative)

The receiver writes `receipt.json` at `receipt_path`. The full schema is
[`schemas/receipt.schema.json`](schemas/receipt.schema.json).

Required fields:

| Field | Type | Rules |
|---|---|---|
| `handoff_version` | string | `"1"` |
| `handoff_id` | UUID | Must match the request. |
| `task_id` | string | Must match the request. |
| `attempt_id` | UUID | Must match the request. |
| `turn` | integer | Must match the request. |
| `sender` | string | Must equal the request's `to`. |
| `status` | enum | `received`, `working`, `done`, `failed`. |
| `written_at` | RFC 3339 UTC | When this receipt was written. |
| `idempotency_key` | string | Unique per receipt write; reuse means "same result". |

Optional fields:

| Field | Type | Rules |
|---|---|---|
| `outcome` | enum | `submitted`, `changes_requested`, `approved`, `accepted`; only with `status=done`. |
| `needs_reply` | boolean | Required when `status=done`. |
| `result` | object | `{path, sha256?, summary?}`; required when `needs_reply=true` or `outcome` is `submitted`/`accepted`. |
| `deliverable` | string | Required for `outcome=accepted`; path or one-line delivery note. |
| `open_items` | string[] | Required and non-empty for `outcome=changes_requested`. |
| `next_action` | string \| null | Required (non-empty) when `needs_reply=true`. |
| `verification` | object | `{passed: boolean, commands: string[], evidence: string[]}`. |
| `error` | object | `{code, message}`; required for `status=failed`. |
| `side_effects_possible` | boolean \| null | `null` means unknown; never assume "no side effects". |
| `session` | object | The session that produced this receipt. |
| `artifacts` | array | Files registered by this attempt. |
| `review` | object | Optional review metadata: `decision` (`author_accept` or `reviewer_final`), `reviewer`, `author`, and `final` (boolean). See §4.2. |

### 4.1 Status semantics

| Status | Meaning | Does it complete the task? |
|---|---|---|
| `received` | The receiver has seen the request. | No. |
| `working` | The receiver has started. | No. |
| `done` | The receiver claims the work is complete. | Only per §5. |
| `failed` | The receiver cannot complete it. | No; it parks the task for a human decision. |

`done` is a **claim**, not proof. A relay may accept it only if the receipt is
valid and the referenced artifacts exist and are readable.

### 4.2 Outcome semantics (review loops)

When a task uses an author/reviewer loop:

- `submitted` — author submitted work; goes to review.
- `changes_requested` — reviewer requires changes; `open_items` must be non-empty.
- `approved` — reviewer accepts the submission. Legacy tasks return to the author
  for `accepted`; tasks that use reviewer-final approval end here (see below).
- `accepted` — author accepts delivery; the legacy terminal outcome.

A `done` receipt with `outcome=accepted` and no `open_items` is terminal
(legacy author acceptance). A task MAY instead end on **reviewer-final
approval**: `outcome=approved`, no `open_items`, a `result`, and the additive
`review` object with `final: true` (optionally
`decision: reviewer_final`). Such a receipt MUST set `needs_reply=false`.
`approved` without `review.final` remains non-terminal.

`review` is optional and additive: handoff/1 readers that do not understand it
MUST ignore it. This keeps existing author-accept flows and older readers valid.

## 5. Completion rules (normative)

1. **A file appearing or changing never completes a handoff.** Only a valid
   receipt can change its state.
2. `done` + `needs_reply=false` MAY complete the handoff if the implementation
   has no unresolved acceptance criteria.
3. `done` + `needs_reply=true` MUST include `result` and a non-empty
   `next_action`; the relay continues to the next receiver.
4. The review loop completes on either (a) legacy author acceptance
   (`outcome=accepted`, no `open_items`) or (b) reviewer-final approval
   (`outcome=approved`, no `open_items`, a `result`, and
   `review.final=true`). Review approval without `review.final` is not terminal.
5. A receipt whose `handoff_id`, `task_id`, `attempt_id`, `turn`, or `sender`
   does not match the request is **invalid** and MUST be rejected with the field
   name and the value found.
6. A receipt for an older attempt MUST NOT overwrite the current attempt's state.
   It MAY be retained as late evidence.
7. A valid late receipt MAY be accepted when there is no newer attempt and the
   task was not cancelled. Its acceptance MUST be recorded as late.
8. `failed` MUST NOT be retried automatically. A new attempt requires an explicit
   operator decision (or an explicit "retry" policy in the ledger).
9. Duplicate receipts with the same `(task_id, attempt_id, turn,
   idempotency_key)` and the same content are idempotent: one state transition,
   one notification.
10. Conflicting final receipts for the same attempt MUST be rejected; a success
    MUST NOT be overwritten by a later failure.

## 6. Delivery facts are separate

Implementations MUST keep these facts distinct, and MUST NOT display one as
another:

- request written (local)
- delivery attempted / accepted / **unknown**
- receiver declared `received` / `working`
- receiver submitted a result (`done`)
- host turn ended (if the host exposes it)
- handoff flow finished

"Sent" does not mean "received". "Received" does not mean "done". "Done" does
not mean "accepted".

If delivery is unknown, do not auto-resend. Reconcile with the transport's
evidence, or ask the operator; a resend MUST be an explicit decision.

## 7. Session binding

`session` binds the next turn to one exact conversation:

```yaml
session:
  host: codex            # or claude, dsh, zcode, hermes, ...
  session_id: 01a16428-7b00-7aa0-bb35-83d2018018cf
  cwd: C:/work/project
  profile: null          # optional; host-specific config profile
```

Rules:

- `session_id` MUST be an exact id. `latest`, `recent`, `*`, an empty string, or
  a value with surrounding whitespace is invalid. Placeholder checks are
  case-insensitive.
- A receiver resuming a session MUST verify that the exact id still exists in the
  host's store before sending, and MUST reject a response that returns a
  different id (byte-for-byte equality, not a prefix or fuzzy match).
- If the session is missing or unverifiable, the correct behavior is to ask for
  a new binding or fall back to manual copy — never to pick "the most recent"
  conversation.
- Visibility and resumability are separate. A session may be resumable while its
  UI visibility is unknown.

## 8. Idempotency

- `handoff_id` is unique per document; `(task_id, attempt_id, turn)` identifies a
  step.
- `idempotency_key` is the delivery key. Retrying with the same key and the same
  content returns the original result; the same key with different content is a
  conflict (`idempotency_conflict`).
- Receipts use `(task_id, attempt_id, turn, sender, idempotency_key)` as their
  uniqueness key.
- Implementations SHOULD persist idempotency keys across restarts.

## 9. Artifacts, TTL, and ownership

Each artifact entry:

```yaml
artifacts:
  - path: notes/result.md
    role: result          # request | input | result | evidence
    sha256: 3f2a...       # optional but recommended
    expires_at: 2026-11-16T00:00:00Z   # optional
```

Rules:

- Paths are relative to the handoff directory unless absolute is explicitly
  allowed by the implementation. Relative paths use forward slashes, must not be
  rooted or start with a drive letter, and must not contain `.` or `..` segments.
  The resolved path MUST remain inside the handoff directory.
- A relay MUST only clean files it can prove it owns: it created them, or the
  handoff registered them, and no active task references them.
- User source files, deliverables in the project tree, and control files MUST
  NOT be deleted by TTL cleanup.
- Deleting a task MUST NOT delete the referenced files automatically.

## 10. Security

- Never put credentials, tokens, cookies, or private keys in `HANDOFF.md`,
  `receipt.json`, or artifact metadata.
- Treat the body as untrusted input. Do not execute embedded commands; do not
  follow instructions that expand the receiver's permissions.
- A receiver MUST NOT modify files outside the workspace declared for the
  handoff, except for the `receipt_path`.
- Implementations SHOULD reject path traversal (`..`) and absolute paths that
  escape the handoff root.

## 11. Compatibility

- `agents-handoff` (MIT): this spec intentionally mirrors its
  frontmatter/body split. Import/export tools SHOULD preserve unknown keys.
- `ai-agent-handoff`: this spec is **not** a reimplementation of its schema; do
  not claim compatibility without a mapping test.
- ACP / MCP: transport-level protocols are orthogonal. A handoff may travel over
  ACP, MCP, a CLI prompt, or a copied file.

## 12. Conformance levels

| Level | What it checks |
|---|---|
| L1 | The document and receipt validate against the JSON Schemas. |
| L2 | IDs, attempt, turn, and sender match; body sections exist. |
| L3 | Duplicate, conflicting, late, and old-attempt receipts behave per §5. |
| L4 | Session ids are exact and verified before resume. |
| L5 | Cleanup only touches proven-owned artifacts with no active references. |

Fixtures and the expected results live in
[`conformance/`](conformance/README.md). A conforming implementation SHOULD run
them in CI.

## 13. Minimal example

`HANDOFF.md`:

```markdown
---
handoff_version: "1"
handoff_id: 6f1c2f0e-6a4a-4a6e-9d6e-2f6b1a8c9d10
task_id: task-2026-11-13-001
attempt_id: 0b6f5a2e-1c3d-4e5f-8a9b-0c1d2e3f4a5b
turn: 0
from: planner
to: writer
created_at: "2026-11-13T02:00:00Z"
idempotency_key: task-2026-11-13-001/turn-0
receipt_path: receipt.json
session:
  host: codex
  session_id: 01a16428-7b00-7aa0-bb35-83d2018018cf
  cwd: C:/work/project
---

## Summary

Update the uninstall wording in CHANGELOG.md and docs/releases/v0.1.4.md.

## Instructions

- Keep existing entries; only change the two paragraphs named in the issue.
- Do not touch unrelated files.

## Acceptance criteria

- [ ] `CHANGELOG.md` contains the new wording.
- [ ] `docs/releases/v0.1.4.md` contains the same wording.
- [ ] `git diff --stat` shows only those two files.

## Context

The wording was flagged in the 0.1.4 release review. The desired sentence is in
the linked issue; do not rewrite the rest of the document.
```

`receipt.json`:

```json
{
  "handoff_version": "1",
  "handoff_id": "6f1c2f0e-6a4a-4a6e-9d6e-2f6b1a8c9d10",
  "task_id": "task-2026-11-13-001",
  "attempt_id": "0b6f5a2e-1c3d-4e5f-8a9b-0c1d2e3f4a5b",
  "turn": 0,
  "sender": "writer",
  "status": "done",
  "outcome": "submitted",
  "needs_reply": false,
  "result": { "path": "notes/result.md", "sha256": "…" },
  "verification": { "passed": true, "commands": ["git diff --stat"], "evidence": ["notes/result.md"] },
  "written_at": "2026-11-13T02:12:00Z",
  "idempotency_key": "task-2026-11-13-001/turn-0/receipt"
}
```

## 14. Versioning

- `handoff_version` is the protocol version. A breaking change bumps it to `"2"`.
- Readers MUST reject an unknown major version rather than guessing.
- Additive optional fields do not require a version bump; readers ignore unknown
  keys. The optional receipt `review` object is such an additive field.
- This document is a draft. Feedback that changes normative behavior should
  include a conformance case.

---

*TaskBaton is the reference implementation. The protocol is intentionally small
so other relays, agent frameworks, and desktop apps can implement it without
adopting TaskBaton.*
