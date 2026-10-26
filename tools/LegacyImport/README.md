# LegacyImport

Importer standalone dalla sorgente legacy (JSON sanificato o export) al MySQL nuovo. Non dipende dall'API né dalle sue migration: il target deve avere lo schema già migrato (`make migrate`).

## Comandi

```bash
# dal meta repo
make legacy-dry-run

# apply su un target MySQL
dotnet run --project tools/LegacyImport -- \
  --input tools/LegacyImport/fixtures/rehearsal.json \
  --report /tmp/import-report.json \
  --apply --connection 'Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;'
```

Variabili per le credenziali:

```text
LEGACY_IMPORT_OLD_KEY=<base64 32 byte della vecchia chiave ServerAccess>
LEGACY_IMPORT_NEW_KEY=<base64 32 byte della nuova chiave>
```

La vecchia chiave decifra `ServerCredentials`; la nuova ricifra con `KeyVersion=1`. Se una delle due manca o il record non è decifrabile, la credenziale viene importata con `NeedsReauth=true` e segreto vuoto.

## Comportamento

- La fixture usa i nomi delle tabelle e delle colonne legacy. Il mapping verso le colonne nuove è in `ImportPlans.cs`.
- Gli ID sono preservati; le righe già presenti per chiave vengono saltate. Le join usano la chiave composta.
- `Roles`/`Menus` esistenti dal seed vengono saltati per ID; `MenuRole` viene unito.
- `Menus.ParentId` e `ServerApplications.ProxyRouteId` sono aggiornati in una seconda fase, per evitare cicli FK.
- `CommandExecutions` viene redatto di default (`Script`/`Output` vuoti); `--no-redact-audit` per un import esplicito non redatto.
- Il report JSON contiene conteggi per tabella, errori, warning, credenziali `NeedsReauth` e record di audit redatti.
- `--apply` è transazionale per tabella; un errore in una riga viene registrato e la riga saltata, il resto della tabella prosegue.

## Limiti

- La sorgente JSON è il formato di rehearsal/dry-run; l'adattatore MySQL legacy è il prossimo passo.
- Il preflight controlla FK e unique sulle tabelle presenti nella fixture; l'import reale deve partire da un dump sanificato completo.
- Gli utenti esistenti con username diverso ma stesso ID non vengono riconciliati automaticamente: il report segnala il conflitto.
