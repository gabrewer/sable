---
description: Audit a Sable feature or specification and produce an implementation-ready sprint
argument-hint: "<feature-or-spec-path> <github-issues|filesystem>"
---

Plan the Sable work described by `$1` using `$2` as the durable state backend.

Before writing anything:

1. Read `AGENTS.md`, `TEAM-ORCHESTRATION.md`, and `TOOL-PI.md` completely.
2. Read `$1`. If it is not a path, treat it as a feature description and identify the authoritative source in the plan.
3. Read `README.md`, `build.cake`, `.github/workflows/build.yml`, and the relevant `_docs/` pages.
4. Audit relevant source under `src/Sable/` and `src/Sable.Cli/`, matching tests under `tests/`, and relevant examples under `samples/`.
5. Read existing sprint state and existing issue comments for this feature before replacing or duplicating work.
6. Confirm `$2` is exactly `github-issues` or `filesystem`. If missing or invalid, stop and ask the user; never infer the backend.

Follow the planning loop and artifact rules in `TEAM-ORCHESTRATION.md`. Use `.agents/skills/product-designer/SKILL.md` and `.agents/skills/pm/SKILL.md` as role instructions. Ask unresolved product questions before declaring the sprint ready.

The authoritative sprint issue/file must include:

- the selected state backend in its header;
- goal, in-scope and out-of-scope boundaries;
- a Contract Impact Check covering the public .NET API, CLI syntax/behavior, generated migration layout and SQL semantics, database safety, docs, samples, and tests;
- for parity or migration work, a source-delta matrix with exact paths/references;
- explicit decisions or blockers for intentional deviations;
- implementation-ready tasks with ID, type, assigned worker, dependencies, exact files to read/change, acceptance criteria, deterministic verification, commit hint, and worker skill path;
- quality gates as phases rather than task-board items;
- final smoke commands appropriate to the scope, normally `dotnet cake --target=Build` followed by `dotnet cake --target=Test`, plus docs build when `_docs/` changes;
- the original acceptance source that the final acceptance-verification checklist must use.

Repository-specific constraints:

- Treat `src/Sable/Extensions/ServiceCollectionExtensions.cs` and CLI commands/options/output as public contracts.
- Treat SQL under `samples/**/sable/` and generated `docs/` as generated artifacts; task any changes through their generation workflows.
- Do not plan direct execution against production, staging, shared, or ambiguous databases.
- Do not tunnel typed migration/database state through free text when a typed .NET or CLI contract is required.
- Do not create quality-gate child tasks for destroyer, reviewer, committer, or final smoke testing.

Backend behavior:

- `github-issues`: use a control issue when cohesive execution is preferred; compose long bodies/comments under `.pi/tmp/`; never commit drafts, close issues, or apply final labels.
- `filesystem`: write `docs/sprints/<sprint-id>.md` and use the report/build-log paths from `TEAM-ORCHESTRATION.md`; do not mirror routine state to GitHub.

Do not implement code. End with the created/updated artifact locations, unresolved questions, and whether the sprint is ready for human approval.
