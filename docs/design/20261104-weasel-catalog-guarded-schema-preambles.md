# Product Requirements Document: Catalog-Guarded Weasel Schema Preambles

**Status:** Draft for human review; implementation not started
**State backend for subsequent sprint:** github-issues
**Repository:** `gabrewer/sable`
**Worktree:** `X:/source/sable/wt/catalog-guarded-schema-preambles`
**Documentation branch:** `docs/catalog-guarded-schema-preambles`
**Base revision:** `a615c5531f51dde676477671b11328dcf34730b7` (`origin/main`)
**Observed published CLI:** `Sable.Cli 0.0.6`
**Originating consumer:** Lessi.App CritterWatch upgrade and fleet migration validation
**Related designs:** [Safe top-level schema preambles](20260711-safe-top-level-schema-do-preambles.md), [Executable compatibility sample](20260711-executable-schema-preamble-sample.md)

## 1. Outcome

Developers can compose and replay authentic Marten/Weasel migrations containing the newer `pg_catalog.pg_namespace` schema-existence guard without editing migration history, granting unnecessary database privileges, disabling idempotence, or weakening Sable's rejection of unsupported top-level SQL.

Extend the existing narrow safety parser. Do not replace the previous design, remove its rejection rules, or introduce a general PostgreSQL parser.

## 2. Confirmed problem and lineage

The originating unchanged Chat migration is:

`Services/Lessi.App.Chat.Api/sable/Marten/migrations/20261019154453_AddMartenStreamCompactionMetadata.sql`

The migration starts with this authentic generated shape:

```sql
DO $$
BEGIN      IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = 'chat_service') THEN
        BEGIN
          EXECUTE 'CREATE SCHEMA IF NOT EXISTS chat_service';
        EXCEPTION
          WHEN duplicate_schema THEN NULL;
          WHEN unique_violation THEN NULL;
        END;
      END IF;

END
$$;
```

The remainder contains product DDL and functions. Current Sable CLI rejects the leading block during composition:

```text
Sable cannot safely wrap migration '<migration-id>' because it begins with an unsupported top-level DO statement. Only a recognized safe schema-creation preamble can be moved outside the idempotence guard. Use Sable's no-idempotence workflow only when the migration is independently idempotent.
```

Evidence:

- Lessi commit `6cbbde85efcf60d054274d020ff865de1454a0c3` upgraded Marten `9.29.0` to `9.50.3` on 2026-10-17.
- Those package manifests declare Weasel.Postgresql `9.25.1` and `9.45.0`, respectively. Inspection of `PostgresqlMigrator.WriteSql` in those assemblies confirms exception-only output in the former and the additional catalog-existence guard in the latter. Weasel `9.48.0` still emits the guarded shape.
- Lessi commit `9db510a2338caf9fe936fcad361292ba2a06a208` first introduced this preamble into its committed service migration histories on 2026-10-17.
- Chat commit `8ee0e37341c3100626fd748876d030da78258e82` records Sable generation and successful shadow replay on 2026-10-19. Its migration blob remains `91a6bba753f9a3f0ddafb6cbbfd0614596268539`, unchanged since introduction.
- These records do not independently establish the global CLI version used for historical generation. Do not infer it from the consuming project's `Sable` library pin.
- The installed `Sable.Cli 0.0.6` package identifies repository revision `a615c5531f51dde676477671b11328dcf34730b7`. The inspected worktree includes the corresponding strict parser and legacy transaction-wrapper support.
- Consumer fleet composition using CLI `0.0.6` encountered this pattern in both Marten and Wolverine envelope histories, including the SpEd BFF. Affected cases are not limited to Agent and Learning Content or to migrations named `AddMartenStreamCompactionMetadata`.

### Why the catalog guard must remain

