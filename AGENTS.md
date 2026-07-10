# Sable Project Instructions

## Project overview

Sable is a .NET 10 database migration management tool for Marten/PostgreSQL. The repository publishes two NuGet packages:

- `src/Sable/` — Marten service-registration integration and the `SABLE_CONNECTION_STRING_OVERRIDE` behavior.
- `src/Sable.Cli/` — the `sable` .NET tool, Spectre.Console commands, SQL composition, shadow PostgreSQL containers, and database updates.

Supporting areas:

- `tests/Sable.Tests/` and `tests/Sable.Cli.Tests/` — xUnit tests.
- `samples/` — runnable Marten examples and checked-in Sable-generated migration artifacts.
- `_docs/` — VitePress documentation source.
- `docs/` — generated GitHub Pages output; regenerate it from `_docs/` rather than editing it by hand.
- `build.cake` — canonical restore/build/test/pack pipeline.

Read `TEAM-ORCHESTRATION.md` for planning, state-backend, quality-gate, commit, and acceptance rules. Read `TOOL-PI.md` for Pi resource layout. This repository does not use agentloop; treat agentloop-specific passages as background and apply the role sequence directly through Pi prompts and skills.

## Build and verification

Run commands from the repository root unless noted otherwise.

```bash
dotnet tool restore
dotnet cake --target=Build
dotnet cake --target=Test
dotnet cake --target=Pack
```

Useful focused checks:

```bash
dotnet build Sable.sln
dotnet test tests/Sable.Tests/Sable.Tests.csproj
dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj
dotnet format Sable.sln --verify-no-changes --no-restore
npm --prefix _docs ci
npm --prefix _docs run docs:build
```

The Cake `Test` target uses `--no-build --no-restore`; run the `Build` target first. Docker is required only for paths that start Sable's shadow PostgreSQL container or for explicitly assigned integration/smoke tests. Existing xUnit tests are unit-level and should not require Docker.

## Code and contract rules

- Preserve the public .NET API in `src/Sable/Extensions/ServiceCollectionExtensions.cs` unless the task explicitly changes it.
- Treat CLI command names, arguments, options, exit codes, generated file layout, directives, and SQL ordering as public contracts. Relevant code lives in `src/Sable.Cli/Program.cs`, `Commands/`, `Settings/`, `MartenMigrationManager.cs`, `Migration.cs`, and `Templates.cs`.
- Follow `.editorconfig`, including four-space C# indentation, `var` preferences, braces, and UTF-8 BOM for C# files.
- Add or update xUnit tests in the matching test project. Keep deterministic logic unit-testable; do not introduce Docker or live database dependencies into ordinary unit tests.
- Never change a test merely to make an implementation pass. If an assigned test conflicts with an approved contract, stop and report the mismatch.
- Do not edit package references directly for dependency changes. Use `dotnet add package` or `npm --prefix _docs install`, as applicable, and include lockfile changes.
- Do not edit generated `docs/` pages directly. Change `_docs/`, run `npm --prefix _docs run docs:build`, and review generated changes.

## Migration and database safety

- Files under `samples/**/sable/**/migrations/*.sql`, `samples/**/sable/**/scripts/*.sql`, and generated backfill SQL are Sable artifacts. Do not hand-write or manually edit them unless repository instructions explicitly require that exact fixture change and the task identifies how it will be regenerated or verified.
- Prefer Sable's approved generation workflow for generated migration fixtures. Never run generated migration SQL directly against a database with `psql`, shell redirection, ad hoc scripts, or copy/paste execution.
- Never run database update commands against production, staging, shared, or ambiguous databases. Database execution requires an explicitly identified disposable/local test database and task approval.
- Do not expose connection strings, credentials, package tokens, or environment secrets in code, logs, plans, or test fixtures.

## Git and generated artifacts

- Never commit or push directly to `main` or `master`; use a feature branch and open a PR.
- Keep task commits small and coherent. Completed work must cite real commit SHAs as required by `TEAM-ORCHESTRATION.md`.
- Do not commit `Artifacts/`, `bin/`, `obj/`, `.vs/`, or `.pi/tmp/`.
- Preserve unrelated working-tree changes. In particular, do not overwrite or discard user-authored untracked files.

## Pi and orchestration

- The user must select `github-issues` or `filesystem` as the durable state backend before planning artifacts are created. Never infer it.
- Use `.agents/skills/` for project worker roles and `.pi/prompts/` for human-facing workflow entry points.
- Quality gates are phases, not normal implementation tasks: destroyer, reviewer, committer, sprint smoke test, completion report, and acceptance-verification preparation.
- Run worker roles sequentially in the current Pi session. Before each phase, read the corresponding `.agents/skills/<role>/SKILL.md`, announce the active role, follow its scope, and record its evidence in the selected backend. Do not claim subprocess or process isolation.
- Agents prepare `## 🧑‍⚖️ Ready for Acceptance Verification: ...` evidence but never self-approve acceptance, close GitHub issues, or apply final disposition labels.
