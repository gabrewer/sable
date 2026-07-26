// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace Sable.Cli.Tests;

public class SchemaPreambleSampleCompositionTests
{
    private const string SampleDirectoryName = "Sable.Samples.SchemaPreamble";
    private const string SchemaName = "schema_preamble_sample";

    [Fact]
    public void GeneratedSampleMigrationIsComposedWithPreambleBeforeGuard()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sampleDirectory = Path.Combine(repositoryRoot, "samples", SampleDirectoryName);
        Assert.True(
            Directory.Exists(sampleDirectory),
            $"The runnable sample directory '{sampleDirectory}' has not been created."
        );

        var projectFile = Path.Combine(sampleDirectory, $"{SampleDirectoryName}.csproj");
        var databaseDirectory = Path.Combine(sampleDirectory, "sable", "Marten");
        var migrationsDirectory = Path.Combine(databaseDirectory, "migrations");
        Assert.True(File.Exists(projectFile), $"The sample project '{projectFile}' is missing.");
        Assert.Equal(SchemaName, File.ReadAllText(Path.Combine(databaseDirectory, "schema.txt")));

        var representativeMigrationPath = Directory
            .EnumerateFiles(migrationsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith("_InfrastructureSetup.sql", StringComparison.Ordinal))
            .Single(path =>
            {
                var source = File.ReadAllText(path);
                return Regex.IsMatch(
                        source,
                        @"CREATE\s+OR\s+REPLACE\s+FUNCTION\b",
                        RegexOptions.IgnoreCase
                    )
                    && source.Contains(
                        $"CREATE SCHEMA IF NOT EXISTS {SchemaName}",
                        StringComparison.OrdinalIgnoreCase
                    );
            });
        var migration = new Migration(representativeMigrationPath, SchemaName);
        var source = migration.Script;
        var composed = migration.GetTransactionalIdempotentScript();
        var sourceDoIndex = Regex.Match(source, @"(?im)^\s*DO\b").Index;
        var sourceFunctionMatch = Regex.Match(
            source,
            @"(?im)^\s*CREATE\s+OR\s+REPLACE\s+FUNCTION\b"
        );
        Assert.True(sourceFunctionMatch.Success);
        var sourceFunctionIndex = sourceFunctionMatch.Index;
        var transactionIndex = composed.IndexOf("BEGIN;", StringComparison.Ordinal);
        var preambleIndex = composed.IndexOf(
            source[sourceDoIndex..sourceFunctionIndex].Trim(),
            StringComparison.Ordinal
        );
        var guardIndex = composed.IndexOf("DO $sable$", StringComparison.Ordinal);
        var composedFunctionMatch = Regex.Match(
            composed,
            @"(?im)^\s*CREATE\s+OR\s+REPLACE\s+FUNCTION\b"
        );
        Assert.True(composedFunctionMatch.Success);
        var functionIndex = composedFunctionMatch.Index;
        var historyIndex = composed.IndexOf(
            $"INSERT INTO {SchemaName}.__sable_migrations",
            StringComparison.Ordinal
        );
        var commitIndex = composed.LastIndexOf("COMMIT;", StringComparison.Ordinal);

        Assert.Matches(@"(?is)AS\s+\$[A-Za-z_][A-Za-z0-9_]*\$.*\$[A-Za-z_][A-Za-z0-9_]*\$", source);
        Assert.True(transactionIndex >= 0);
        Assert.True(preambleIndex > transactionIndex);
        Assert.True(guardIndex > preambleIndex);
        Assert.True(functionIndex > guardIndex);
        Assert.True(historyIndex > functionIndex);
        Assert.True(commitIndex > historyIndex);
    }

    [Fact]
    public void PendingMappingSeamIsExplicitAndDisabledByDefault()
    {
        var programFile = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            SampleDirectoryName,
            "Program.cs"
        );
        Assert.True(File.Exists(programFile), $"The sample Program.cs '{programFile}' is missing.");

        var source = File.ReadAllText(programFile);

        Assert.Contains("SABLE_SAMPLE_INCLUDE_PENDING_CHANGE", source);
        Assert.Contains("StringComparison.Ordinal", source);
        Assert.Contains("\"1\"", source);
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
}
