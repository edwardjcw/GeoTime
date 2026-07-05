# GeoTime Agent Instructions

Read this file before completing or updating `docs/status.md`.

## Working Rules

- Preserve dirty work. Do not revert, overwrite, or reformat unrelated changes.
- Keep status updates concise, factual, and scoped to the assigned section or task.
- Treat `docs/status.md` as the canonical implementation tracker. The root `status.md` is only a navigation note.
- Keep GeoTime backend-first: simulation behavior belongs in `backend/GeoTime.Core`; frontend code displays, requests, and interacts with backend state.
- When behavior changes, update focused tests near the changed backend or frontend surface.
- Do not edit generated outputs such as `bin/`, `obj/`, `dist/`, `node_modules/`, `test-results/`, or screenshots unless explicitly assigned.
- Do not run broad proof commands unless the coordinator or user grants the slot. Documentation-only changes normally require only diff review.

## Documentation Checklist

- Record changed behavior, verification commands, pass/fail status, and residual risks.
- Keep README updates limited to public workflow, command, architecture, or feature changes.
- If API or rendering contracts change, note Unreal impact explicitly.
- If optional tools are present, document their purpose without making them part of required setup.
