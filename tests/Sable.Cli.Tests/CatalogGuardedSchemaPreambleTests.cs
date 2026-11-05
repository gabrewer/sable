// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using Xunit;

namespace Sable.Cli.Tests;

public class CatalogGuardedSchemaPreambleTests
{
    [Theory]
    [InlineData("sample", "sample", "sample")]
    [InlineData("SAMPLE", "sample", "SAMPLE")]
    [InlineData("\"Sample\"", "Sample", "\"Sample\"")]
    [InlineData("\"a\"\"b'c\"", "a\"b'c", "\"a\"\"b'c\"")]
    public void MatchingSchemaNamesPreserveOriginalPreamble(
        string configuredSchema,
        string catalogName,
        string createIdentifier
    )
    {
        AssertComposition(Preamble(catalogName, createIdentifier), configuredSchema);
    }

    [Theory]
    [InlineData("authentic")]
    [InlineData("comments")]
    [InlineData("tagged")]
    [InlineData("language")]
    [InlineData("semicolon")]
    [InlineData("handlers")]
    [InlineData("casing")]
    public void SupportedFormattingPreservesOriginalPreamble(string variant)
    {
        var preamble = Preamble();
        preamble = variant switch
        {
            "comments" => preamble.Replace(
                "SELECT 1 FROM",
                "SELECT /* outer /* nested */ comment */ 1 -- catalog\n FROM"
            ),
            "tagged" => preamble.Replace("$$", "$schema_1$"),
            "language" => preamble.Replace("DO $$", "DO LANGUAGE plpgsql $schema$")
                .Replace("\n$$;", "\n$schema$;"),
            "semicolon" => preamble.Replace("\nEND\n", "\nEND;\n"),
            "handlers" => preamble.Replace("duplicate_schema", "temporary_handler")
                .Replace("unique_violation", "duplicate_schema")
                .Replace("temporary_handler", "unique_violation"),
            "casing" => preamble.Replace("BEGIN", "begin")
                .Replace("SELECT", "select")
                .Replace("CREATE SCHEMA IF NOT EXISTS", "create schema if not exists"),
            _ => preamble,
        };
        AssertComposition(preamble, "sample");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedConsecutivePreamblesAndLegacyEnvelopeRemainSupported(bool legacyEnvelope)
    {
        var oldException = """
            DO $old$
            BEGIN
                BEGIN
                    EXECUTE 'CREATE SCHEMA IF NOT EXISTS sample';
                EXCEPTION
                    WHEN duplicate_schema THEN NULL;
                    WHEN unique_violation THEN NULL;
                END;
            END
            $old$;
            """;
        var oldConditional = """
            DO $legacy$
            BEGIN
                IF NOT EXISTS (SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'sample') THEN
                    EXECUTE 'CREATE SCHEMA sample';
                END IF;
            END
            $legacy$;
            """;
        var preambles = oldException + "\n" + Preamble() + "\n" + oldConditional;
        using var fixture = new CatalogGuardedMigrationFixture();
        var body = preambles + "\nCREATE TABLE sample.items (id integer);";
        var source = legacyEnvelope
            ? "DO LANGUAGE plpgsql $tran$\nBEGIN\n" + body + "\nEND;\n$tran$;"
            : body;
        var composed = fixture.Create(source).GetTransactionalIdempotentScript();
        Assert.Contains(preambles, composed, StringComparison.Ordinal);
        Assert.DoesNotContain("$tran$", composed);
        Assert.True(composed.IndexOf(preambles, StringComparison.Ordinal) < GuardIndex(composed));
        Assert.True(composed.IndexOf("CREATE TABLE", StringComparison.Ordinal) > GuardIndex(composed));
    }

    [Fact]
    public void NoTransactionDirectiveOnlyRemovesOuterTransaction()
    {
        using var fixture = new CatalogGuardedMigrationFixture();
        var preamble = Preamble();
        var composed = fixture.Create("-- Sable NoTransactionWrapper\n" + preamble)
            .GetTransactionalIdempotentScript();
        Assert.Contains(preamble, composed, StringComparison.Ordinal);
        Assert.DoesNotContain("COMMIT;", composed);
        Assert.DoesNotContain("BEGIN;", composed);
        Assert.True(GuardIndex(composed) > composed.IndexOf(preamble, StringComparison.Ordinal));
    }

    [Fact]
    public void BackfillKeepsExistingHistoryOnlySemantics()
    {
        using var fixture = new CatalogGuardedMigrationFixture();
        var script = fixture.Create(Preamble()).GetIdempotentMigrationRecordInsertionScript(true);
        Assert.DoesNotContain("pg_namespace", script);
        Assert.Contains("INSERT INTO sample.__sable_migrations", script);
        Assert.Contains("'1'", script);
    }

    internal static string Preamble(string catalogName = "sample", string createIdentifier = "sample")
    {
        return $$"""
            DO $$
            BEGIN      IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = '{{catalogName.Replace("'", "''")}}') THEN
                    BEGIN
                      EXECUTE 'CREATE SCHEMA IF NOT EXISTS {{createIdentifier.Replace("'", "''")}}';
                    EXCEPTION
                      WHEN duplicate_schema THEN NULL;
                      WHEN unique_violation THEN NULL;
                    END;
                  END IF;

            END
            $$;
            """.ReplaceLineEndings("\n");
    }

    private static void AssertComposition(string preamble, string schema)
    {
        using var fixture = new CatalogGuardedMigrationFixture();
        var ddl = $"CREATE TABLE {schema}.items (id integer);\n"
            + $"CREATE OR REPLACE FUNCTION {schema}.answer() RETURNS integer LANGUAGE sql AS $$ SELECT 42; $$;";
        var migration = fixture.Create("-- Generated by Sable\n" + preamble + "\n" + ddl, schema);
        var composed = migration.GetTransactionalIdempotentScript();
        var preambleIndex = composed.IndexOf(preamble, StringComparison.Ordinal);
        var guardIndex = GuardIndex(composed);
        Assert.True(preambleIndex > composed.IndexOf("BEGIN;", StringComparison.Ordinal));
        Assert.True(guardIndex > preambleIndex);
        Assert.Equal(1, composed.Split(preamble, StringSplitOptions.None).Length - 1);
        foreach (var statement in ddl.Split('\n'))
        {
            Assert.True(composed.IndexOf(statement, StringComparison.Ordinal) > guardIndex);
        }
        Assert.True(composed.IndexOf("INSERT INTO", StringComparison.Ordinal) > guardIndex);
        Assert.True(composed.LastIndexOf("COMMIT;", StringComparison.Ordinal) > guardIndex);
        Assert.Contains("AS $$ SELECT 42; $$;", composed);
    }

    private static int GuardIndex(string script)
    {
        var index = script.IndexOf("DO $sable$", StringComparison.Ordinal);
        Assert.True(index >= 0);
        return index;
    }
}

internal sealed class CatalogGuardedMigrationFixture : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sable-catalog-tests-{Guid.NewGuid():N}"
    );

    public Migration Create(string script, string schema = "sample")
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "20261104000000_CatalogGuard.sql");
        File.WriteAllText(file, script);
        return new Migration(file, schema);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
