# Schema Preamble Compatibility Sample

This runnable Marten application demonstrates migrations that begin with a safe schema-creation `DO` preamble. Sable keeps that preamble at PostgreSQL's top level and places the remaining Marten DDL and migration-history insertion inside its `$sable$` guard.

The checked-in files under `sable/Marten/` were generated through Sable with Marten **9.8.1**. They remain an older-producer regression fixture; do not edit or rebaseline them to imitate newer output. This version has a critical security advisory: use this isolated fixture only for disposable tests, never in production. To keep its producer version independent from the current Sable package's newer minimum, the fixture links Sable's unchanged registration source rather than referencing the Sable project. It does not establish that the current package supports Marten 9.8.1. See the separate [catalog-guarded sample](../Sable.Samples.CatalogGuardedSchemaPreamble) for Marten 9.57.0 / Weasel 9.48.0.

## Run the sample

Provide an explicitly local development PostgreSQL connection through configuration, then run:

```bash
ConnectionStrings__Marten="<local-development-connection-string>" \
  dotnet run --project samples/Sable.Samples.SchemaPreamble
```

Do not point sample or verification commands at shared, staging, or production databases.

## Verification levels

The normal Docker-free test reads the checked-in migration and verifies composition ordering:

```bash
dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj \
  --filter "FullyQualifiedName~SchemaPreambleSampleCompositionTests"
```

With Docker Desktop available, apply the sample twice through Sable's `database update` CLI in an owned disposable PostgreSQL container; Npgsql is used only for result assertions:

```bash
bash verify/safe-schema-preambles/task-002.sh
```

Run the slower, full `sable migrations add` workflow against a temporary sample copy:

```bash
bash verify/schema-preamble-sample/e2e.sh
```

Both scripts create only local disposable resources and clean them afterward. Normal Cake tests skip these Docker workflows.

## Test-only pending mapping

The sample's default mappings match its checked-in migration history. `SABLE_SAMPLE_INCLUDE_PENDING_CHANGE=1` enables one additional mapping solely in the temporary end-to-end verification process. It is not a Sable setting and should not be used when running the baseline sample.

## Regenerate the migration artifacts

From the repository root:

```bash
SABLE_REGENERATE_SCHEMA_PREAMBLE_SAMPLE=1 \
  bash verify/schema-preamble-sample/regenerate.sh
```

The helper starts Sable's owned local shadow container, preserves the previous artifact directory if generation fails, and validates that Sable produced an Initial migration. Review producer output without editing it.
