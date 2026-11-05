# Catalog-Guarded Schema Preamble Compatibility Sample

A separate runnable producer fixture for the newer Marten/Weasel schema preamble. The existing `Sable.Samples.SchemaPreamble` sample remains pinned to the older producer and its artifacts are unchanged.

## Producer provenance

- Marten: **9.57.0**, added through `dotnet add package`.
- Resolved Weasel.Postgresql/Core/Storage: **9.48.0**; dependency graph recorded in `packages.lock.json`.
- Sable CLI source used to generate the baseline: **`5361376eed6c3be4ed715abaedd61f098a7a6d65`** (local development build, not a published tool version). The previous 9.50.3 / Weasel 9.45.0 baseline is preserved in Git history.
- Generated through Sable `init` on 2026-11-05 against an owned disposable `postgres:15.1` shadow container.
- Subject selector: `marten://store/`; generated directory: `sable/Marten`; schema: `catalog_guarded_schema_preamble_sample`.

The checked-in Initial migration is authentic producer output: catalog existence check, both duplicate-schema/unique-violation handlers, product DDL, and functions. Do not edit its SQL. Sable preserves the preamble and guards remaining DDL/history. The catalog check avoids unnecessary database CREATE privilege checks when the schema already exists; the handlers address concurrent creation. This does not grant privileges or solve missing-schema/bootstrap prerequisites.

## Run locally

Supply an explicitly local development connection through `ConnectionStrings__Marten` and run:

```bash
dotnet run --project samples/Sable.Samples.CatalogGuardedSchemaPreamble
```

The checked-in connection placeholder is not a usable configured database. Never target production, staging, shared, or ambiguous databases. Do not manually apply the generated SQL.

## Docker-free composition verification

From the repository root:

```bash
dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj \
  --filter "FullyQualifiedName~CatalogGuardedSchemaPreambleSampleCompositionTests"
```

This reads authentic artifacts and verifies preservation/ordering; it does not start PostgreSQL or prove runtime privileges.

## Regenerate the baseline

Requires .NET per `global.json`, local Docker, Bash, and Python 3. From the repository root:

```bash
SABLE_REGENERATE_CATALOG_GUARDED_SAMPLE=1 \
  bash verify/catalog-guarded-schema-preambles/regenerate.sh
```

The helper invokes the current source CLI's supported `init --database marten://store/ --schema catalog_guarded_schema_preamble_sample` workflow with unique disposable shadow-container options. It captures/redacts child output, bounds generation to 180 seconds, terminates child processes on timeout, cleans only owned containers, and restores prior artifacts if generation or shape validation fails. Review newly generated files and update provenance when intentionally regenerating; never manufacture compatibility by editing SQL.

For a controlled rollback check, `SABLE_CATALOG_GUARDED_GENERATION_TIMEOUT_SECONDS=1` shortens the helper's timeout (accepted range 1–180). This is a verification-helper setting, not a Sable CLI option. Verify that a failed run restores the complete prior artifact tree byte-for-byte.

## Test-only pending mapping

Default mappings match the baseline. `SABLE_CATALOG_GUARDED_SAMPLE_INCLUDE_PENDING_CHANGE=1` enables exactly one additional mapping only in temporary end-to-end verification copies. It is not Sable configuration. The regeneration helper clears this seam and inherited connection overrides in its child.

## CLI-driven disposable verification

From the repository root with local Docker available:

```bash
bash verify/catalog-guarded-schema-preambles/postgres.sh
bash verify/catalog-guarded-schema-preambles/e2e.sh
```

The PostgreSQL lane applies old/new authentic histories through CLI child processes, checks non-idempotent replay protection, verifies effective restricted-role privileges, requires an unguarded control to fail with SQLSTATE `42501`, and checks missing-schema/rejected-history failure without partial application. Its restricted-role fixture provisions prerequisites as owner and excludes the unconditional bootstrap artifact only in its owned temporary history; application history is not rewritten.

The full-workflow lane enables the pending seam in a temporary sample copy, generates exactly one substantive migration, composes the resulting history, and replays it twice against a fresh disposable target. A rejection case proves the patch project never builds and no migration artifact changes. Baseline/source hashes and run-scoped cleanup are checked. Ordinary Cake tests skip these explicitly opt-in lanes; skips/discovery are not execution evidence.

## Isolated package verification

```bash
dotnet tool restore
dotnet cake --target=Build
dotnet cake --target=Pack
bash verify/catalog-guarded-schema-preambles/package.sh
```

The package lane installs the exact locally built CLI package into an isolated directory/cache, verifies identity and success/rejection behavior, and runs disposable CLI application against that executable. It never replaces a global tool or publishes a package. A development package version is not a release reservation. Consumer history/feed validation and publication require separate human approval.
