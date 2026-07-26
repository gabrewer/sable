// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Sable.Cli.Tests;

public class SchemaPreambleSampleEndToEndTests
{
    private const string EndToEndEnvironmentVariable = "SABLE_RUN_SAMPLE_E2E";
    private const string PendingChangeEnvironmentVariable =
        "SABLE_SAMPLE_INCLUDE_PENDING_CHANGE";

    [SampleEndToEndFact]
    [Trait("Category", "SampleEndToEnd")]
    public async Task MigrationsAddReplaysTheSampleAndGeneratesPendingMigration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceSample = Path.Combine(
            repositoryRoot,
            "samples",
            "Sable.Samples.SchemaPreamble"
        );
        var sourceHashes = HashSourceFiles(sourceSample);
        var temporarySample = Path.Combine(
            repositoryRoot,
            "samples",
            $".schema-preamble-e2e-{Guid.NewGuid():N}"
        );
        var password = Guid.NewGuid().ToString("N");
        var databaseName = $"sable_sample_e2e_{Guid.NewGuid():N}";
        var port = FindAvailablePort();
        var migrationName = $"PendingMapping{Guid.NewGuid():N}";

        try
        {
            CopySample(sourceSample, temporarySample);
            var projectFile = Path.Combine(
                temporarySample,
                "Sable.Samples.SchemaPreamble.csproj"
            );
            var migrationsDirectory = Path.Combine(
                temporarySample,
                "sable",
                "Marten",
                "migrations"
            );
            var baselineMigrations = Directory
                .EnumerateFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var optionsFile = Path.Combine(temporarySample, "container-options.json");
            await File.WriteAllTextAsync(
                optionsFile,
                JsonSerializer.Serialize(
                    new
                    {
                        Image = "postgres:15.1",
                        PortBindings = new[] { new { HostPort = port, ContainerPort = 5432 } },
                        EnvironmentVariables = new Dictionary<string, string>
                        {
                            ["PGPORT"] = "5432",
                            ["POSTGRES_DB"] = databaseName,
                            ["POSTGRES_USER"] = "postgres",
                            ["POSTGRES_PASSWORD"] = password,
                        },
                        ConnectionString =
                            $"Host=127.0.0.1;Port={port};Username=postgres;Password={password};Database={databaseName};Pooling=false",
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );

            var result = await RunSable(
                repositoryRoot,
                projectFile,
                optionsFile,
                migrationName,
                password
            );
            var currentMigrations = Directory
                .EnumerateFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .ToList();
            var generatedMigration = Assert.Single(
                currentMigrations,
                path => !baselineMigrations.Contains(path)
            );

            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Successfully saved migration", result.Output);
            Assert.DoesNotContain("42601", result.Output);
            Assert.EndsWith($"_{migrationName}.sql", generatedMigration);
            Assert.False(File.Exists(Path.ChangeExtension(generatedMigration, ".drop.sql")));
            Assert.Contains(
                "mt_doc_pendingcatalogitem",
                await File.ReadAllTextAsync(generatedMigration),
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(
                sourceHashes.OrderBy(pair => pair.Key),
                HashSourceFiles(sourceSample).OrderBy(pair => pair.Key)
            );
            Assert.False(await CanConnect(port));
        }
        finally
        {
            await RemoveOwnedPostgresContainers(databaseName);
            DeleteDirectory(temporarySample);
        }
    }

    private static async Task<ProcessResult> RunSable(
        string repositoryRoot,
        string projectFile,
        string optionsFile,
        string migrationName,
        string password
    )
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot,
        };
        startInfo.ArgumentList.Add(typeof(Migration).Assembly.Location);
        startInfo.ArgumentList.Add("migrations");
        startInfo.ArgumentList.Add("add");
        startInfo.ArgumentList.Add(migrationName);
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--database");
        startInfo.ArgumentList.Add(SableCliConstants.DefaultDatabaseName);
        startInfo.ArgumentList.Add("--container-options");
        startInfo.ArgumentList.Add(optionsFile);
        startInfo.Environment[PendingChangeEnvironmentVariable] = "1";
        startInfo.Environment.Remove(SableConstants.ConnectionStringOverride);

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The sample migrations-add process exceeded five minutes.");
        }

        var output = (await standardOutput) + (await standardError);
        return new ProcessResult(process.ExitCode, output.Replace(password, "<redacted>"));
    }

    private static async Task RemoveOwnedPostgresContainers(string databaseName)
    {
        var containers = await RunDocker("ps", "-aq", "--filter", "ancestor=postgres:15.1");
        Assert.True(containers.ExitCode == 0, containers.Output);
        foreach (
            var containerId in containers.Output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            var environment = await RunDocker(
                "inspect",
                "--format",
                "{{range .Config.Env}}{{println .}}{{end}}",
                containerId
            );
            if (
                environment.ExitCode != 0
                || !environment.Output.Split(['\r', '\n']).Contains($"POSTGRES_DB={databaseName}")
            )
            {
                continue;
            }

            var removal = await RunDocker("rm", "-f", containerId);
            if (removal.ExitCode != 0 && removal.Output.Contains("No such container"))
            {
                continue;
            }

            Assert.True(removal.ExitCode == 0, removal.Output);
        }
    }

    private static async Task<ProcessResult> RunDocker(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + (await standardError)
        );
    }

    private static void CopySample(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, directory);
            if (IsRuntimePath(relativePath))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(destination, relativePath));
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, file);
            if (IsRuntimePath(relativePath))
            {
                continue;
            }

            var target = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static bool IsRuntimePath(string relativePath)
    {
        return relativePath.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");
    }

    private static IReadOnlyDictionary<string, string> HashSourceFiles(string sampleDirectory)
    {
        return Directory
            .EnumerateFiles(sampleDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !IsRuntimePath(Path.GetRelativePath(sampleDirectory, path)))
            .ToDictionary(
                path => Path.GetRelativePath(sampleDirectory, path),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal
            );
    }

    private static int FindAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<bool> CanConnect(int port)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sable.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Sable repository root.");
    }

    private static void DeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(directory, true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(250);
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed class SampleEndToEndFactAttribute : FactAttribute
    {
        public SampleEndToEndFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(EndToEndEnvironmentVariable), "1"))
            {
                Skip =
                    $"Set {EndToEndEnvironmentVariable}=1 to run the sample end-to-end test.";
            }
        }
    }
}
