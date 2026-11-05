# Samples

Runnable Sable configurations:

- [`Sable.Samples.GettingStarted`](./Sable.Samples.GettingStarted) — basic single-store setup.
- [`Sable.Samples.MultipleDatabases`](./Sable.Samples.MultipleDatabases) — multiple Marten stores and database identities.
- [`Sable.Samples.MultiTenancy`](./Sable.Samples.MultiTenancy) — multi-tenant database setup.
- [`Sable.Samples.SchemaPreamble`](./Sable.Samples.SchemaPreamble) — isolated, test-only Marten 9.8.1 producer fixture for the older exception-only schema-creation `DO` preamble. This version has a critical security advisory and must not be used in production.
- [`Sable.Samples.CatalogGuardedSchemaPreamble`](./Sable.Samples.CatalogGuardedSchemaPreamble) — separately pinned Marten 9.57.0 / Weasel 9.48.0 producer sample for catalog-guarded schema preambles, with CLI replay, least-privilege, full-generation, and isolated-package verification.

The schema-preamble sample is also consumed by Sable's fast, disposable PostgreSQL, and full `migrations add` regression checks. Its generated migration SQL must be regenerated through its documented Sable workflow and never edited manually.
