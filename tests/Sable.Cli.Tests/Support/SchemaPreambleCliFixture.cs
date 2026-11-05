// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Diagnostics;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Xunit;

namespace Sable.Cli.Tests.Support;

internal sealed class SchemaPreambleCliFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sable-catalog-cli-{Guid.NewGuid():N}");
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly List<string> _secrets = [];
    private readonly IContainer _container;

    internal static string VerificationRunId { get; } =
        Environment.GetEnvironmentVariable("SABLE_SCHEMA_PREAMBLE_VERIFICATION_RUN_ID") ?? Guid.NewGuid().ToString("N");

    public SchemaPreambleCliFixture()
    {
        DatabaseName = $"sable_catalog_{Guid.NewGuid():N}";
        _container = new ContainerBuilder("postgres:15.1")
            .WithEnvironment("POSTGRES_DB", DatabaseName)
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", _password)
            .WithPortBinding(5432, true)
            .WithLabel("sable-verification-owner", DatabaseName)
            .WithLabel("sable-verification-run", VerificationRunId)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .WithCleanUp(true)
            .Build();
    }

    public string DatabaseName { get; }
    public string ConnectionString { get; private set; }

    public async Task Start()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await _container.StartAsync(timeout.Token);
        Assert.True(_container.Hostname is "localhost" or "127.0.0.1" or "::1", "Only a local disposable target is permitted.");
        ConnectionString = new NpgsqlConnectionStringBuilder
        {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Username = "postgres",
            Password = _password,
            Database = DatabaseName,
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 30,
        }.ConnectionString;
        _secrets.AddRange([ConnectionString, _password]);
        using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                await Query<int>("SELECT 1", cancellationToken: ready.Token);
                break;
            }
            catch (NpgsqlException) when (!ready.IsCancellationRequested)
            {
                await Task.Delay(200, ready.Token);
            }
        }
    }

    public string ConnectionForRole(string role, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Username = role, Password = password };
        var value = builder.ConnectionString;
        _secrets.AddRange([value, password]);
        return value;
    }

    public HistoryProject CopySampleHistory(bool includeInfrastructure = true)
    {
        var project = CatalogGuardedSchemaPreambleSampleCompositionTests.ProjectFile();
        var history = CreateHistory(CatalogGuardedSchemaPreambleSampleCompositionTests.Schema);
        var source = Path.Combine(Path.GetDirectoryName(project)!, "sable", "Marten", "migrations");
        foreach (var file in Directory.EnumerateFiles(source, "*.sql"))
        {
            if (includeInfrastructure || !file.EndsWith("_InfrastructureSetup.sql", StringComparison.Ordinal))
            {
                File.Copy(file, Path.Combine(history.MigrationsDirectory, Path.GetFileName(file)));
            }
        }
        return history;
    }

    public HistoryProject CreateHistory(string schema)
    {
        var directory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var migrations = Path.Combine(directory, "sable", "Marten", "migrations");
        Directory.CreateDirectory(migrations);
        var project = Path.Combine(directory, "Fixture.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "sable", "Marten", "schema.txt"), schema);
        return new HistoryProject(project, migrations, schema);
    }

    public Task<CliResult> Update(HistoryProject history, string connectionString = null)
    {
        return RunCli(
            ["database", "update", connectionString ?? ConnectionString, "--project", history.ProjectFile, "--database", SableCliConstants.DefaultDatabaseName],
            _secrets
        );
    }

    // Only handwritten fixture prerequisites/control SQL belong here. Generated
    // migration content is always applied by Update/RunCli, never by Npgsql.
    public async Task ExecuteSetup(string sql, string connectionString = null)
    {
        await using var connection = new NpgsqlConnection(connectionString ?? ConnectionString);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(timeout.Token);
    }

    public async Task<T> Query<T>(string sql, string connectionString = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString ?? ConnectionString);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(timeout.Token))!;
    }

    public static async Task<CliResult> RunCli(string[] arguments, IEnumerable<string> secrets)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(typeof(Migration).Assembly.Location);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment.Remove(SableConstants.ConnectionStringOverride);
        start.Environment.Remove("SABLE_SAMPLE_INCLUDE_PENDING_CHANGE");
        start.Environment.Remove("SABLE_CATALOG_GUARDED_SAMPLE_INCLUDE_PENDING_CHANGE");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Owned CLI verification process exceeded three minutes.");
        }
        var output = (await stdout) + (await stderr);
        foreach (var secret in secrets.OrderByDescending(value => value.Length))
        {
            output = output.Replace(secret, "<redacted>", StringComparison.Ordinal);
        }
        return new CliResult(process.ExitCode, output);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
    }

    internal sealed record HistoryProject(string ProjectFile, string MigrationsDirectory, string Schema);

    internal sealed record CliResult(int ExitCode, string Output)
    {
        public void AssertUpdated()
        {
            Assert.True(ExitCode == 0, Output);
            Assert.Contains("Successfully updated the database", Output);
        }
    }
}
