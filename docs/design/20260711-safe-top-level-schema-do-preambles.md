# Product Requirements Document: Safe Top-Level Schema-Creation `DO` Preambles

**Status:** Approved for sprint planning
**Planned sprint state backend:** github-issues
**Owner / lead:** team-lead
**Originating consumer:** Lessi.App Learning Content API
**Related branch:** `fix/top-level-do-migrations`
**Related PR(s):** n/a

## Summary

Sable must be able to consolidate and replay migrations that begin with a safe, idempotent top-level schema-creation `DO` preamble. Sable must keep recognized safe schema preambles at PostgreSQL's top level, preserve idempotence and transaction semantics for the remaining migration, retain explicit wrapper directives, and reject unsupported top-level `DO` content with an actionable nonzero CLI failure.

This corrects a migration shape observed in Marten 9, but recognition is based on the preamble's safety characteristics rather than a producer or version identity. It is not a general PostgreSQL parser or a redesign of Sable's migration execution model.

## Problem statement

Lessi.App uses Sable to generate and consolidate Marten schema migrations. While adding `VisualSupportDocument` to the Learning Content API, the approved command failed:

```text
sable migrations add AddVisualSupportDocument

42601: syntax error at or near "BEGIN"
POSITION: 366
```

The document mapping was not responsible. Sable could not replay an existing Marten 9 migration into its disposable shadow database. Because Sable must first reconstruct migration history in the shadow database, this failure prevented every subsequent migration from being generated.

### Root cause

Marten 9 generated a migration beginning with a top-level schema-creation block:

```sql
DO $$
BEGIN
    BEGIN
        EXECUTE 'CREATE SCHEMA IF NOT EXISTS learning_content_service';
    EXCEPTION
        -- Marten-generated exception handling
        ...
    END;
END
$$;
```

The migration then contained additional DDL, including functions whose bodies used `$$` delimiters.

Sable attempted to place the entire migration inside its migration-history guard:

```sql
DO $sable$
BEGIN
    IF migration_not_applied THEN
        -- Entire Marten migration, including its top-level DO block
    END IF;
END
$sable$;
```

This fails because:

1. PostgreSQL does not permit Marten's top-level `DO` statement to be nested inside Sable's PL/pgSQL block.
2. An untagged `$$` delimiter for Sable's outer block can be terminated prematurely by Marten function bodies that also use `$$`.

## Target users and workflows

### Primary user

A .NET developer using Sable with Marten to add, consolidate, or apply schema migrations.

### Affected workflows

- `sable migrations add`: replay existing migrations into the disposable shadow database before asking Marten to generate the next patch.
- `sable migrations script`: consolidate stored migrations into a self-contained idempotent script.
- `sable database update`: execute the same consolidated migration semantics against an explicitly selected database.

No command names, arguments, options, generated paths, or public .NET registration APIs change.

## Goals

1. Restore Sable migration generation for projects whose migration history includes safe schema-creation `DO` preambles.
2. Keep recognized safe schema preambles at PostgreSQL's top level.
3. Keep the remaining migration DDL and history insertion inside Sable's idempotence guard.
4. Preserve existing transaction and no-wrapper directives exactly.
5. Use a tagged Sable dollar delimiter that cannot be terminated by ordinary Marten `$$` function bodies.
6. Fail early and clearly when Sable cannot safely compose an unsupported top-level `DO` statement.
7. Verify the behavior against an explicitly disposable local PostgreSQL container.

## Non-goals

- General-purpose PostgreSQL parsing or rewriting.
- Hoisting arbitrary user-authored `DO` blocks.
- Replacing Sable's generated-script idempotence model with application-side migration gating.
- Adding CLI flags or changing generated migration locations.
- Changing `AddMartenWithSableSupport` or other public .NET registration APIs.
- Hand-editing generated sample migration or script SQL.
- Executing verification against shared, staging, or production databases.
- Suppressing schema-creation SQL for externally managed schemas.
- Replacing downstream schema-stripping workarounds as part of this sprint.

## Decision: externally managed schemas

This PRD fixes safe composition of top-level schema-creation `DO` preambles only. Sable deployment scripts will continue to contain recognized schema-creation preambles. A first-class externally managed schemas policy—and removal of downstream SQL-stripping workarounds—is separate follow-up work.

## Product requirements

### PR-1: Strict safe-preamble recognition

Sable shall recognize one or more consecutive leading schema-creation `DO` preambles when idempotence wrapping is enabled and the blocks satisfy the approved safety characteristics.

Recognition must be narrow and positive. A recognized block must perform only idempotent creation of the configured database schema and bounded exception handling for that operation; recognition must not depend on a Marten version marker or exact generated formatting. Differences in insignificant whitespace, comments, or valid dollar-quote tags must not prevent recognition. Text in comments, string literals, or unrelated statements must not create a false positive.

Sable shall not treat the mere presence of the text `CREATE SCHEMA IF NOT EXISTS` as sufficient proof that an arbitrary `DO` block is safe to hoist.

