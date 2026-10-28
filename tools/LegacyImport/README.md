# LegacyImport

Standalone importer from the legacy source (legacy MySQL or sanitized JSON) to the new MySQL database. It does not depend on the API or its migrations: the target must already have the migrated schema (`make migrate`).

The MySQL source is read from `LEGACY_SOURCE_CONNECTION`; alternatively `--input` reads a JSON fixture.

## Commands

```bash
# from the meta repo
make legacy-dry-run       # importer dry-run on JSON fixtures

# dry-run with target collision checks
LEGACY_IMPORT_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;' \
  make legacy-dry-run

# legacy MySQL source
LEGACY_SOURCE_CONNECTION='Server=localhost;Port=3308;Database=infraorgit_legacy;User ID=legacy;Password=dev-legacy-password;' \
  dotnet run --project tools/LegacyImport -- --report /tmp/import-report.json

# apply to a MySQL target
export LEGACY_IMPORT_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;'
dotnet run --project tools/LegacyImport -- \
  --input tools/LegacyImport/fixtures/rehearsal.json \
  --report /tmp/import-report.json \
  --apply          # without keys add --allow-reauth
```

Secrets only from environment variables, never from arguments:

```text
LEGACY_SOURCE_CONNECTION=Server=localhost;Port=3308;Database=infraorgit_legacy;User ID=...;Password=...;
LEGACY_IMPORT_CONNECTION=Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;
LEGACY_IMPORT_OLD_KEY=<32-byte base64 of the old ServerAccess key>
LEGACY_IMPORT_NEW_KEY=<32-byte base64 of the new key>
LEGACY_IMPORT_NEW_KEY_VERSION=1
```

The old key decrypts `ServerCredentials`; the new key re-encrypts them with `LEGACY_IMPORT_NEW_KEY_VERSION` (default 1). If either key is missing or the record cannot be decrypted, the credential is imported with `NeedsReauth=true` and an empty secret. `--apply` rejects that case unless `--allow-reauth` is given; the dry-run reports it.

## Behavior

- The fixture uses the legacy table and column names. The mapping to the new columns is in `ImportPlans.cs`.
- With `LEGACY_SOURCE_CONNECTION` the adapter reads every table in `ImportPlans` with `SELECT *` and produces the same JSON rows as the fixture; missing tables end up in the warnings.
- IDs are preserved. An existing row with the same ID is skipped only when the natural key matches (`Name`, `Username`, `Path`, `SourceServerId`/`TargetServerId`, etc.); otherwise the report flags the collision and the apply is rolled back.
- The apply uses a single transaction: it either imports everything or writes nothing. `RolledBack` and `Inserted=0` in the report mean it was rolled back.
- `Menus.ParentId` and `ServerApplications.ProxyRouteId` are updated after the inserts, in the same transaction, to avoid FK cycles. Orphans are rejected by the preflight.
- `CommandExecutions` is redacted by default (`Script`/`Output` empty); use `--no-redact-audit` for an explicit non-redacted import.
- The JSON report contains per-table counts, errors, warnings, `NeedsReauth` credentials, redacted audit rows, `RolledBack` and the new key version.
- A dry-run without `LEGACY_IMPORT_CONNECTION` checks the fixture only; with the connection it also checks ID and natural-key collisions on the target, without writing.
- AES keys and the connection string are not accepted as arguments: they would show up in `ps` and the shell history.

## Limits

- The fake dump covers one machine, one proxy, one service, one diagram and one execution: it validates the adapter, not representative data. The rehearsal on a sanitized legacy export is still to do.
- The preflight checks FKs and uniques on the tables present in the source; missing tables are listed in the warnings.
- `--classes`, `--batch-size` and `--fresh` are not implemented yet.
