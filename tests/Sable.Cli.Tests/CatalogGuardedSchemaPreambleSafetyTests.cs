// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

using Xunit;

namespace Sable.Cli.Tests;

public class CatalogGuardedSchemaPreambleSafetyTests
{
    [Theory]
    [InlineData("sample", "SAMPLE", "sample")]
    [InlineData("\"Sample\"", "sample", "\"Sample\"")]
    [InlineData("\"Sample\"", "Sample", "Sample")]
    [InlineData("sample", "\"sample\"", "sample")]
    [InlineData("sample", "other", "sample")]
    [InlineData("sample", "sample", "other")]
    [InlineData("other", "sample", "sample")]
    public void SchemaMismatchFailsWithExistingDiagnostic(
        string configuredSchema,
        string catalogName,
        string createIdentifier
    )
    {
        AssertRejected(CatalogGuardedSchemaPreambleTests.Preamble(catalogName, createIdentifier), configuredSchema);
    }

    [Theory]
    [MemberData(nameof(UnsupportedScripts))]
    public void UnsupportedGuardedLookingBlocksRemainRejected(string source)
    {
        AssertRejected(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoIdempotenceDirectivePreservesOriginalSqlAndConditionalHistory(bool noTransaction)
    {
        var source = "-- Sable NoIdempotenceWrapper\n"
            + (noTransaction ? "-- Sable NoTransactionWrapper\n" : "")
            + CatalogGuardedSchemaPreambleTests.Preamble().Replace("NULL;", "RAISE NOTICE 'unclassified';");
        using var fixture = new CatalogGuardedMigrationFixture();
        var composed = fixture.Create(source).GetTransactionalIdempotentScript();
        Assert.Contains(source, composed, StringComparison.Ordinal);
        Assert.DoesNotContain("RAISE NOTICE 'Running migration", composed);
        Assert.Contains("RAISE NOTICE 'Inserting record for migration", composed);
        Assert.Equal(!noTransaction, composed.Contains("COMMIT;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogValueCannotNameAnOverlongPostgresSchema(bool multibyte)
    {
        var schema = multibyte ? new string('é', 32) : new string('s', 64);
        AssertRejected(CatalogGuardedSchemaPreambleTests.Preamble(schema, "\"" + schema + "\""), "\"" + schema + "\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatalogValueAtPostgresNameByteBoundaryIsAccepted(bool multibyte)
    {
        var schema = multibyte ? new string('é', 31) + "s" : new string('s', 63);
        var identifier = "\"" + schema + "\"";
        var preamble = CatalogGuardedSchemaPreambleTests.Preamble(schema, identifier);
        using var fixture = new CatalogGuardedMigrationFixture();
        Assert.Contains(preamble, fixture.Create(preamble, identifier).GetIdempotentScript(), StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogValueWithNulIsRejected()
    {
        AssertRejected(CatalogGuardedSchemaPreambleTests.Preamble("a\0b", "\"a\0b\""), "\"a\0b\"");
    }

    [Theory]
    [InlineData("ÉXAMPLE", "Éxample")]
    [InlineData("ÅBC", "Åbc")]
    public void UnquotedUtf8IdentifierFoldingDoesNotLowercaseCatalogValues(string identifier, string catalog)
    {
        using var fixture = new CatalogGuardedMigrationFixture();
        var preamble = CatalogGuardedSchemaPreambleTests.Preamble(catalog, identifier);
        Assert.Contains(preamble, fixture.Create(preamble, identifier).GetIdempotentScript(), StringComparison.Ordinal);
        AssertRejected(CatalogGuardedSchemaPreambleTests.Preamble(catalog.ToLowerInvariant(), identifier), identifier);
    }

    public static IEnumerable<object[]> UnsupportedScripts()
    {
        var safe = CatalogGuardedSchemaPreambleTests.Preamble();
        foreach (var (oldText, newText) in new (string, string)[]
        {
            ("SELECT 1 FROM", "SELECT 10 FROM"),
            ("SELECT 1 FROM", "SELECT * FROM"),
            ("pg_catalog.pg_namespace", "information_schema.schemata"),
            ("pg_catalog.pg_namespace", "pg_namespace"),
            ("pg_catalog.pg_namespace", "pg_catalog.pg_namespace AS n"),
            ("nspname =", "schema_name ="),
            ("nspname =", "nspname <>"),
            ("'sample') THEN", "'sample' OR true) THEN"),
            ("'sample') THEN", "'sample' AND true) THEN"),
            ("BEGIN      IF", "BEGIN PERFORM 1; IF"),
            ("EXECUTE 'CREATE", "PERFORM 1; EXECUTE 'CREATE"),
            ("sample';", "sample; DELETE FROM sample.items';"),
            ("sample';", "sample'; EXECUTE 'DROP SCHEMA sample CASCADE';"),
            ("sample';", "sample AUTHORIZATION current_user';"),
            ("sample';", "sample /* unterminated';"),
            ("WHEN unique_violation THEN NULL;", ""),
            ("WHEN unique_violation", "WHEN duplicate_schema"),
            ("WHEN unique_violation", "WHEN OTHERS"),
            ("WHEN unique_violation", "WHEN duplicate_schema OR unique_violation"),
            ("THEN NULL;", "THEN DELETE FROM sample.items;"),
            ("THEN NULL;", "THEN NULL; PERFORM 1;"),
            ("WHEN unique_violation THEN NULL;", "WHEN unique_violation THEN NULL; WHEN OTHERS THEN NULL;"),
            ("THEN NULL;", "THEN NULL"),
            ("END IF;", "END IF; DELETE FROM sample.items;"),
            ("END IF;", "END;"),
            ("END IF;", "END IF"),
            ("END\n$$;", "END /* unterminated\n$$;"),
            ("'sample')", "'sample)"),
            ("\n$$;", "\n$other$;"),
            ("\n$$;", "\n$$"),
            ("\n$$;", "\n$$ LANGUAGE plpgsql;"),
            ("DO $$", "DO LANGUAGE sql $$"),
        })
        {
            yield return [safe.Replace(oldText, newText, StringComparison.Ordinal)];
        }
        yield return [safe + "\nDO $$ BEGIN DELETE FROM sample.items; END $$;"];
        yield return [safe + "\n" + safe.Replace("nspname = 'sample'", "nspname = 'other'")];
        yield return ["DO $$ BEGIN RAISE NOTICE 'pg_catalog.pg_namespace CREATE SCHEMA sample'; END $$;"];
        yield return ["DO $$ BEGIN /* " + safe.Replace("$$", "$quoted$") + " */ DELETE FROM sample.items; END $$;"];
    }

    private static void AssertRejected(string source, string schema = "sample")
    {
        using var fixture = new CatalogGuardedMigrationFixture();
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Create(source, schema));
        Assert.Equal(
            "Sable cannot safely wrap migration '20261104000000_CatalogGuard' because it begins with an unsupported top-level DO statement. Only a recognized safe schema-creation preamble can be moved outside the idempotence guard. Use Sable's no-idempotence workflow only when the migration is independently idempotent.",
            error.Message
        );
    }
}