[Weasel PR #495](https://github.com/JasperFx/weasel/pull/495), merged 2026-08-22, explains and regression-tests the privilege requirement. PostgreSQL checks database-level `CREATE` permission before evaluating `CREATE SCHEMA IF NOT EXISTS`. A role that owns or can modify an existing schema can therefore fail an unnecessary schema-create statement without the outer existence check.

The two protections have different purposes:

- The `pg_catalog.pg_namespace` existence check prevents unnecessary database-level privilege checks.
- The `duplicate_schema` and `unique_violation` handlers protect concurrent schema creation after the check.

[Weasel PR #498](https://github.com/JasperFx/weasel/pull/498) additionally requires the catalog literal to name the actual catalog schema, without SQL identifier delimiters; the `CREATE SCHEMA` statement retains identifier quoting.

Removing the guard, replacing it with a privilege-filtered `information_schema` query, or removing either exception handler is not an acceptable compatibility fix. An earlier consumer-side normalization to the older shape was withdrawn after this evidence was established. Its successful shadow replay does not prove least-privilege correctness.

## 3. Scope and non-goals

### In scope

- Strict recognition of the catalog-guarded shape for the configured schema.
- Preservation of the original recognized SQL at PostgreSQL's top level.
- Existing composition behavior across `migrations add`, `migrations script`, and `database update`.
- Deterministic positive and adversarial regression tests.
- Explicitly opt-in, disposable PostgreSQL privilege/replay tests and a full migration-generation test.
- Source documentation, reproducible verification instructions, and a fork CLI package suitable for consumer validation.

### Out of scope

- Rewriting, renaming, or rebaselining consumers' historical migrations.
- Changing Marten mappings, product tables, service ownership, or application permissions.
- Changing `AddMartenWithSableSupport`, CLI syntax, selectors, directives, history-table layout, or migration identities.
- Accepting arbitrary `DO`, arbitrary catalog queries, arbitrary dynamic SQL, or general conditional SQL.
- Disabling idempotence or recommending `--no-idempotence-wrapper` for non-idempotent product DDL.
- Fixing histories whose infrastructure migration assumes an externally provisioned schema. That distinct bootstrap problem requires its own scoped decision; it is not evidence that this preamble is unsafe.
- Broad child-process logging, generic exit-code cleanup, transport configuration, or unrelated Sable improvements.
- Running consumer migrations or release commands against shared, staging, production, or ambiguous environments.

## 4. Required behavior

### R1. Recognize only the approved guarded operation

When default idempotence wrapping is enabled, recognize a leading `DO` whose sole operation is the following bounded structure:

1. `BEGIN`.
2. `IF NOT EXISTS (SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = '<catalog-schema-name>') THEN`.
3. The already supported inner exception-protected `EXECUTE 'CREATE SCHEMA IF NOT EXISTS <configured-schema>'` block.
4. Exactly the approved `duplicate_schema` and `unique_violation` handlers, each executing only `NULL`.
5. Matching `END IF` and outer `END`, followed by the valid dollar-quote terminator.

The inspected schema literal, dynamically created schema identifier, and configured migration schema must identify the same PostgreSQL schema. Catalog literals are **case-sensitive values**, not SQL identifiers. Do not pass the decoded catalog literal to the existing case-insensitive unquoted-identifier comparison.

For this new branch, resolve the configured name and dynamic `CREATE SCHEMA` identifier to their PostgreSQL names: fold unquoted identifiers according to PostgreSQL identifier rules; remove identifier delimiters and decode doubled double quotes for quoted identifiers without folding their contents. Decode doubled single quotes in the catalog literal, then compare its value ordinally with both resolved names. SQL identifier delimiters are not part of the catalog value. Reuse lexical readers where correct, but do not change historical branches' acceptance rules as a side effect. No substring matching, unconditional lowercasing of quoted names/literals, or new string-literal syntax is authorized.

Required equality examples (cells show SQL spelling; catalog cells are string literals):

| Configured schema | Catalog literal | Dynamic CREATE identifier | Decision |
|---|---|---|---|
| `sample` | `'sample'` | `sample` | Accept |
| `SAMPLE` | `'sample'` | `SAMPLE` | Accept: unquoted identifiers fold |
| `sample` | `'SAMPLE'` | `sample` | Reject: catalog values do not fold |
| `"Sample"` | `'Sample'` | `"Sample"` | Accept |
| `"Sample"` | `'sample'` | `"Sample"` | Reject |
| `"Sample"` | `'Sample'` | `Sample` | Reject: dynamic target resolves to `sample` |
| `sample` | `'"sample"'` | `sample` | Reject: literal includes identifier delimiters |
| `"a""b'c"` | `'a"b''c'` | `"a""b'c"` | Accept after decoding each quoting layer |

Insignificant whitespace, keyword casing, well-formed comments accepted by the existing reader, and supported dollar-quote tags must not change the decision. The grammar boundary is explicit:

- Accept `DO <dollar-body>;` and `DO LANGUAGE plpgsql <dollar-body>;`, preserving current language placement. Do not add a trailing `LANGUAGE` clause or other languages.
- The semicolon after the closing dollar delimiter is mandatory. Only the semicolon immediately after the outer body `END` is optional; `EXECUTE`, each `NULL`, inner `END`, and `END IF` require their semicolons.
- Accept either ordering of the two approved handlers, exactly once each; no combined conditions or additional handlers.
- Require complete consumption of the body, except well-formed trailing trivia. Unterminated comments, strings, delimiters, or control flow are rejected, not silently treated as trailing trivia.
- Do not expand the accepted grammar to aliases, alternative predicates, other catalog sources, or additional operations without separate evidence and review.

### R2. Preserve the generated SQL and ordering

Recognize and split; do not simplify or rewrite the recognized preamble. In particular, preserve its catalog check, exception handlers, schema quoting, and SQL content.

Logical output order remains:

1. Outer transaction unless explicitly disabled.
2. Recognized schema preamble(s), unchanged and at SQL top level.
3. Sable's tagged migration-history guard.
4. Remaining product DDL and the history insertion inside that guard.
5. Commit unless explicitly disabled.

Support consecutive recognized preambles, including mixed exception-only, legacy information-schema, and catalog-guarded forms for the configured schema, and interaction with the existing legacy `DO LANGUAGE plpgsql $tran$` envelope handling. A recognized preamble followed by an unsupported leading `DO` must reject the migration, not partially compose it. Ordinary `$$` function bodies must remain isolated from Sable's `$sable$` delimiter. Replaying a recorded migration must not rerun guarded product DDL.

### R3. Keep unsafe inputs rejected

Default wrapping must still reject unsupported or malformed top-level blocks with the existing migration-specific diagnostic and exit code `1`. No newly generated migration/script artifact or migration SQL execution may result from a rejected history. Existing requested output files must not be overwritten on failure.

Negative coverage must include at least:

- Guard schema differs from configured schema or from the `CREATE SCHEMA` target.
- The catalog predicate is broadened with `OR`, extra predicates/statements, or a different relation/column.
- Additional work appears before, inside, or after the approved schema operation within the `DO` block.
- Extra dynamic SQL, a second statement in the dynamic SQL string, or false-positive keywords in comments/literals.
- Missing, duplicate, additional, or side-effecting exception handlers.
- Malformed strings, dollar quotes, or unbalanced control flow.
- Destructive/data-changing SQL hidden beside an otherwise valid schema operation.

Do not weaken existing adversarial tests or change them to accept unrelated blocks.

### R4. Preserve existing public contracts

- Existing exception-only and legacy `information_schema.schemata` preambles retain their approved behavior. Supporting those historical inputs does not mean rewriting the newer catalog guard into them.
- `-- Sable NoIdempotenceWrapper` remains authoritative and retains its established original-SQL/history behavior.
- `-- Sable NoTransactionWrapper` continues to affect only transaction wrapping.
- Migration IDs, timestamps, paths, history rows, and each configured schema remain unchanged.
- Public commands remain `migrations add`, `migrations script`, `migrations backfill`, and `database update` with their existing arguments/options.
- Existing database URI selectors and folder mapping remain unchanged: `marten://store/` and `wolverine://messages/main` map to their established directories.
- No new library registration API, persistence, remote dependency, or transport is required.

## 5. Source-delta audit

| Boundary | Existing behavior / evidence | Required delta |
|---|---|---|
| `src/Sable.Cli/SafeSchemaPreambleParser.cs`, `IsSafeSchemaBody` | Accepts exception-only and legacy information-schema bodies | Add a narrowly bounded catalog-guarded branch using the existing SQL reader where correct |
| Same file, identifier/literal handling | Existing unquoted comparison is case-insensitive; catalog literals require exact value matching | Implement R1's separate catalog-value comparison and equality table; preserve historical branches |
| `src/Sable.Cli/Migration.cs` | Splits recognized preambles, preserves directives, emits tagged guard | Preserve behavior; add coverage for new recognized input, not a new execution strategy |
| `src/Sable.Cli/MartenMigrationManager.cs` | Shared history composition feeds scripting, shadow replay, and updates | Prove the guarded shape works at each actual command boundary |
| `src/Sable.Cli/Commands/` and `MigrationCompositionError.cs` | Existing composition diagnostic and rejection exit handling | Preserve contract and test positive/negative paths; no broad CLI refactor |
| `tests/Sable.Cli.Tests/MigrationTests.cs` | Positive older shapes, directives, legacy envelope, unsafe inputs | Add authentic newer shape, equivalence variants, identifier cases, and adversarial cases |
| `CommandExitTests.cs` | Unsupported-block CLI checks | Prove rejection/no-artifact behavior is retained and newer safe input succeeds |
| `SchemaPreambleSampleCompositionTests.cs`, `SafeSchemaPreamblePostgresSmokeTests.cs`, `SchemaPreambleSampleEndToEndTests.cs` | Existing fast and full-workflow lanes; current replay smoke executes composed SQL directly through Npgsql | Preserve older sample coverage; add guarded-sample composition/full-workflow coverage and CLI-driven replay/privilege tests without making ordinary tests require Docker |
| `samples/Sable.Samples.SchemaPreamble/`, new `samples/Sable.Samples.CatalogGuardedSchemaPreamble/`, and `verify/catalog-guarded-schema-preambles/` | Existing sample pins Marten `9.8.1`, not the documented guarded producer | Keep existing artifacts unchanged; add the separately pinned authentic producer and safe regeneration/verification workflow specified below |
| `_docs/reference/cli.md`, `_docs/reference/how-sable-works.md`, sample README and `verify/` | Documents strict safe-preamble composition | Explain catalog-guarded support and both safeguards; preserve source-authored `docs/design/` during site generation |

The original safe-preamble PRD's positive-recognition and rejection contracts remain correct. This is a compatibility gap for an additional proven schema-only form, not permission to rewrite the original design around the failure.

### 5.1. Authentic fixture and producer decision

Add a separate `samples/Sable.Samples.CatalogGuardedSchemaPreamble/` sample rather than upgrading or regenerating the existing Marten `9.8.1` sample. The old sample and its checked-in artifacts remain regression evidence for older producer output. The new sample uses its own schema `catalog_guarded_schema_preamble_sample`, matching default mappings/history, and a test-only pending mapping enabled only in temporary verification copies.

Pin the new sample to Marten `9.50.3`, the documented consumer producer. Add the dependency through `dotnet add package`, include applicable lockfile changes, and record the actually resolved Weasel.Postgresql version; do not assume that package-manifest minimums prove the resolved version. If this pinned dependency graph does not generate the approved shape, stop fixture readiness and report the mismatch rather than hand-editing SQL or silently choosing a different producer.

Create `verify/catalog-guarded-schema-preambles/regenerate.sh` using the existing approved Sable `init`/shadow-generation workflow. It must require explicit regeneration opt-in, create unique owned disposable resources, restore prior artifacts on failure, and validate that the generated Initial migration contains the catalog-guarded shape. No generated SQL may be manufactured or edited to satisfy that assertion. New generated artifact creation is explicitly in scope; rewriting existing sample or consumer artifacts is not.

The sample README records the exact regeneration command, resolved Marten/Weasel versions, producing Sable revision, and verification commands. Check in Sable-generated baseline artifacts after review. Hash baseline artifacts before and after temporary-copy tests to prove they were not changed. Use the authentic inline shape from §2 for the initial failing unit test so parser work does not depend on successful fixture generation.

## 6. Verification and acceptance

### Deterministic, Docker-free lane

- [ ] The authentic catalog-guarded shape reproduces rejection before the implementation change.
- [ ] It composes afterward with its original preamble content before `$sable$` and all product DDL inside the history guard.
- [ ] R1's equality table and grammar boundaries pass, including both handler orders, malformed trailing comments, mixed old/new consecutive preambles, and legacy transaction wrappers.
- [ ] All negative cases in R3 reject, including configured/inspected/created-schema mismatches.
- [ ] Existing ordinary-migration, directive, legacy-preamble, diagnostic, and exit-code tests pass unchanged unless a demonstrated test gap requires an additional assertion.
- [ ] Unsupported input still fails before new output artifacts, existing-output replacement, or migration execution.

### Opt-in disposable PostgreSQL lane

All database targets must be test-created local disposable containers with unique credentials/databases and ports. Provision roles and test prerequisites through fixture setup; apply generated migration content through Sable's supported CLI path, never manual execution of generated SQL. Always clean up owned containers and temporary directories, including timeout/failure paths. Do not log credentials or connection strings.

For this feature, database acceptance executes `database update` as a CLI child process against the explicit disposable target; the full-generation check executes `migrations add` with owned shadow-container options. Direct Npgsql execution of a composed migration is not a substitute. Convert the existing `SafeSchemaPreamblePostgresSmokeTests` replay path to CLI application while preserving its old-sample assertions, and add the guarded-sample cases. Npgsql remains permitted for fixture-owned role/schema/history setup, privilege assertions, a small handwritten privilege-control statement, and read-only result assertions—not for applying generated migration SQL.

Restricted-role fixtures must assert, using that role's own connection, that it is neither superuser nor database owner and that `has_database_privilege(current_user, current_database(), 'CREATE')` is false. Effective privilege checks must account for memberships and `PUBLIC`; fixture setup revokes unintended grants in the owned test database. Assert the necessary schema and history-table privileges rather than relying on role names. In a separate transaction, execute a handwritten `CREATE SCHEMA IF NOT EXISTS` control against the already existing schema and require insufficient-privilege SQLSTATE `42501`; roll back that failed transaction before the CLI success test. This control is test code, not an edited generated migration.

- [ ] Compose and apply a representative guarded history twice; verify expected schema objects/function results and exactly one history row per migration.
- [ ] Verify guarded product DDL does not run a second time, using a non-idempotent operation whose replay would fail if the history guard were bypassed.
- [ ] Pre-provision a schema and required history infrastructure, then use a restricted role with sufficient schema/table privileges but **no database-level CREATE privilege**. Apply a pending guarded migration through Sable; it succeeds without changing grants or removing the catalog guard.
- [ ] Keep that privilege test independent of an infrastructure migration that unconditionally creates schemas. Seed its prerequisites as the fixture owner; do not broaden this fix into an infrastructure policy change.
- [ ] Against a genuinely missing schema in a separate disposable case, the same restricted-role privilege conditions hold and the CLI reaches the guarded schema-create operation and fails for insufficient privilege, not merely for missing history infrastructure or connection failure. Preserve the existing execution-failure behavior; verify as the fixture owner that the target schema/product objects and applied history row were not created. Do not claim this failure path is solved by the recognition change.
- [ ] Run the full `migrations add` workflow in a temporary copy of the new guarded sample with its controlled pending mapping. Verify baseline history replay, exactly one substantive new delta, no unwanted drop artifact, unchanged baseline hashes, then composition and CLI replay of the resulting complete history against a fresh disposable target.
- [ ] Initial fixture generation emits the guarded shape, and composition preserves that generated preamble exactly. Do not require an incremental migration to emit schema creation when its producer has no reason to do so. Never manufacture compatibility by editing generated sample migration SQL; inline adversarial inputs are test code, not generated migration artifacts.

### Required verification entry points and ownership

The implementation must deliver the following scripts; paths below are planned deliverables, not claims that the commands already exist. Each script must fail on unmet assertions, enforce a timeout, clean up owned resources, and redact secrets. Ordinary Cake tests skip opt-in Docker cases; skipped discovery does not satisfy those gates.

| Evidence | Required command from repository root | Execution owner / completion boundary |
|---|---|---|
| Failing-before/passing-after parser and command regression tests | `dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj` | Test writer records pre-change failure; builder records post-change pass; repository gate |
| Authentic guarded baseline provenance | `SABLE_REGENERATE_CATALOG_GUARDED_SAMPLE=1 bash verify/catalog-guarded-schema-preambles/regenerate.sh` | Builder generates; reviewer checks producer versions and unedited artifacts; repository gate |
| CLI replay, least privilege, negative privilege case, rejection without execution | `bash verify/catalog-guarded-schema-preambles/postgres.sh` | Builder supplies opt-in tests; destroyer and sprint smoke phase verify results; repository gate |
| Complete add/script/update workflow | `bash verify/catalog-guarded-schema-preambles/e2e.sh` | Builder supplies temporary-copy workflow; sprint smoke phase runs it; repository gate |
| Packaged tool identity, composition, rejection exit, and disposable application | `bash verify/catalog-guarded-schema-preambles/package.sh` | Sprint smoke phase installs the locally built package into an isolated directory; repository gate |
| Unchanged Lessi fleet composition and approved shadow generation | Exact commands and sanitized history identifiers recorded in the GitHub consumer-validation checklist | Consumer maintainer supplies access and authorizes disposable shadow checks; consumer gate, not repository gate |

The PostgreSQL script must include a history with an earlier valid migration followed by a rejected guarded-looking migration; verify no migration SQL was executed and no history rows were inserted. Command tests also preserve an existing requested output file byte-for-byte on rejection. This proves full-history validation, not only rejection of the first file. These checks use default wrapping; directive opt-outs retain their existing contract.

### Repository build and package lane

```bash
dotnet tool restore
dotnet cake --target=Build
dotnet cake --target=Test
dotnet format Sable.sln --verify-no-changes --no-restore
npm --prefix _docs ci
npm --prefix _docs run docs:build
dotnet cake --target=Pack
```

Keep Docker lanes opt-in using the existing verification conventions. Record actual commands/results; test discovery, script generation, or a build alone is not database-execution evidence.

- [ ] Run the fork's destroyer, reviewer, committer, and tester/smoke phases before repository implementation readiness is claimed. Record real commit SHAs and actual commands/results in the GitHub sprint/control issue.
- [ ] Produce and smoke-test the locally built `Sable.Cli` package in an isolated tool directory. Verify actual tool identity/version, the guarded sample's successful composition/application, and unsupported-input rejection/exit behavior. Do not replace a global tool or publish as part of this gate; any local validation version is not a release reservation.

### Completion boundaries

1. **Repository ready for human acceptance verification:** All deterministic, disposable PostgreSQL, full-workflow, docs/build/format/pack, packaged-tool, and quality-phase gates above have actual passing evidence. Docker gates are opt-in to ordinary tests but mandatory for this readiness claim. Missing Docker or producer-package access means the affected repository gate is blocked, not waived. Prepare `## 🧑‍⚖️ Ready for Acceptance Verification: ...` in the sprint/control issue; agents do not self-approve acceptance or close issues.
2. **Consumer validation ready:** The consumer maintainer supplies the unchanged affected Lessi histories and intended feed/source configuration. With explicit approval, compose those histories and run owned disposable shadow-generation checks using the corrected CLI. Record tool version separately from the application's `Sable` library pin, sanitized scope/results, and any distinct infrastructure-bootstrap failures. Missing access leaves this gate explicitly pending; it neither blocks repository review nor permits claiming consumer compatibility was verified. Never apply these histories to real application databases for acceptance.
3. **Release authorized:** A human reviews repository acceptance and consumer evidence (or explicitly accepts a documented deferral), selects the next available release version, and authorizes publication/feed changes. Repository readiness alone does not authorize publication, global-tool replacement, or consumer rollout.

The PM creates separately identified repository and consumer-validation checklists in GitHub, with the responsible human named before external checks begin. Unavailable evidence remains pending/blocked with an owner; it must not disappear into a repository completion claim.

## 7. Contract impact

- **UI/accessibility:** Not applicable; no application UI.
- **Public .NET registration API:** Unchanged.
- **CLI arguments/options/layout:** Unchanged; existing safe-input acceptance expands to the proven guarded form.
- **SQL semantics/idempotence:** Existing protections preserved; no product DDL or history-state redesign.
- **Authorization/privileges:** Existing PostgreSQL least-privilege boundary is explicitly in scope; both positive existing-schema and negative missing-schema checks are required.
- **New persistence or cross-resource linkage:** None.
- **Growing data reads:** No new database collection reads; parsing remains bounded to the supplied migration input under the existing composition model. No new payload limit is invented.
- **Error behavior:** Existing unsupported-input diagnostic/exit contract preserved; no new public exception-disclosure surface.
- **Distributed workflow:** No new boundary; existing local CLI/shadow-database behavior only.
- **Runtime validation:** Required, explicitly local/disposable and opt-in.
- **Documentation:** Source-authored PRD here; update `_docs/` and regenerate published site output during implementation.

## 8. Planning and review handoff

This document is a design deliverable, not a sprint-status file or implementation authorization. The PM should translate the reviewed requirements into a GitHub sprint/control issue with implementation and test deliverables; do not create discovery-only tasks or a filesystem sprint backend.

Delivery order is: (1) lock the R1 contract and write failing inline/command regressions; (2) implement narrowly bounded parser support against those tests; (3) create the separately pinned producer sample and regenerate authentic fixtures with that implementation; (4) deliver CLI-driven privilege/replay/full-command verification; (5) update source documentation, build the package, and run isolated package verification. Do not make parser regression tests depend on the later fixture-generation task.

Before marking implementation tasks ready, the PM must map every requirement and acceptance checkbox to a task owner, exact test/script, prerequisite, and completion boundary using §6. Assign automated PostgreSQL fixture/test work explicitly to an integration-capable builder task rather than the unit-only test-writer role. Treat scripts as deliverables, not substitutes for executing their checks. Keep the distinct infrastructure-bootstrap and broad CLI-diagnostics concerns as separate human-prioritized work.

The earlier `fix/top-level-do-migrations` work was already merged. This follow-up PRD lives on `docs/catalog-guarded-schema-preambles`, created from fetched `origin/main` at `a615c5531f51dde676477671b11328dcf34730b7`. Do not revive the merged branch or count its historical commits as unreviewed follow-up work. Implementation should use a focused follow-up branch based on current main. No push or PR creation is authorized by this documentation request.

## 9. Decision record

- **Human request:** Create a PRD in the Sable fork for correcting compatibility rather than rewriting consumer migration history or weakening rejection checks.
- **Retained contract:** Strict recognition of proven schema-only effects, original SQL preservation, idempotence, transactions, directives, and rejection of unsupported blocks.
- **New evidence:** The catalog guard is required for least-privilege correctness, per Weasel #495/#498; an exception-only rewrite is not semantically equivalent for restricted roles.
- **Alternatives rejected:** Blindly hoisting arbitrary `DO`; removing the catalog guard; changing grants; disabling idempotence; hand-rewriting consumer histories.
- **Review decisions:** Catalog values compare exactly after independent identifier resolution; preserve historical parser branches. Add a separate Marten `9.50.3` producer sample instead of upgrading the old fixture. Apply generated SQL through CLI-driven tests, with independently verified restricted-role privileges. Repository, consumer, and publication readiness are distinct gates with named evidence owners.
- **Deferred:** Infrastructure histories that require pre-provisioned schemas; unrelated process-output and exit-code changes.
- **Confidence:** High for the reproduced compatibility gap and upstream guard rationale. Historical global CLI provenance and the release version remain unproven/unselected and must not be guessed.
