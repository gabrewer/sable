# Product Requirements Document: Executable Schema-Preamble Compatibility Sample

**Status:** Approved for sprint planning
**Planned sprint state backend:** github-issues
**Owner / lead:** team-lead
**Depends on:** issue #5, safe top-level schema `DO` preambles
**Related branch:** `feature/schema-preamble-sample` (stacked on the unpushed issue #5 implementation)

## Summary

Add a separate, runnable sample named `Sable.Samples.SchemaPreamble` that demonstrates the Marten migration shape fixed by issue #5. Use the same checked-in, Sable-generated sample migrations as the source for three levels of regression proof: a fast Docker-free composition test, an opt-in disposable PostgreSQL double-replay test, and a slower opt-in end-to-end `sable migrations add` test that reproduces the original user workflow.

The sample is both user-facing and an executable compatibility fixture. It must not complicate or repurpose the existing Getting Started sample.

## Original acceptance source

After issue #5 implemented safe top-level schema-preamble composition, the user asked whether samples could provide stronger testing. The user explicitly selected:

1. a separate sample rather than changing Getting Started;
2. both fast script/replay coverage and a full `migrations add` workflow;
3. a sample that is runnable by users and consumed by automated tests.

## Problem

Issue #5 has strong inline fixtures and an owned PostgreSQL smoke test, but the representative SQL currently lives inside test code. That proves behavior while leaving three gaps:

- Users cannot easily run or inspect a focused example of the compatibility scenario.
- The smoke test can drift away from SQL actually generated for a Marten sample project.
- The original `sable migrations add` shadow-replay workflow is not exercised end to end against a maintained sample.

## Target users and workflows

### Users

- Sable maintainers validating compatibility with current Marten-generated migration SQL.
- Sable users learning why schema preambles remain top-level.
- Contributors changing migration composition, sample configuration, or Marten package versions.

### Workflows

- Build and run `samples/Sable.Samples.SchemaPreamble` like the other sample applications.
- Inspect its checked-in Sable migration history and README.
- Run normal Docker-free tests against the checked-in sample artifact.
- Opt into disposable PostgreSQL replay verification.
- Opt into the slower `sable migrations add` workflow against a temporary copy.

No public Sable command, option, API, output path, exit code, or generated SQL contract changes.

## Product requirements

### PR-1: Separate runnable sample

Create `samples/Sable.Samples.SchemaPreamble/` and add its project to `Sable.sln`. The project shall:

- use `AddMartenWithSableSupport` and the repository's current Marten/JasperFx command conventions;
- configure a dedicated schema such as `schema_preamble_sample`;
- contain a small, understandable document model;
- use no embedded credentials or shared database requirement;
- build in the normal Cake Build target;
- include a focused README explaining the migration shape, safe ordering, run commands, and Docker requirements.

The existing Getting Started, Multiple Databases, and Multi-Tenancy samples remain unchanged except for optional links from sample/documentation indexes.

### PR-2: Authentic generated migration artifacts

The sample shall check in infrastructure and representative migration files produced through Sable's approved generation workflow with the repository's current Marten version. The representative migration must naturally contain:

- a leading safe schema-creation `DO` preamble matching issue #5's accepted shape;
- remaining Marten DDL after the preamble;
- at least one function or procedure body that exercises dollar-quote isolation.

Generated files under the sample's `sable/**/migrations` and `sable/**/scripts` directories must never be manually authored or edited. The implementation evidence must record the exact generation commands and disposable container configuration. If current Marten does not naturally produce the required shape, stop and report the mismatch rather than manufacturing SQL.

Provide a local/disposable regeneration helper or documented workflow. Regeneration may change timestamped file names; reviewers compare semantics and provenance rather than requiring timestamp stability.

### PR-3: Controlled pending sample change

The baseline sample source must match its checked-in migration history for ordinary users. The full workflow test needs a deterministic pending schema change without leaving the repository sample permanently out of date.

Provide a clearly named, test-only opt-in such as `SABLE_SAMPLE_INCLUDE_PENDING_CHANGE=1` that adds one simple Marten mapping only for the temporary end-to-end run. Without that opt-in, the sample behaves normally and has no pending change. The sample README must identify this as a verification seam, not a Sable product setting.

### PR-4: Fast Docker-free sample contract test

Add deterministic xUnit coverage that reads the checked-in representative sample migration and composes it through Sable. The test shall verify:

- the sample fixture exists and targets the configured sample schema;
- the schema preamble is emitted before `DO $sable$`;
- remaining DDL/function text is inside the guard;
- transaction and history ordering remain correct;
- the accepted sample text is preserved rather than rewritten.

This test runs in the normal Cake Test target and starts no process, container, or database.

### PR-5: Opt-in PostgreSQL replay using the sample

Refactor or extend the existing `SafeSchemaPreamblePostgresSmokeTests` so the checked-in sample artifact—not duplicated inline migration SQL—is the runtime source of truth. With `SABLE_RUN_POSTGRES_SMOKE=1`, the test shall:

- start only an owned disposable local PostgreSQL container with unique credentials/database and random host port;
- compose the sample migration history through Sable;
- execute the consolidated script twice;
- verify representative schema objects and function behavior;
- verify exactly one history row for the representative migration;
- always dispose the container.

Without the opt-in variable, normal tests skip before constructing Docker resources.

### PR-6: Opt-in full `migrations add` workflow

Add a slower verification gated by `SABLE_RUN_SAMPLE_E2E=1`. It shall:

1. copy the entire sample to a unique temporary directory;
2. enable the test-only pending mapping in that process;
3. invoke the built Sable CLI with `migrations add` and a unique migration name;
4. use only a test-created local container-options file and a non-shared/free local port;
5. prove shadow replay crosses the checked-in schema preamble without the original PostgreSQL `42601` error;
6. assert exit code `0`, success output, and exactly one newly generated migration with no leftover drop file;
7. optionally consolidate and replay the resulting history in another owned disposable database when needed for meaningful proof;
8. remove temporary files and every owned container on success or failure.

The test must not modify the checked-in sample, use a developer's configured connection string, or run in normal Cake Test/CI unless explicitly enabled.

### PR-7: Documentation and discoverability

Update `samples/README.md` to list the new sample and its purpose. Add a concise link from the relevant `_docs` Getting Started or reference page so users can find the runnable compatibility example. Regenerate `docs/` through VitePress and preserve all `docs/design/` records byte-for-byte.

Documentation must distinguish:

- the sample's test-only pending-change variable from Sable configuration;
- fast tests from Docker opt-in tests;
- safe schema-preamble support from arbitrary `DO` support;
- local/disposable execution from production database workflows.

## Compatibility and safety invariants

1. No public Sable library or CLI contract changes.
2. Existing sample behavior and checked-in generated artifacts are not rewritten to create this scenario.
3. New generated sample SQL is accepted only when produced by the approved Sable/Marten workflow.
4. Normal Cake tests never start Docker.
5. Every database is uniquely owned, local, disposable, and unambiguously created by the test.
6. The full test mutates only a temporary sample copy.
7. No connection strings, passwords, package tokens, or environment secrets are committed or logged.
8. Issue #5's fail-closed parser and directive behavior remain unchanged.

## Contract Impact Check

- **UI only?** No; Sable has no application UI.
- **Public .NET API affected?** No.
- **CLI syntax/options affected?** No; existing commands are exercised only.
- **Generated path/layout affected?** No product path change; the new sample uses the existing `sable/<database>/...` layout.
- **SQL semantics affected?** No new semantics; tests lock issue #5 behavior against authentic sample artifacts.
- **Persistence/auth/ownership affected?** No product persistence or auth. Test-owned database lifecycle must be explicit.
- **Backend tests needed?** Yes: Docker-free xUnit composition plus two opt-in Docker levels.
- **Documentation affected?** Yes: sample README/index, `_docs` source, generated `docs/`.
- **Dependency changes expected?** None unless generation proves a currently missing package is essential; any package change requires approved CLI commands and review.

## Acceptance criteria

- [ ] `Sable.Samples.SchemaPreamble` builds and is understandable as a standalone runnable sample.
- [ ] Its representative checked-in migration is proven to be generated by current Sable/Marten and naturally contains the required leading schema preamble and dollar-quoted function body.
- [ ] The baseline sample has no pending schema change without the explicit test seam.
- [ ] A normal Docker-free test composes the sample artifact and verifies top-level/guard/history ordering.
- [ ] The opt-in PostgreSQL test consumes the sample artifact, replays twice, verifies objects/function behavior, and records one target history row.
- [ ] The opt-in full workflow runs `sable migrations add` against a temporary sample copy and generates one new migration without the original syntax error.
- [ ] Normal Cake Test skips both Docker workflows without starting containers.
- [ ] All owned temporary files, ports, processes, and containers are cleaned up after success and failure.
- [ ] Existing samples and generated artifacts do not regress.
- [ ] Sample/source documentation and generated pages are current, and every `docs/design/` record survives generation unchanged.
- [ ] Cake Build/Test/Pack, formatting, both approved Docker checks, and docs build pass.

## Verification requirements

```bash
dotnet tool restore
dotnet cake --target=Build
dotnet cake --target=Test
dotnet cake --target=Pack
dotnet format Sable.sln --verify-no-changes --no-restore
SABLE_RUN_POSTGRES_SMOKE=1 dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj --filter "FullyQualifiedName~SchemaPreamble"
SABLE_RUN_SAMPLE_E2E=1 dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj --filter "FullyQualifiedName~SchemaPreambleSampleEndToEnd"
npm --prefix _docs ci
npm --prefix _docs run docs:build
```

Docker commands are approved only for the two explicitly gated tests and sample artifact generation, using unambiguously local disposable resources.

## Out of scope

- Changing issue #5's parser or expanding accepted `DO` grammar.
- Modifying existing generated sample migrations/scripts.
- Enabling Docker tests by default in Cake or CI.
- Running against shared, staging, production, or user-configured databases.
- Adding schema suppression for externally managed schemas.
- Turning the sample's pending-change seam into a public Sable environment variable.
- Hand-authoring SQL to imitate Marten output.

## Decisions

- 2026-07-11 — Use a separate sample rather than changing Getting Started.
- 2026-07-11 — Provide both fast and full workflow verification, with Docker work explicitly gated.
- 2026-07-11 — Make the sample runnable for users and reusable by tests.
- 2026-07-11 — Keep authentic generated sample SQL as the single compatibility fixture for deterministic and runtime tests.
- 2026-07-11 — Use a test-only pending mapping seam so the checked-in baseline remains current.
