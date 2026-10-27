# infraorgit — guida per agenti

`AGENTS.md` è un symlink a questo file, letto da opencode e dagli altri agenti: modifica solo `CLAUDE.md`.

Leggi `../AGENTS.md` e `../PLAN.md`.

## Regole repo

- Migration solo con `dotnet ef migrations add <Nome>`; mai scritte o modificate a mano.
- Provider MySQL: `MySql.EntityFrameworkCore` 10.0.x. Non usare Pomelo (fermo a EF Core 9, fuori supporto).
- Niente repository generico: `DbContext` usato direttamente nei service di feature.
- Test di integrazione su Testcontainers MySQL; SQLite solo per logica pura.
- Contratto OpenAPI generato dal backend; il frontend consuma quello.
- Segreti solo user-secrets in sviluppo o secret manager; mai in `appsettings`.
- Audit remoto redatto: nessun segreto in chiaro.
- RBAC D7 = B: anagrafiche globali `SystemAdmin`; risorse d'ambiente `ServerAdmin` su tutti gli ambienti coinvolti; `SystemAdmin` non basta per server/console/run.
- Menu filtrato da `MenuRole`; CSRF su tutte le mutazioni.
- Cookie di sessione: HttpOnly, `SameSite=Lax`, `Secure` in produzione; nessun token in risposta.

## Stato

Fase 6: identity + auth BFF + inventario + topologia/diagrammi + gestione remota + admin/RBAC. Fase 7 completata sul perimetro disponibile: importer legacy con sorgente MySQL o JSON, dump fittizio in Docker e test sorgente → target; il rehearsal sui dati rappresentativi resta in `docs/migration/phase7-plan.md`. Checklist in `../AllInfraOrgIT/docs/domain/` (`inventory-parity`, `topology-diagrams`, `remote-management`, `admin-rbac`). Comandi reali nel README.
