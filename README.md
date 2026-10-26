# infraorgit

Backend di InfraOrgIT: API .NET 10, Minimal APIs, EF Core 10, MySQL LTS, Keycloak.

## Stato

Fase 7 in corso — 2026-10-26. Backend completo fino a Fase 6 (identity, inventario, topologia/diagrammi, gestione remota, admin/RBAC) e importer `tools/LegacyImport`: dry-run/apply su JSON legacy-shaped, ID preservati, due fasi per i cicli FK, credenziali ricifrate o `NeedsReauth`, audit redatto, report JSON. Test: 12 unitari e 25 di integrazione (SSH reale incluso). Dettagli in `tools/LegacyImport/README.md` e `../AllInfraOrgIT/docs/migration/phase7-plan.md`.

## Stack

- .NET 10 LTS, ASP.NET Core Minimal APIs
- EF Core 10 + `MySql.EntityFrameworkCore` (Oracle) 10.0.x
- MySQL LTS corrente (8.4)
- Keycloak 26 OIDC, BFF con sessione server-side in memoria
- xunit, Testcontainers MySQL, WebApplicationFactory

## Struttura

```
src/InfraOrgIt.Api/
  BuildingBlocks/{Auth, Authorization, Errors, Pagination, Validation}
  Features/{Auth, Session, Menus, ServerAccess, Users, Inventory, Catalog, Topology, Remote, Admin}
  Infrastructure/Ssh
tools/LegacyImport/
  Infrastructure/Persistence/{Entities, Migrations}
  Program.cs
tests/InfraOrgIt.UnitTests/
tests/InfraOrgIt.IntegrationTests/
```

## Comandi

```bash
dotnet build -warnaserror
dotnet test
dotnet ef migrations add <Nome> --project src/InfraOrgIt.Api --startup-project src/InfraOrgIt.Api
```

Per avvio e migrazioni con l'infra Compose usare il meta repo:

```bash
cd ../AllInfraOrgIT
make dev       # infra + API :5298 + Vite :3000
make migrate
make test
```

`dotnet ef` è nel manifest `.config/dotnet-tools.json` del meta repo; in alternativa `dotnet tool restore` dalla root del workspace.

## Configurazione locale

- `appsettings.Development.json` contiene solo valori non segreti.
- `Oidc:ClientSecret` e `ConnectionStrings:MySql` arrivano da env (`Oidc__ClientSecret`, `ConnectionStrings__MySql`) tramite `scripts/env.sh`.
- `ServerAccess:CredentialKeys:<n>` (base64 di 32 byte) e `ServerAccess:CurrentKeyVersion` cifrano le credenziali SSH. In sviluppo `scripts/env.sh` genera la chiave nel `.env` ignorato; senza chiavi l'API non parte.
- Bootstrap SystemAdmin: `Auth:BootstrapAdminUsername=admin` in Development; il primo login dell'utente Keycloak `admin` assegna il ruolo se non esiste già un SystemAdmin. In produzione la variabile resta vuota.
- Cookie di sessione in memoria: adatto a una singola istanza; lo store condiviso è una decisione di Fase 8.

## Auth BFF

- `GET /auth/login` → redirect Keycloak con PKCE S256.
- `GET /auth/callback` → scambio codice, JIT provisioning utente, cookie di sessione HttpOnly.
- `POST /auth/logout` → revoca Keycloak e cancella la sessione; richiede CSRF.
- `GET /api/session` → utente, scadenza, CSRF token; rinnova il token lato server.
- Mutazioni: header `X-CSRF-Token` + `Origin` ammessa; `SameSite=Lax`.
- Nessun token è restituito al browser o leggibile da JavaScript.

## Riferimenti

- `../PLAN.md` §5.3, §5.5, §5.8
- `../AllInfraOrgIT/docs/security/permissions.md`
- ADR 003, 005, 007, 008
