---
name: brainstorming
description: Explores and refines a Sable feature before sprint planning. Use when a request is ambiguous, crosses public .NET/CLI/SQL contracts, or needs alternatives and deterministic verification design.
---

# Sable Brainstorming

Read `AGENTS.md`, `TEAM-ORCHESTRATION.md`, the requested source/spec, relevant code in `src/`, matching tests, docs, and samples before proposing a plan.

Follow the interactive planning process in `TEAM-ORCHESTRATION.md`:

1. Explore the user goal, affected Sable migration workflow, public API/CLI compatibility, SQL safety, docs, and likely failure modes.
2. Ask focused clarification questions in three rounds, one round at a time. Do not bury unresolved decisions in assumptions.
3. Offer three distinct approaches, one round at a time, with compatibility, complexity, testability, migration-artifact, and database-safety tradeoffs.
4. Design deterministic verification per proposed task. Prefer focused xUnit commands, Cake build/test, CLI argument/exit-code assertions, and docs builds. Database checks must use an explicitly approved disposable local database.
5. Record the original acceptance source and explicit out-of-scope items.

After user approval, run the planning preflight: verify git, create a feature branch if on `main`/`master`, and check the remote. Write the approved PRD to `docs/design/<YYYYMMDD>-<feature-slug>.md`, then hand off to `/pm-agent`; GitHub Issues is the fixed sprint backend. Do not implement the feature from this skill.
