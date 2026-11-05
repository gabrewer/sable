// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using System.Security;
using System.Security.Cryptography;
using Sable.Cli.Tests.Support;
using Xunit;

namespace Sable.Cli.Tests;

public class CatalogGuardedSchemaPreambleEndToEndTests
{
    private static readonly IReadOnlyDictionary<string, string> PendingMapping =
        new Dictionary<string, string> { ["SABLE_CATALOG_GUARDED_SAMPLE_INCLUDE_PENDING_CHANGE"] = "1" };

    [CatalogEndToEndFact]
    [Trait("Category", "CatalogGuardedEndToEnd")]
    public async Task AddScriptsAndReplaysTheResultingHistoryOnAFreshTarget()
    {
        var originalDirectory = Path.GetDirectoryName(CatalogGuardedSchemaPreambleSampleCompositionTests.ProjectFile())!;
        var originalHashes = HashFiles(originalDirectory);
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var history = fixture.CopyRunnableSample();
        var baseline = HashFiles(history.MigrationsDirectory);
        var name = "PendingMapping" + Guid.NewGuid().ToString("N");
        var result = await fixture.Command(
            ["migrations", "add", name, "--project", history.ProjectFile,
                "--database", SableCliConstants.DefaultDatabaseName,
                "--container-options", fixture.CreateShadowOptions()],
            PendingMapping
        );
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Successfully saved migration", result.Output);
        Assert.DoesNotContain("42601", result.Output);
        var after = HashFiles(history.MigrationsDirectory);
        var delta = Assert.Single(after.Keys.Except(baseline.Keys));
        Assert.EndsWith("_" + name + ".sql", delta);
        Assert.Contains("mt_doc_pendingcatalogitem", File.ReadAllText(Path.Combine(history.MigrationsDirectory, delta)), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.EnumerateFiles(history.MigrationsDirectory, "*.drop.sql").Any());
        foreach (var (file, hash) in baseline)
        {
            Assert.Equal(hash, after[file]);
        }

        var output = fixture.TemporaryFile("complete-history.sql");
        var script = await fixture.Command(
            ["migrations", "script", "--project", history.ProjectFile,
                "--database", SableCliConstants.DefaultDatabaseName, "--output", output]
        );
        Assert.True(script.ExitCode == 0, script.Output);
        Assert.Contains("pg_catalog.pg_namespace", await File.ReadAllTextAsync(output));
        Assert.Equal(0L, await fixture.Query<long>(
            $"SELECT count(*) FROM pg_catalog.pg_namespace WHERE nspname = '{history.Schema}'"
        ));
        (await fixture.Update(history)).AssertUpdated();
        (await fixture.Update(history)).AssertUpdated();
        Assert.True(await fixture.Query<bool>($"SELECT to_regclass('{history.Schema}.mt_doc_pendingcatalogitem') IS NOT NULL"));
        foreach (var file in after.Keys)
        {
            var id = Path.GetFileNameWithoutExtension(file);
            Assert.Equal(1L, await fixture.Query<long>($"SELECT count(*) FROM {history.Schema}.__sable_migrations WHERE migration_id = '{id}'"));
        }
        Assert.Equal((long)after.Count, await fixture.Query<long>($"SELECT count(*) FROM {history.Schema}.__sable_migrations"));
        Assert.Equal(originalHashes.OrderBy(pair => pair.Key), HashFiles(originalDirectory).OrderBy(pair => pair.Key));
    }

    [CatalogEndToEndFact]
    [Trait("Category", "CatalogGuardedEndToEnd")]
    public async Task RejectedHistoryDoesNotLaunchPatchOrCreateNewMigrationArtifacts()
    {
        var originalDirectory = Path.GetDirectoryName(CatalogGuardedSchemaPreambleSampleCompositionTests.ProjectFile())!;
        var originalHashes = HashFiles(originalDirectory);
        await using var fixture = new SchemaPreambleCliFixture();
        await fixture.Start();
        var history = fixture.CopyRunnableSample();
        File.WriteAllText(Path.Combine(history.MigrationsDirectory, "20990101000000_Rejected.sql"),
            "-- Handwritten adversarial input, not a producer artifact\nDO $$ BEGIN RAISE NOTICE 'unsupported'; END $$;");
        var baseline = HashFiles(history.MigrationsDirectory);
        var marker = Path.Combine(Path.GetDirectoryName(history.ProjectFile)!, "unexpected-patch-build.txt");
        var project = await File.ReadAllTextAsync(history.ProjectFile);
        project = project.Replace("</Project>",
            $"<Target Name=\"RecordUnexpectedPatchBuild\" BeforeTargets=\"Build\"><WriteLinesToFile File=\"{SecurityElement.Escape(marker)}\" Lines=\"unexpected patch build\" Overwrite=\"true\" /></Target></Project>",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(history.ProjectFile, project);
        var result = await fixture.Command(
            ["migrations", "add", "MustNotGenerate" + Guid.NewGuid().ToString("N"),
                "--project", history.ProjectFile, "--database", SableCliConstants.DefaultDatabaseName,
                "--container-options", fixture.CreateShadowOptions()],
            PendingMapping
        );
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Sable cannot safely wrap migration '20990101000000_Rejected'", result.Output);
        Assert.DoesNotContain("Successfully saved migration", result.Output);
        Assert.False(File.Exists(marker), "Rejected history must not reach the db-patch project build.");
        Assert.Equal(baseline.OrderBy(pair => pair.Key), HashFiles(history.MigrationsDirectory).OrderBy(pair => pair.Key));
        Assert.Equal(originalHashes.OrderBy(pair => pair.Key), HashFiles(originalDirectory).OrderBy(pair => pair.Key));
    }

    private static Dictionary<string, string> HashFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToDictionary(file => Path.GetRelativePath(root, file), file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
    }

    private sealed class CatalogEndToEndFactAttribute : FactAttribute
    {
        public CatalogEndToEndFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("SABLE_RUN_CATALOG_GUARDED_E2E"), "1", StringComparison.Ordinal))
            {
                Skip = "Set SABLE_RUN_CATALOG_GUARDED_E2E=1 for owned temporary-copy end-to-end verification.";
            }
        }
    }
}
