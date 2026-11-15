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

Update the uninstall wording in `CHANGELOG.md` and `docs/releases/v0.1.4.md`.

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
