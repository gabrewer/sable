---
description: Execute an approved Sable sprint through Pi worker roles and mandatory quality gates
argument-hint: "<github-sprint-issue-number> [resume]"
---

Execute the approved Sable sprint issue `$1` using GitHub Issues as the required durable state backend. Additional arguments: `${@:2}`.

This repository does not use agentloop and Pi has no native subagent isolation. Orchestrate the workflow sequentially in this Pi session. Before each phase, read the corresponding `.agents/skills/<role>/SKILL.md`, announce the active role, obey that role's scope, and write its evidence to the GitHub sprint issue. Never claim a subprocess or independent agent ran.

Preflight — do not edit implementation code until all checks pass:

1. Read `AGENTS.md`, `TEAM-ORCHESTRATION.md`, and `TOOL-PI.md` completely. Treat agentloop-specific invocation/state passages as background; retain the planning, role, gate, evidence, and acceptance rules.
2. Confirm the sprint header declares `github-issues` and remains the source of truth. If it conflicts with the repository's fixed GitHub Issues policy, stop and report the conflict.
3. Load the authoritative sprint record and all existing task comments/build logs/reports. Confirm human approval, original acceptance criteria, dependencies, exact file scopes, and deterministic verification commands.
4. Run `git status --short --branch`. Preserve unrelated changes. If on `main` or `master`, create a feature branch before any commit.
5. Run `dotnet tool restore` and record failures.
6. Confirm each task is implementation-ready and its dependencies are satisfied. In this single Pi session, execute tasks sequentially even when the plan says they are independent.

Enforce this phase order by explicitly loading each skill before acting:

1. domain model — `.agents/skills/domain-modeler/SKILL.md`;
2. public API/CLI contract — `.agents/skills/api-developer/SKILL.md`;
3. for each task:
   - test writer — `.agents/skills/test-writer/SKILL.md`;
   - assigned builder — `.agents/skills/backend-builder/SKILL.md` or `.agents/skills/frontend-builder/SKILL.md`;
   - deterministic build gate;
   - destroyer — `.agents/skills/destroyer/SKILL.md`;
   - reviewer and any surgical remediation loop — `.agents/skills/review-agent/SKILL.md`, then the assigned builder skill again when needed;
   - committer after `SHIP IT` — `.agents/skills/git-committer/SKILL.md`;
4. sprint smoke build/test and docs build when applicable;
5. PM summary — `.agents/skills/pm/SKILL.md`;
6. completion record and human acceptance-verification checklist.

Do not merge worker responsibilities within a phase. Builders must not alter assigned tests, destroyer/reviewer scope is limited to task-owned changes, and the committer runs only after `SHIP IT`. Role changes are instruction/scope changes within this session, not security boundaries; use deterministic commands and recorded diffs as evidence.

Mandatory repository gates:

- Before destroyer: `dotnet cake --target=Build` or a narrower approved build must pass.
- Final .NET smoke: `dotnet cake --target=Build` then `dotnet cake --target=Test`.
- If `_docs/` changes: `npm --prefix _docs ci` and `npm --prefix _docs run docs:build`; inspect generated `docs/` changes.
- Never directly execute generated SQL or any database update against production, staging, shared, or ambiguous databases.
- Generated sample migrations must use the approved Sable generation workflow defined by the task; no hand edits.
- Completed implementation work must have real commit SHAs. Never commit temporary state or unrelated changes.

Write state updates in real time to GitHub Issues using the exact headings from `TEAM-ORCHESTRATION.md`. Use `.pi/tmp/` for long comment drafts and leave issues open.

Do not report completion until every task and mandatory gate passes. Then post both:

- `## 🚀 Sprint Complete: <sprint-or-feature-id>` with commits, commands/results, tradeoffs, warnings, and deferred work;
- `## 🧑‍⚖️ Ready for Acceptance Verification: <sprint-or-feature-id>` mapped to the original criteria, with human steps, expected results, source references/artifacts, remaining deltas, and the explicit statement that tests and commits are implementation evidence—not acceptance.

Never self-approve acceptance, close GitHub issues, or apply final disposition labels.
