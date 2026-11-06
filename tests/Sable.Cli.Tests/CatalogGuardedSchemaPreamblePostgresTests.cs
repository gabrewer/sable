// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using Npgsql;
using Sable.Cli.Tests.Support;
using Xunit;

namespace Sable.Cli.Tests;

public class CatalogGuardedSchemaPreamblePostgresTests
{
    private const string Schema = CatalogGuardedSchemaPreambleSampleCompositionTests.Schema;
    private const string Role = "sable_restricted";

    [CatalogPostgresFact]
    [Trait("Category", "CatalogGuardedPostgres")]
    public async Task GeneratedHistoryAndNonIdempotentProbeReplayTwice()
    {
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var history = fixture.CopySampleHistory();
        AddNonIdempotentProbe(history);
        (await fixture.Update(history)).AssertUpdated();
        (await fixture.Update(history)).AssertUpdated();
        Assert.Equal("2026-07-26 12:34:56", await fixture.Query<string>(
            $"SELECT {Schema}.mt_immutable_timestamp('2026-07-26 12:34:56')::text"
        ));
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{Schema}.mt_doc_catalogitem') IS NOT NULL"));
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{Schema}.replay_probe') IS NOT NULL"));
        await AssertHistoryRecordedOnce(fixture, history);
    }

    [CatalogPostgresFact]
    [Trait("Category", "CatalogGuardedPostgres")]
    public async Task ExistingSchemaSupportsRestrictedRoleWithoutDatabaseCreate()
    {
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var connectionString = await CreateRestrictedRole(fixture);
        await SetupSchemaAndHistory(fixture);
        await fixture.ExecuteSetup($"GRANT USAGE, CREATE ON SCHEMA {Schema} TO {Role}; GRANT SELECT, INSERT ON {Schema}.__sable_migrations TO {Role};");
        await AssertRestrictedRole(fixture, connectionString);
        Assert.True(await fixture.Query<bool>($"SELECT has_schema_privilege(current_user, '{Schema}', 'USAGE')", connectionString));
        Assert.True(await fixture.Query<bool>($"SELECT has_schema_privilege(current_user, '{Schema}', 'CREATE')", connectionString));
        Assert.True(await fixture.Query<bool>($"SELECT has_table_privilege(current_user, '{Schema}.__sable_migrations', 'SELECT')", connectionString));
        Assert.True(await fixture.Query<bool>($"SELECT has_table_privilege(current_user, '{Schema}.__sable_migrations', 'INSERT')", connectionString));

        // Handwritten privilege control, not execution of any generated migration.
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var control = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {Schema}", connection, transaction);
            var error = await Assert.ThrowsAsync<PostgresException>(() => control.ExecuteNonQueryAsync());
            Assert.Equal("42501", error.SqlState);
            await transaction.RollbackAsync();
        }

