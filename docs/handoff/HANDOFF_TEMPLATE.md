# Coordinator Handoff — Wave {{WAVE_NUMBER}}

**Written:** {{ISO_DATE}}  
**Effort:** {{EFFORT_SCOPE_ONE_LINE}}  
**Coordinator wave:** {{WAVE_NUMBER}} of ongoing effort  
**Reason for handoff:** {{CONTEXT_LIMIT | MILESTONE_BOUNDARY | USER_DIRECTED | BOSS_DIRECTED}}

---

## Effort progress (counts only)

| Metric | Value |
|--------|-------|
| Work items done | {{DONE_COUNT}} (total {{TOTAL}} if known) |
| Next work item | e.g. issue key, status row, or task label |
| Integration branch | e.g. `feature/my-effort` |

Canonical detail lives in `docs/status.md` and/or the tracker named in the effort scope — do not duplicate full task tables here.

---

## What this wave completed

- {{WORK_ITEM}} — one-line outcome
- {{WORK_ITEM}} — one-line outcome

(Max 15 bullets. No file diffs. No logs.)

---

## Integration state

- **Branches/worktrees active:** name → purpose (one line each)
- **Merge order pending:** ordered list of branch names or "none"
- **Hot-spot contracts changed:** file paths only, or "none"
- **Proof gates passed this wave:** gate name → pass/fail (one line each)

---

## WIP and blockers

| Work item | State | Blocker (if any) |
|-----------|-------|------------------|
| {{ID_OR_LABEL}} | in_progress / proof_pending / blocked | one line |

---

## Decisions the next coordinator must honor

1. (Max 8 numbered decisions — architecture, branch, contract, or sequencing only)

---

## Next coordinator: first actions

1. Read this file, `docs/status.md`, README.md, and `docs/instructions.md` when present. Read any plan or tracker sources named in the effort scope.
2. **Keep context low** — delegate all implementation, proof, repair, and integration to `geotime-leaf-worker` or controller roles.
3. **Do not stop at milestone boundaries** — continue until the effort is complete or a documented user-blocking blocker needs the user.
4. Resume at work item: **{{NEXT_WORK_ITEM}}**
5. First delegation: (one line — which leaf task and branch)

---

## Do not carry forward

- Full agent transcripts, diffs, test logs, or code excerpts.
- Completed work item implementation detail (status.md or tracker has it).

---

*End of handoff. Next coordinator deletes nothing; append new handoff as next wave when boss directs.*
