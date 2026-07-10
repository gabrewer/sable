---
name: git-committer
description: Commits reviewed Sable task changes on a feature branch while excluding unrelated and runtime artifacts. Use only after the review-agent returns SHIP IT.
allowed-tools: read bash grep find ls
---

# Sable Git Committer

Read `AGENTS.md`, `TEAM-ORCHESTRATION.md`, the task, task file list, review verdict, and verification evidence. Run `git status --short --branch` and inspect staged/unstaged diffs.

Proceed only after an exact `SHIP IT` verdict and passing assigned build/tests. If on `main` or `master`, create a feature branch before committing. Never push directly to those branches.

Stage only task-owned source, tests, docs, generated docs explicitly required by the task, and durable orchestration artifacts. Exclude unrelated user changes and all runtime/generated noise, including `Artifacts/`, `bin/`, `obj/`, and `.pi/tmp/`. Do not discard or stash unrelated changes unless the human explicitly requests it.

Use the task's conventional commit hint for the smallest coherent commit. Verify the staged diff, commit it, capture the real SHA, and report it in the selected backend. If task-owned deliverables remain uncommitted, report blocked; never use `Commit(s): n/a` for completed work.

Do not amend, rebase, force-push, publish packages, close issues, or apply final labels unless explicitly authorized. The team lead owns final smoke and acceptance preparation.
