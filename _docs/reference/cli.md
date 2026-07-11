# Command Line Interface

## `sable init`

Initialize the migration infrastructure for a database.

### Usage

```bash
sable init [OPTIONS]
```

### Options

| Option                                | Description                                                                                                                                                                                              |
|---------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `-p, --project <project-file-path>`   | Path to the project file of the Marten project. Defaults to using the project file in the current directory if it's the only one in there.                                                               |
| `-d, --database <database-name>`      | Which database to use. Defaults to the `Marten` database.                                                                                                                                                |
| `-s, --schema <schema-name>`          | Name of the database schema. Defaults to the `public` schema.                                                                                                                                            |
| `-c, --container-options <options-file-path>` | Path to a JSON file that contains options for building a custom Postgres container that is used as the shadow database for migration management. See [How Sable Works](./how-sable-works) to learn more. |

## `sable migrations add`

Add a new migration for a database.

### Usage

```bash
sable migrations add <migration-name> [OPTIONS]
```

### Options

| Option                                        | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
|-----------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `-p, --project <project-file-path>`           | Path to the project file of the Marten project. Defaults to using the project file in the current directory if it's the only one in there.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `-d, --database <database-name>`              | Which database to use. Defaults to the `Marten` database.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `-c, --container-options <options-file-path>` | Path to a JSON file that contains options for building a custom Postgres container that is used as the shadow database for migration management. See [How Sable Works](./how-sable-works) to learn more.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |
| `--no-transaction-wrapper`                  | Do not add Sable's outer `BEGIN` and `COMMIT`. The migration-history guard remains enabled unless `--no-idempotence-wrapper` is also specified. Use only for PostgreSQL statements that cannot run in a transaction. |
| `--no-idempotence-wrapper`                  | Keep the original migration SQL top-level instead of placing it in Sable's migration-history guard. Sable does not split, hoist, or reject top-level `DO` content in this mode, and still conditionally records migration history afterward. Every source statement must be independently idempotent, for example `CREATE INDEX CONCURRENTLY IF NOT EXISTS ...`. |

## `sable migrations script`

Create an idempotent migration script from existing migrations that can be used to bring a database up to date.

### Usage

```bash
sable migrations script  [OPTIONS]
```

### Options

| Option                              | Description                                                                                                                                |
|-------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `-p, --project <project-file-path>` | Path to the project file of the Marten project. Defaults to using the project file in the current directory if it's the only one in there. |
| `-d, --database <database-name>`    | Which database to use. Defaults to the `Marten` database.                                                                                  |
| `-f, --from <migration-identifier>` | Id or name of the first migration that should be included in the script. Defaults to the first migration that was generated.               |
| `-t, --to <migration-identifier>`   | Id or name of the last migration that should be included in the script. Defaults to the last migration that was generated.                 |
| `-o, --output <output-file-path>`   | Path of the file to save the script to. Defaults to a path within the `sable` directory tree.                                         |

### Leading `DO` statements

The `migrations add`, `migrations script`, and `database update` workflows keep recognized idempotent schema-creation preambles at PostgreSQL's top level while guarding the rest of the migration. This support is deliberately narrow; it does not hoist arbitrary `DO` statements or suppress schema creation.

If idempotence wrapping is enabled and a migration begins with an unsupported or malformed top-level `DO`, Sable returns exit code `1` before creating a script/migration artifact or executing migration SQL:

```text
Sable cannot safely wrap migration '<migration-id>' because it begins with an unsupported top-level DO statement. Only a recognized safe schema-creation preamble can be moved outside the idempotence guard. Use Sable's no-idempotence workflow only when the migration is independently idempotent.
```

See [How Sable Works](./how-sable-works#safe-top-level-schema-preambles) for the generated SQL ordering and advanced directive behavior.

## `sable migrations backfill`

For an existing database that is already up to date, and for which the migration infrastructure has newly been initialized, backfill the newly created migrations.

### Usage

```bash
sable migrations backfill  [OPTIONS]
```

### Options

| Option                              | Description                                                       |
|-------------------------------------| ----------------------------------------------------------------- |
| `-p, --project <project-file-path>` | Path to the project file of the Marten project. Defaults to using the project file in the current directory if it's the only one in there.               |
| `-d, --database <database-name>`    | Which database to use. Defaults to the `Marten` database.                                         |
| `-o, --output <output-file-path>`   | Path of the file to save the script to. Defaults to a path within the `sable` directory tree.                                         |

## `sable database update`

Use pending migrations to bring a database up to date.

### Usage

```bash
sable database update <connection-string>  [OPTIONS]
```

### Options

| Option                                   | Description                                                                                                                                |
|------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `-p, --project <project-file-path>`      | Path to the project file of the Marten project. Defaults to using the project file in the current directory if it's the only one in there. |
| `-d, --database <database-name>`         | Which database to use. Defaults to the `Marten` database.                                                                                  |
| `-m, --migration <migration-identifier>` | Id or name of the latest migration that should be applied. Defaults to the last migration that was generated.                              |