### PR-2: Generated SQL ordering and idempotence

For each recognized migration, generated SQL shall have this logical order:

1. Begin the migration transaction, unless transaction wrapping is disabled.
2. Execute recognized safe schema preamble block(s) at PostgreSQL's top level.
3. Execute Sable's tagged migration-history guard.
4. Within the guard, execute the remaining Marten DDL and insert the migration-history row.
5. Commit the transaction, unless transaction wrapping is disabled.

The schema preamble may run on each replay because its schema creation is idempotent. When the migration-history row already exists, the remaining migration DDL and history insertion must not run again.

### PR-3: Dollar-quote isolation

Sable's own PL/pgSQL guard shall use a distinct tagged delimiter such as `$sable$`. Marten function or procedure bodies using `$$` must remain intact and must not terminate Sable's guard.

### PR-4: Directive compatibility

Existing directives remain authoritative:

- `-- Sable NoIdempotenceWrapper` means Sable shall not wrap, split, hoist, or reject the migration based on top-level `DO` content. The source migration executes as top-level SQL, followed by Sable's existing history-record behavior.
- `-- Sable NoTransactionWrapper` means Sable shall not add an outer transaction.

Automatic Marten compatibility behavior must never override either directive.

### PR-5: Unsupported top-level `DO` diagnostic

When idempotence wrapping is enabled and a migration begins with a top-level `DO` statement that is not a recognized safe schema-creation preamble, Sable shall stop before database execution or output-file creation.

The CLI shall write an actionable error equivalent to:

```text
Sable cannot safely wrap migration '<migration-id>' because it begins with an unsupported top-level DO statement. Only a recognized safe schema-creation preamble can be moved outside the idempotence guard. Use Sable's no-idempotence workflow only when the migration is independently idempotent.
```

The affected command shall return exit code `1`. It shall not report success, emit a raw PostgreSQL syntax error as the primary diagnostic, or leave a newly generated migration/script artifact.

### PR-6: Backward compatibility

- Ordinary migrations retain their current generated SQL ordering and history semantics.
- Infrastructure setup and backfill behavior remain unchanged.
- Migration identifiers, timestamps, paths, and history-table values remain unchanged.
- Multi-database and multi-tenant configurations use each migration's configured schema when recognizing a safe preamble.
- Existing independently idempotent no-wrapper migrations remain valid, including migrations containing statements such as `CREATE INDEX CONCURRENTLY`.

### PR-7: Documentation

Source documentation under `_docs/` shall explain that Sable keeps recognized safe schema preambles top-level while guarding the remaining migration. It shall also preserve and clarify the advanced no-wrapper semantics. Generated `docs/` output must be regenerated from `_docs/`, never edited directly.

## SQL invariants

1. No PostgreSQL `DO` command may be emitted inside Sable's PL/pgSQL guard.
2. No arbitrary side-effecting statement may be moved outside migration-history gating.
3. A migration-history row is inserted only after its guarded DDL succeeds.
4. Recognized schema preambles and guarded DDL remain in the same Sable transaction unless explicitly disabled.
5. A migration already recorded in `__sable_migrations` does not rerun its guarded DDL.
6. Explicit no-wrapper directives take precedence over automatic composition.
7. Sable never executes generated SQL against production, staging, shared, or ambiguous databases as part of verification.

## 🔎 Contract Impact Check

- **UI only?** No; Sable has no application UI.
- **Existing typed API contract sufficient?** Public .NET registration APIs remain sufficient and unchanged. CLI composition/error behavior changes in `src/Sable.Cli`.
- **New request/response fields needed?** No.
- **Server-side validation/auth/ownership needed?** No.
- **Cross-entity IDs or durable linkage introduced?** No.
- **Persistence/metadata needed?** No new persistence; existing `__sable_migrations` semantics must be preserved.
- **CLI contract affected?** Yes: generated SQL composition, actionable error output, and exit code `1` for unsupported top-level `DO` content.
- **Generated SQL contract affected?** Yes: recognized safe schema preambles move before the idempotence guard.
- **Backend/API tests needed?** Yes: deterministic xUnit coverage for SQL composition and directives.
- **Runtime validation needed?** Yes: explicitly disposable PostgreSQL smoke verification.
- **Documentation affected?** Yes: `_docs/reference/how-sable-works.md` and generated documentation output.

## Source delta audit

