// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Diagnostics;
using Xunit;

namespace Sable.Cli.Tests;

public class CommandExitTests : IDisposable
{
    private const string ExpectedUnsupportedMessage =
        "Sable cannot safely wrap migration '20260710000000_Unsupported' because it begins with an unsupported top-level DO statement. Only a recognized safe schema-creation preamble can be moved outside the idempotence guard. Use Sable's no-idempotence workflow only when the migration is independently idempotent.";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sable-command-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task UnsupportedTopLevelDoReturnsOneWithoutWritingAScript()
    {
        var projectFile = CreateProjectWithUnsupportedMigration();
        var outputFile = Path.Combine(_directory, "output", "migration.sql");
        var cliAssembly = typeof(Migration).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(cliAssembly);
        startInfo.ArgumentList.Add("migrations");
        startInfo.ArgumentList.Add("script");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectFile);
        startInfo.ArgumentList.Add("--database");
        startInfo.ArgumentList.Add("Test");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputFile);

        using var process = Process.Start(startInfo)!;
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = standardOutput + standardError;

        Assert.Equal(1, process.ExitCode);
        Assert.Contains(ExpectedUnsupportedMessage, output);
        Assert.DoesNotContain("Successfully saved migration script", output);
        Assert.False(File.Exists(outputFile));
    }

    [Theory]
    [InlineData("script")]
    [InlineData("backfill")]
    public async Task CatalogGuardedHistorySucceedsAtCommandBoundary(string command)
    {
        var project = CreateProjectWithUnsupportedMigration();
        var source = CatalogGuardedSchemaPreambleTests.Preamble()
            + "\nCREATE TABLE sample.items (id integer);";
        File.WriteAllText(MigrationPath(), source);
        var outputFile = Path.Combine(_directory, "result.sql");
        var result = await RunCompositionCommand(command, project, outputFile);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Successfully saved", result.Output);
        var output = await File.ReadAllTextAsync(outputFile);
        if (command == "script")
        {
            Assert.Contains(CatalogGuardedSchemaPreambleTests.Preamble(), output, StringComparison.Ordinal);
            Assert.True(
                output.IndexOf("pg_catalog.pg_namespace", StringComparison.Ordinal)
                    < output.IndexOf("DO $sable$", StringComparison.Ordinal)
            );
        }
        else
        {
            Assert.DoesNotContain("pg_catalog.pg_namespace", output);
            Assert.Contains("INSERT INTO sample.__sable_migrations", output);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedLaterMigrationDoesNotCreateOrOverwriteOutput(bool existingOutput)
    {
        var project = CreateProjectWithUnsupportedMigration();
        File.WriteAllText(MigrationPath(), "CREATE TABLE sample.first_item (id integer);");
        var invalidFile = Path.Combine(Path.GetDirectoryName(MigrationPath())!, "20261104000000_Rejected.sql");
        File.WriteAllText(
            invalidFile,
            CatalogGuardedSchemaPreambleTests.Preamble().Replace("nspname = 'sample'", "nspname = 'other'")
        );
        var outputFile = Path.Combine(_directory, "result.sql");
        var originalBytes = new byte[] { 0xEF, 0xBB, 0xBF, 0x41, 0x0D, 0x0A };
        if (existingOutput)
        {
            await File.WriteAllBytesAsync(outputFile, originalBytes);
        }
        var result = await RunCompositionCommand("script", project, outputFile);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sable cannot safely wrap migration '20261104000000_Rejected'", result.Output);
        Assert.DoesNotContain("Successfully saved", result.Output);
        if (existingOutput)
        {
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(outputFile));
        }
        else
        {
            Assert.False(File.Exists(outputFile));
        }
    }

    private string MigrationPath()
    {
        return Path.Combine(_directory, "sable", "Test", "migrations", "20260710000000_Unsupported.sql");
    }

    private static async Task<(int ExitCode, string Output)> RunCompositionCommand(
        string command,
        string project,
        string output
    )
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
        {
            typeof(Migration).Assembly.Location, "migrations", command, "--project", project,
            "--database", "Test", "--output", output,
        })
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Composition command exceeded 30 seconds.");
        }
        return (process.ExitCode, (await stdout) + (await stderr));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private string CreateProjectWithUnsupportedMigration()
    {
        Directory.CreateDirectory(_directory);
        var projectFile = Path.Combine(_directory, "TestProject.csproj");
        File.WriteAllText(
            projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"
        );

        var databaseDirectory = Path.Combine(_directory, "sable", "Test");
        var migrationsDirectory = Path.Combine(databaseDirectory, "migrations");
        Directory.CreateDirectory(migrationsDirectory);
        File.WriteAllText(Path.Combine(databaseDirectory, "schema.txt"), "sample");
        File.WriteAllText(
            Path.Combine(migrationsDirectory, "20260710000000_Unsupported.sql"),
            """
            DO $$
            BEGIN
                RAISE NOTICE 'unsupported';
            END
            $$;
            """
        );

        return projectFile;
    }
}
