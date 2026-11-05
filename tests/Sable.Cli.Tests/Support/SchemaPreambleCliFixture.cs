// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using Xunit;

namespace Sable.Cli.Tests.Support;

internal sealed class SchemaPreambleCliFixture : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"sable-catalog-cli-{VerificationRunId}-{Guid.NewGuid():N}");
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly List<string> _secrets = [];
    private readonly List<string> _sampleCopies = [];
    private readonly HashSet<string> _shadowDatabases = [];
    private readonly IContainer _container;

    internal static string VerificationRunId { get; } = ReadVerificationRunId();

    private static string ReadVerificationRunId()
    {
        var value = Environment.GetEnvironmentVariable("SABLE_SCHEMA_PREAMBLE_VERIFICATION_RUN_ID");
        if (value is null)
        {
            return Guid.NewGuid().ToString("N");
        }
        if (!Guid.TryParseExact(value, "N", out var id))
        {
            throw new InvalidOperationException("Verification run identity must be a GUID in N format.");
        }
        return id.ToString("N");
    }

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

    public HistoryProject CopyRunnableSample()
    {
        var sourceProject = CatalogGuardedSchemaPreambleSampleCompositionTests.ProjectFile();
        var source = Path.GetDirectoryName(sourceProject)!;
        var directory = Path.Combine(Path.GetDirectoryName(source)!, $".catalog-guarded-e2e-{VerificationRunId}-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(directory));
        _sampleCopies.Add(directory);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                continue;
            }
            var destination = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return new HistoryProject(
            Path.Combine(directory, Path.GetFileName(sourceProject)),
            Path.Combine(directory, "sable", "Marten", "migrations"),
            CatalogGuardedSchemaPreambleSampleCompositionTests.Schema
        );
    }

    public string CreateShadowOptions()
    {
        var database = $"sable_shadow_{Guid.NewGuid():N}";
        _shadowDatabases.Add(database);
        var password = Guid.NewGuid().ToString("N");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var connection = $"Host=127.0.0.1;Port={port};Username=postgres;Password={password};Database={database};Pooling=false";
        _secrets.AddRange([connection, password]);
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, $"shadow-{database}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            Image = "postgres:15.1",
            PortBindings = new[] { new { HostPort = port, ContainerPort = 5432 } },
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["PGPORT"] = "5432",
                ["POSTGRES_DB"] = database,
                ["POSTGRES_USER"] = "postgres",
                ["POSTGRES_PASSWORD"] = password,
                ["SABLE_VERIFICATION_RUN_ID"] = VerificationRunId,
            },
            ConnectionString = connection,
        }));
        return file;
    }

    public string TemporaryFile(string name)
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, name);
    }

    public Task<CliResult> Command(string[] arguments, IReadOnlyDictionary<string, string> environment = null)
    {
        return RunCli(arguments, _secrets, environment);
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

    public static async Task<CliResult> RunCli(
        string[] arguments,
        IEnumerable<string> secrets,
        IReadOnlyDictionary<string, string> environment = null
    )
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
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                start.Environment[name] = value;
            }
        }
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

    private async Task RemoveOwnedShadowContainers()
    {
        if (_shadowDatabases.Count == 0)
        {
            return;
        }
        var ids = await RunDocker(["ps", "-aq", "--filter", "ancestor=postgres:15.1"]);
        foreach (var id in ids.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var environment = await RunDocker(["inspect", "--format", "{{range .Config.Env}}{{println .}}{{end}}", id]);
            if (environment.Split(['\r', '\n']).Any(line => _shadowDatabases.Any(database => line == "POSTGRES_DB=" + database)))
            {
                await RunDocker(["rm", "-f", id]);
            }
        }
    }

    private static async Task<string> RunDocker(string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Owned Docker cleanup timed out.");
        }
        await error;
        Assert.True(process.ExitCode == 0, "Could not inspect or clean owned shadow resources.");
        return await output;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await RemoveOwnedShadowContainers();
        }
        finally
        {
            try
            {
                await _container.DisposeAsync();
            }
            finally
            {
                foreach (var directory in _sampleCopies.Append(_directory))
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }
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
