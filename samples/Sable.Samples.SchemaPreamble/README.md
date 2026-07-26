# Schema Preamble Compatibility Sample

This runnable Marten application demonstrates migrations that begin with a safe schema-creation `DO` preamble. Sable keeps that preamble at PostgreSQL's top level and places the remaining Marten DDL and migration-history insertion inside its `$sable$` guard.

The checked-in files under `sable/Marten/` are generated artifacts. Do not edit them manually. From the repository root, regenerate them only through the approved local workflow:

```bash
SABLE_REGENERATE_SCHEMA_PREAMBLE_SAMPLE=1 bash verify/schema-preamble-sample/regenerate.sh
```

The regeneration command starts and removes Sable's local disposable PostgreSQL shadow container. Docker Desktop must be available.

The sample's normal mappings match its checked-in migration history. `SABLE_SAMPLE_INCLUDE_PENDING_CHANGE=1` enables one additional mapping solely for the repository's end-to-end verification. It is not a Sable setting and should not be used when running the baseline sample.

To run the application, provide a local PostgreSQL connection through configuration or user secrets, then run:

```bash
dotnet run --project samples/Sable.Samples.SchemaPreamble
```
