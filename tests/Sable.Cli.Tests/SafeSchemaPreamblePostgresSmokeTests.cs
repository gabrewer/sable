// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using DotNet.Testcontainers.Builders;
using Npgsql;
using Xunit;

namespace Sable.Cli.Tests;

public class SafeSchemaPreamblePostgresSmokeTests
{
    private const string SmokeEnvironmentVariable = "SABLE_RUN_POSTGRES_SMOKE";
    private const string SchemaName = "schema_preamble_sample";

    [PostgresSmokeFact]
    [Trait("Category", "PostgresSmoke")]
    public async Task ConsolidatedSampleMigrationReplaysTwiceInDisposablePostgres()
    {
        var databaseName = $"sable_smoke_{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        await using var container = new ContainerBuilder("postgres:15.1")
            .WithEnvironment("POSTGRES_DB", databaseName)
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", password)
            .WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .WithCleanUp(true)
            .Build();

        await container.StartAsync();
        var connectionString =
            $"Host=localhost;Port={container.GetMappedPublicPort(5432)};Username=postgres;Password={password};Database={databaseName};Pooling=false";
        await WaitUntilReady(connectionString);

        var sampleProject = FindSampleProject();
        var representativeMigrationId = FindRepresentativeMigrationId(sampleProject);
        var manager = new MartenMigrationManager(new NullConsoleLogger());
        var script = await manager.CreateMigrationScript(
            sampleProject,
            SableCliConstants.DefaultDatabaseName
        );

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await Execute(dataSource, script);
        await Execute(dataSource, script);

        Assert.Equal(
            "2026-07-26 12:34:56",
            await Query<string>(
                dataSource,
                $"SELECT {SchemaName}.mt_immutable_timestamp('2026-07-26 12:34:56')::text;"
            )
        );
        Assert.Equal(
            "mt_doc_catalogitem",
            await Query<string>(
                dataSource,
                $"SELECT table_name FROM information_schema.tables WHERE table_schema = '{SchemaName}' AND table_name = 'mt_doc_catalogitem';"
            )
        );
        Assert.Equal(
            1L,
            await Query<long>(
                dataSource,
                $"SELECT count(*) FROM {SchemaName}.__sable_migrations WHERE migration_id = '{representativeMigrationId}';"
            )
        );
    }

    private static string FindSampleProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var project = Path.Combine(
                directory.FullName,
                "samples",
                "Sable.Samples.SchemaPreamble",
                "Sable.Samples.SchemaPreamble.csproj"
            );
            if (File.Exists(project))
            {
                return project;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the schema preamble sample project.");
    }

    private static string FindRepresentativeMigrationId(string sampleProject)
    {
        var migrationsDirectory = Path.Combine(
            Path.GetDirectoryName(sampleProject)!,
            "sable",
            "Marten",
            "migrations"
        );
        var migration = Directory
            .EnumerateFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Single(path => !path.EndsWith("_InfrastructureSetup.sql", StringComparison.Ordinal));
        return Path.GetFileNameWithoutExtension(migration);
    }

    private static async Task WaitUntilReady(string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(timeout.Token);
                return;
            }
            catch (Exception) when (!timeout.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
            }
        }
    }

    private static async Task Execute(NpgsqlDataSource dataSource, string script)
    {
        await using var command = dataSource.CreateCommand(script);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Query<T>(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed class NullConsoleLogger : IConsoleLogger
    {
        public void LogInfo(string message) { }

        public void LogError(string message) { }
    }

    private sealed class PostgresSmokeFactAttribute : FactAttribute
    {
        public PostgresSmokeFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(SmokeEnvironmentVariable), "1"))
            {
                Skip =
                    $"Set {SmokeEnvironmentVariable}=1 to run the disposable PostgreSQL smoke test.";
            }
        }
    }
}
