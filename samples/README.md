# Samples

Runnable Sable configurations:

- [`Sable.Samples.GettingStarted`](./Sable.Samples.GettingStarted) — basic single-store setup.
- [`Sable.Samples.MultipleDatabases`](./Sable.Samples.MultipleDatabases) — multiple Marten stores and database identities.
- [`Sable.Samples.MultiTenancy`](./Sable.Samples.MultiTenancy) — multi-tenant database setup.
- [`Sable.Samples.SchemaPreamble`](./Sable.Samples.SchemaPreamble) — executable compatibility sample for Marten migrations that begin with a safe schema-creation `DO` preamble.

The schema-preamble sample is also consumed by Sable's fast, disposable PostgreSQL, and full `migrations add` regression checks. Its generated migration SQL must be regenerated through its documented Sable workflow and never edited manually.
