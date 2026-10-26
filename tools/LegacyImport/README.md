# LegacyImport

Importer standalone dalla sorgente legacy (JSON sanificato o export) al MySQL nuovo. Non dipende dall'API né dalle sue migration: il target deve avere lo schema già migrato (`make migrate`).

## Comandi

```bash
# dal meta repo
make legacy-dry-run

# dry-run con verifica delle collisioni sul target
LEGACY_IMPORT_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;' \
  make legacy-dry-run

# apply su un target MySQL
export LEGACY_IMPORT_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;'
dotnet run --project tools/LegacyImport -- \
  --input tools/LegacyImport/fixtures/rehearsal.json \
  --report /tmp/import-report.json \
  --apply          # senza chiavi aggiungi --allow-reauth
```

Segreti solo da variabili d'ambiente, mai da argomenti:

```text
LEGACY_IMPORT_CONNECTION=Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;
LEGACY_IMPORT_OLD_KEY=<base64 32 byte della vecchia chiave ServerAccess>
LEGACY_IMPORT_NEW_KEY=<base64 32 byte della nuova chiave>
LEGACY_IMPORT_NEW_KEY_VERSION=1
```

La vecchia chiave decifra `ServerCredentials`; la nuova ricifra con `LEGACY_IMPORT_NEW_KEY_VERSION` (default 1). Se una delle due manca o il record non è decifrabile, la credenziale viene importata con `NeedsReauth=true` e segreto vuoto. `--apply` rifiuta questo caso senza `--allow-reauth`; il dry-run lo segnala.

## Comportamento

- La fixture usa i nomi delle tabelle e delle colonne legacy. Il mapping verso le colonne nuove è in `ImportPlans.cs`.
- Gli ID sono preservati. Una riga già presente con lo stesso ID viene saltata solo se la chiave naturale coincide (`Name`, `Username`, `Path`, `SourceServerId`/`TargetServerId`, ecc.); altrimenti il report segnala la collisione e l'apply viene annullato.
- L'apply usa una sola transazione: o importa tutto, o non scrive nulla. `RolledBack` e `Inserted=0` nel report indicano l'annullamento.
- `Menus.ParentId` e `ServerApplications.ProxyRouteId` sono aggiornati dopo gli insert, nella stessa transazione, per evitare cicli FK. Gli orfani sono rifiutati dal preflight.
- `CommandExecutions` viene redatto di default (`Script`/`Output` vuoti); `--no-redact-audit` per un import esplicito non redatto.
- Il report JSON contiene conteggi per tabella, errori, warning, credenziali `NeedsReauth`, righe di audit redatte, `RolledBack` e la versione della nuova chiave.
- Il dry-run senza `LEGACY_IMPORT_CONNECTION` verifica solo la fixture; con la connessione controlla anche le collisioni di ID e chiave naturale sul target, senza scrivere.
- Le chiavi AES e la connection string non sono accettate come argomenti: finirebbero in `ps` e nella cronologia della shell.

## Limiti

- La sorgente JSON è il formato di rehearsal/dry-run; l'adattatore MySQL legacy è il prossimo passo.
- Il preflight controlla FK e unique sulle tabelle presenti nella fixture; l'import reale deve partire da un dump sanificato completo. Le tabelle assenti sono elencate nei warning.
- `--classes`, `--batch-size` e `--fresh` non sono ancora implementati.
