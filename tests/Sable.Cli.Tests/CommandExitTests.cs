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