| Area | `origin/main` behavior | Current feature-branch behavior | Required disposition |
|---|---|---|---|
| Outer guard delimiter | Uses tagged `$sable$`; protects against ordinary Marten `$$` function bodies | Preserved | Keep |
| Leading safe schema `DO` | Entire migration is nested and fails PostgreSQL parsing | One leading block is split by regex and hoisted | Replace with strict characteristic-based recognition; support consecutive recognized preambles |
| Recognition safety | No recognition | Any first `DO` containing the substring `CREATE SCHEMA IF NOT EXISTS` is accepted | Reject broad substring matching and false positives |
| `NoIdempotenceWrapper` | Directive is authoritative | Automatic split sets wrapping back to `true` | Restore exact directive precedence |
| Unsupported top-level `DO` | Reaches PostgreSQL and fails with a syntax error | Usually remains nested and fails later | Fail before execution/output with the specified diagnostic and exit code `1` |
| Tests | No focused migration-composition tests | Four string-oriented tests; one codifies directive override | Replace/expand with approved fixtures and directive coverage |
| PostgreSQL execution evidence | None | None | Add an explicitly disposable container smoke check |
| Documentation | Describes the original wrapper model | Unchanged | Document safe-preamble composition and retained directives |

## Acceptance criteria

- [ ] A representative Marten 9 migration with a leading schema-creation `DO` preamble can be consolidated without nesting that `DO` inside Sable's guard.
- [ ] One or more consecutive recognized safe schema preambles are emitted before `DO $sable$`.
- [ ] Remaining DDL, including functions with `$$` bodies, remains inside the migration-history guard and executes successfully.
- [ ] Replaying the consolidated script twice succeeds and records exactly one history row for the migration.
- [ ] `NoIdempotenceWrapper` is never overridden, and independently idempotent top-level SQL remains unwrapped.
- [ ] `NoTransactionWrapper` continues to suppress Sable's outer transaction.
- [ ] An unsupported or malformed leading top-level `DO` fails before execution/output with the approved diagnostic and exit code `1`.
- [ ] Comments and quoted text containing schema-creation words do not trigger hoisting.
- [ ] Ordinary migrations, infrastructure setup, backfill, multiple-database paths, and multi-tenant schema selection do not regress.
- [ ] Focused tests, full build/test gates, formatting verification, and documentation build pass.
- [ ] Disposable PostgreSQL smoke verification passes without using a shared or persistent database.

## Verification requirements

### Deterministic tests

- Exact originating Marten 9 schema-preamble fixture plus equivalent safe formatting variants.
- Consecutive recognized preambles.
- Marten functions using `$$` delimiters.
- Ordinary migration composition.
- Both wrapper directives independently and together.
- Unsupported and malformed leading `DO` statements.
- False-positive resistance for comments and quoted strings.
- Migration ID included in the diagnostic.
- Exit code and no-output-artifact behavior for affected CLI workflows.

### Disposable PostgreSQL smoke

Start an isolated local PostgreSQL container, execute a representative consolidated migration twice, and verify:

- expected schema objects exist;
- function bodies compile;
- exactly one migration-history row exists;
- the second replay performs no guarded DDL;
- the container is removed afterward.

### Repository gates

```bash
dotnet tool restore
dotnet cake --target=Build
dotnet cake --target=Test
dotnet format Sable.sln --verify-no-changes --no-restore
npm --prefix _docs ci
npm --prefix _docs run docs:build
```

Docker is permitted only for the explicitly assigned disposable smoke verification.

## 🧩 Planning handoff

The PM phase shall convert this PRD into implementation-ready tasks with exact files, dependencies, acceptance criteria, verification commands, and commit hints. Expected workstreams are:

- [ ] 🧭 Lock SQL-composition and CLI error contracts.
- [ ] 🧭 Replace broad preamble detection with strict characteristic-based safe-preamble recognition.
- [ ] 🧭 Preserve wrapper directives and ordinary migration behavior.
- [ ] 🧭 Add deterministic regression and diagnostic tests.
- [ ] 🧭 Add explicitly disposable PostgreSQL smoke verification.
- [ ] 🧭 Update source documentation and regenerate `docs/`.

## 👀 Quality gates

- [ ] 🔥 Destroyer round 1 complete
- [ ] 👀 Reviewer round 1 PASS
- [ ] Committer phase records reviewed task commit SHA(s)
- [ ] 🧪 Full build/test/format/docs smoke round 1 PASS
- [ ] 🧑‍⚖️ Ready for Acceptance Verification evidence posted for human review

## Decisions

- 2026-07-10 — Recognize safe schema-creation preambles by characteristics rather than Marten version identity, while rejecting general top-level PostgreSQL rewriting.
- 2026-07-10 — Preserve both existing wrapper directives exactly.
- 2026-07-10 — Require disposable PostgreSQL execution evidence.
- 2026-07-10 — Reject unsupported top-level `DO` content early with a Sable-specific diagnostic and exit code `1`.
- 2026-07-10 — Retain Sable's existing generated-script idempotence architecture.
- 2026-07-11 — Keep externally managed schema suppression out of this sprint; deployment scripts continue to include schema-creation preambles.

## Original acceptance source

The originating report is the Lessi.App failure reproduced in this issue: `sable migrations add AddVisualSupportDocument` failed with PostgreSQL `42601` while replaying a Marten 9 migration. Acceptance is based on restoring that migration workflow while satisfying the compatibility, safety, and verification requirements above—not merely on making the current feature-branch tests pass.
