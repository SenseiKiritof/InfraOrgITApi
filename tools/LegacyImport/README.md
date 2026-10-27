# LegacyImport

Importer standalone dalla sorgente legacy (MySQL legacy o JSON sanificato) al MySQL nuovo. Non dipende dall'API né dalle sue migration: il target deve avere lo schema già migrato (`make migrate`).

La sorgente MySQL si legge da `LEGACY_SOURCE_CONNECTION`; in alternativa `--input` legge una fixture JSON. Il dump fittizio per il rehearsal è nel Compose, profilo `legacy` (`make legacy-rehearsal`).

## Comandi

```bash
# dal meta repo
make legacy-dry-run       # dry-run importer su fixture JSON
make legacy-rehearsal     # dry-run contro il MySQL legacy fittizio in Docker

# dry-run con verifica delle collisioni sul target
LEGACY_IMPORT_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;' \
  make legacy-dry-run

# sorgente MySQL legacy
LEGACY_SOURCE_CONNECTION='Server=localhost;Port=3308;Database=infraorgit_legacy;User ID=legacy;Password=dev-legacy-password;' \
  dotnet run --project tools/LegacyImport -- --report /tmp/import-report.json

# apply su un target MySQL
export LEGACY_IMPORT_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;'
dotnet run --project tools/LegacyImport -- \
  --input tools/LegacyImport/fixtures/rehearsal.json \
  --report /tmp/import-report.json \
  --apply          # senza chiavi aggiungi --allow-reauth
```

Segreti solo da variabili d'ambiente, mai da argomenti:

```text
LEGACY_SOURCE_CONNECTION=Server=localhost;Port=3308;Database=infraorgit_legacy;User ID=...;Password=...;
LEGACY_IMPORT_CONNECTION=Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;
LEGACY_IMPORT_OLD_KEY=<base64 32 byte della vecchia chiave ServerAccess>
LEGACY_IMPORT_NEW_KEY=<base64 32 byte della nuova chiave>
LEGACY_IMPORT_NEW_KEY_VERSION=1
```

La vecchia chiave decifra `ServerCredentials`; la nuova ricifra con `LEGACY_IMPORT_NEW_KEY_VERSION` (default 1). Se una delle due manca o il record non è decifrabile, la credenziale viene importata con `NeedsReauth=true` e segreto vuoto. `--apply` rifiuta questo caso senza `--allow-reauth`; il dry-run lo segnala.

## Comportamento

- La fixture usa i nomi delle tabelle e delle colonne legacy. Il mapping verso le colonne nuove è in `ImportPlans.cs`.
- Con `LEGACY_SOURCE_CONNECTION` l'adapter legge ogni tabella di `ImportPlans` con `SELECT *` e produce le stesse righe JSON della fixture; le tabelle assenti finiscono nei warning.
- Gli ID sono preservati. Una riga già presente con lo stesso ID viene saltata solo se la chiave naturale coincide (`Name`, `Username`, `Path`, `SourceServerId`/`TargetServerId`, ecc.); altrimenti il report segnala la collisione e l'apply viene annullato.
- L'apply usa una sola transazione: o importa tutto, o non scrive nulla. `RolledBack` e `Inserted=0` nel report indicano l'annullamento.
- `Menus.ParentId` e `ServerApplications.ProxyRouteId` sono aggiornati dopo gli insert, nella stessa transazione, per evitare cicli FK. Gli orfani sono rifiutati dal preflight.
- `CommandExecutions` viene redatto di default (`Script`/`Output` vuoti); `--no-redact-audit` per un import esplicito non redatto.
- Il report JSON contiene conteggi per tabella, errori, warning, credenziali `NeedsReauth`, righe di audit redatte, `RolledBack` e la versione della nuova chiave.
- Il dry-run senza `LEGACY_IMPORT_CONNECTION` verifica solo la fixture; con la connessione controlla anche le collisioni di ID e chiave naturale sul target, senza scrivere.
- Le chiavi AES e la connection string non sono accettate come argomenti: finirebbero in `ps` e nella cronologia della shell.

## Limiti

- Il dump fittizio copre una macchina, un proxy, un servizio, un diagramma e un'esecuzione: valida l'adapter, non i dati rappresentativi. Il rehearsal su dump sanificato reale resta da fare.
- Il preflight controlla FK e unique sulle tabelle presenti nella sorgente; le tabelle assenti sono elencate nei warning.
- `--classes`, `--batch-size` e `--fresh` non sono ancora implementati.