        // Exclude the unconditional bootstrap artifact from this owned temporary
        // history; the fixture owner has provisioned prerequisites instead.
        var history = fixture.CopySampleHistory(includeInfrastructure: false);
        AddNonIdempotentProbe(history);
        (await fixture.Update(history, connectionString)).AssertUpdated();
        (await fixture.Update(history, connectionString)).AssertUpdated();
        await AssertRestrictedRole(fixture, connectionString);
        await AssertHistoryRecordedOnce(fixture, history);
    }

    [CatalogPostgresFact]
    [Trait("Category", "CatalogGuardedPostgres")]
    public async Task MissingSchemaStillFailsForRestrictedRoleWithoutPartialState()
    {
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var connectionString = await CreateRestrictedRole(fixture);
        await AssertRestrictedRole(fixture, connectionString);
        var history = fixture.CopySampleHistory(includeInfrastructure: false);
        var result = await fixture.Update(history, connectionString);
        Assert.Contains("42501", result.Output);
        Assert.Contains("permission denied", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Successfully updated", result.Output);
        Assert.Equal(0L, await fixture.Query<long>($"SELECT count(*) FROM pg_catalog.pg_namespace WHERE nspname = '{Schema}'"));
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{Schema}.__sable_migrations') IS NULL"));
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{Schema}.mt_doc_catalogitem') IS NULL"));
    }

    [CatalogPostgresFact]
    [Trait("Category", "CatalogGuardedPostgres")]
    public async Task RejectedLaterHistoryExecutesNoEarlierMigrationSql()
    {
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        await SetupSchemaAndHistory(fixture);
        var history = fixture.CreateHistory(Schema);
        File.WriteAllText(Path.Combine(history.MigrationsDirectory, "20990101000000_Valid.sql"),
            $"-- Handwritten test migration\nCREATE TABLE {Schema}.must_not_execute (id integer);");
        var invalid = CatalogGuardedSchemaPreambleTests.Preamble(Schema, Schema)
            .Replace($"nspname = '{Schema}'", "nspname = 'other_schema'", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(history.MigrationsDirectory, "20990102000000_Rejected.sql"), invalid);
        var result = await fixture.Update(history);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sable cannot safely wrap migration '20990102000000_Rejected'", result.Output);
        Assert.DoesNotContain("Successfully updated", result.Output);
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{Schema}.must_not_execute') IS NULL"));
        Assert.Equal(0L, await fixture.Query<long>($"SELECT count(*) FROM {Schema}.__sable_migrations"));
    }

    [CatalogPostgresFact]
    [Trait("Category", "CatalogGuardedPostgres")]
    public async Task PostgresNameCoercionPreservesRestrictedGuardedReplay()
    {
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var connectionString = await CreateRestrictedRole(fixture);
        await AssertRestrictedRole(fixture, connectionString);
        foreach (var (requested, physical) in new[]
        {
            (new string('s', 64), new string('s', 63)),
            (new string('é', 32), new string('é', 31)),
        })
        {
            var identifier = "\"" + requested + "\"";
            var physicalIdentifier = "\"" + physical + "\"";
            await SetupSchemaAndHistory(fixture, physicalIdentifier);
            await fixture.ExecuteSetup($"GRANT USAGE, CREATE ON SCHEMA {physicalIdentifier} TO {Role}; GRANT SELECT, INSERT ON {physicalIdentifier}.__sable_migrations TO {Role};");
            // nspname is PostgreSQL type name: the unknown literal is coerced by
            // PostgreSQL, not by Sable. It still identifies the existing schema.
            Assert.Equal(1L, await fixture.Query<long>($"SELECT count(*) FROM pg_catalog.pg_namespace WHERE nspname = '{requested}'"));
            var history = fixture.CreateHistory(identifier);
            var preamble = CatalogGuardedSchemaPreambleTests.Preamble(requested, identifier);
            File.WriteAllText(Path.Combine(history.MigrationsDirectory, "20990101000000_NameCoercion.sql"),
                preamble + $"\nCREATE TABLE {identifier}.name_coercion_probe (id integer);");
            (await fixture.Update(history, connectionString)).AssertUpdated();
            (await fixture.Update(history, connectionString)).AssertUpdated();
            Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{physicalIdentifier}.name_coercion_probe') IS NOT NULL"));
            Assert.Equal(1L, await fixture.Query<long>($"SELECT count(*) FROM {physicalIdentifier}.__sable_migrations"));
            await AssertRestrictedRole(fixture, connectionString);
        }
    }

    [CatalogPostgresFact]
    [Trait("Category", "CatalogGuardedPostgres")]
    public async Task BackslashLiteralsAreRejectedWhenStandardConformingStringsIsOff()
    {
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Options = "-c standard_conforming_strings=off",
        }.ConnectionString;
        Assert.Equal("off", await fixture.Query<string>("SHOW standard_conforming_strings", connectionString));
        Assert.Equal("6108", await fixture.Query<string>("SELECT encode(convert_to('a\\b', 'UTF8'), 'hex')", connectionString));

        const string schema = "\"a\\b\"";
        var history = fixture.CreateHistory(schema);
        var preamble = CatalogGuardedSchemaPreambleTests.Preamble("a\\b", schema);
        File.WriteAllText(Path.Combine(history.MigrationsDirectory, "20990101000000_Backslash.sql"), preamble);
        var result = await fixture.Update(history, connectionString);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sable cannot safely wrap migration '20990101000000_Backslash'", result.Output);
        Assert.DoesNotContain("Successfully updated", result.Output);
        Assert.Equal(0L, await fixture.Query<long>("SELECT count(*) FROM pg_catalog.pg_namespace WHERE nspname IN (chr(97) || chr(8) || chr(98), chr(97) || chr(92) || chr(98))"));

        // A normal preamble remains usable under the same session setting.
        var safe = fixture.CreateHistory(Schema);
        File.WriteAllText(Path.Combine(safe.MigrationsDirectory, "20990102000000_Safe.sql"),
            CatalogGuardedSchemaPreambleTests.Preamble(Schema, Schema) + $"\nCREATE TABLE {Schema}.literal_probe (id integer);");
        await SetupSchemaAndHistory(fixture);
        (await fixture.Update(safe, connectionString)).AssertUpdated();
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{Schema}.literal_probe') IS NOT NULL"));
        Assert.Equal(1L, await fixture.Query<long>($"SELECT count(*) FROM {Schema}.__sable_migrations"));
    }

    private static void AddNonIdempotentProbe(SchemaPreambleCliFixture.HistoryProject history)
    {
        File.WriteAllText(Path.Combine(history.MigrationsDirectory, "20990101000000_TestReplayProbe.sql"),
            $"-- Handwritten non-idempotent test probe; not a generated sample artifact\nCREATE TABLE {Schema}.replay_probe (id integer);");
    }

    private static async Task SetupSchemaAndHistory(SchemaPreambleCliFixture fixture, string schemaIdentifier = Schema)
    {
        await fixture.ExecuteSetup($"""
            CREATE SCHEMA {schemaIdentifier};
            CREATE TABLE {schemaIdentifier}.__sable_migrations (
                migration_id varchar(150) PRIMARY KEY,
                date_applied timestamp NOT NULL DEFAULT (now() at time zone 'utc'),
                backfilled boolean NOT NULL DEFAULT false
            );
            """);
    }

    private static async Task<string> CreateRestrictedRole(SchemaPreambleCliFixture fixture)
    {
        var password = Guid.NewGuid().ToString("N");
        var connectionString = fixture.ConnectionForRole(Role, password);
        await fixture.ExecuteSetup($"CREATE ROLE {Role} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT PASSWORD '{password}'; REVOKE CREATE ON DATABASE {fixture.DatabaseName} FROM PUBLIC; REVOKE CREATE ON DATABASE {fixture.DatabaseName} FROM {Role};");
        return connectionString;
    }

    private static async Task AssertRestrictedRole(SchemaPreambleCliFixture fixture, string connectionString)
    {
        Assert.False(await fixture.Query<bool>("SELECT rolsuper FROM pg_roles WHERE rolname = current_user", connectionString));
        Assert.False(await fixture.Query<bool>("SELECT datdba = (SELECT oid FROM pg_roles WHERE rolname = current_user) FROM pg_database WHERE datname = current_database()", connectionString));
        Assert.False(await fixture.Query<bool>("SELECT has_database_privilege(current_user, current_database(), 'CREATE')", connectionString));
    }

    private static async Task AssertHistoryRecordedOnce(SchemaPreambleCliFixture fixture, SchemaPreambleCliFixture.HistoryProject history)
    {
        var ids = Directory.EnumerateFiles(history.MigrationsDirectory, "*.sql")
            .Select(Path.GetFileNameWithoutExtension).ToList();
        foreach (var id in ids)
        {
            Assert.Equal(1L, await fixture.Query<long>($"SELECT count(*) FROM {Schema}.__sable_migrations WHERE migration_id = '{id}'"));
        }
        Assert.Equal((long)ids.Count, await fixture.Query<long>($"SELECT count(*) FROM {Schema}.__sable_migrations"));
    }

    private sealed class CatalogPostgresFactAttribute : FactAttribute
    {
        public CatalogPostgresFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("SABLE_RUN_CATALOG_GUARDED_POSTGRES"), "1", StringComparison.Ordinal))
            {
                Skip = "Set SABLE_RUN_CATALOG_GUARDED_POSTGRES=1 for owned disposable PostgreSQL verification.";
            }
        }
    }
}
