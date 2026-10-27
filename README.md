# InfraOrgITApi

Backend di InfraOrgIT: API .NET 10, Minimal APIs, EF Core 10, MySQL LTS, Keycloak.

## Stato

Fase 7 completata sul perimetro disponibile — 2026-10-26. Backend completo fino a Fase 6 (identity, inventario, topologia/diagrammi, gestione remota, admin/RBAC) e importer `tools/LegacyImport`: sorgente MySQL legacy (`LEGACY_SOURCE_CONNECTION`) o JSON legacy-shaped, dry-run/apply atomico, ID preservati con controllo di chiave naturale, collisioni bloccanti, due fasi per i cicli FK, credenziali ricifrate con chiave versionata (`NeedsReauth` solo con `--allow-reauth`), audit redatto, report JSON. Il dump legacy fittizio è nel Compose (`mysql-legacy`). Test: 16 unitari e 38 di integrazione (SSH reale incluso). Dettagli in `tools/LegacyImport/README.md` e `../AllInfraOrgIT/docs/migration/phase7-plan.md`.

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
./scripts/openapi.sh   # rigenera openapi/InfraOrgIt.Api.json; la CI fallisce se è driftato
```

Il contratto OpenAPI è generato dal codice in `openapi/InfraOrgIt.Api.json` (148 operazioni, 3.1.1); `docs/api/openapi.yaml` del meta repo resta il draft di dominio.

Per avvio e migrazioni con l'infra Compose usare il meta repo:

```bash
cd ../AllInfraOrgIT
make dev       # infra + API :5298 + Vite :3000
make migrate
make test
make legacy-rehearsal  # dry-run importer contro il dump legacy fittizio
```

`dotnet ef` è nel manifest `.config/dotnet-tools.json` del meta repo; in alternativa `dotnet tool restore` dalla root del workspace.

## Configurazione locale

- `appsettings.Development.json` contiene solo valori non segreti.
- `Oidc:ClientSecret` e `ConnectionStrings:MySql` arrivano da env (`Oidc__ClientSecret`, `ConnectionStrings__MySql`) tramite `scripts/env.sh`.
- `ServerAccess:CredentialKeys:<n>` (base64 di 32 byte) e `ServerAccess:CurrentKeyVersion` cifrano le credenziali SSH. In sviluppo `scripts/env.sh` genera la chiave nel `.env` ignorato; senza chiavi l'API non parte.
- Bootstrap SystemAdmin: `Auth:BootstrapAdminUsername=admin` in Development; il primo login dell'utente Keycloak `admin` assegna il ruolo se non esiste già un SystemAdmin. In produzione la variabile resta vuota.
- Sessioni in MySQL (`Sessions`) con payload cifrato via DataProtection, scadenza e limite per utente (`SessionStore:MaxSessionsPerUser`, default 10). Le chiavi DataProtection stanno nel volume `api-dp-keys` in produzione (`DataProtection:KeysPath`); in sviluppo si usa il profilo utente.

## Auth BFF

- `GET /auth/login` → redirect Keycloak con PKCE S256.
- `GET /auth/callback` → scambio codice, JIT provisioning utente, cookie di sessione HttpOnly.
- `POST /auth/logout` → revoca Keycloak e cancella la sessione; richiede CSRF.
- `GET /api/session` → utente, scadenza, CSRF token; rinnova il token lato server.
- Mutazioni: header `X-CSRF-Token` + `Origin` ammessa; `SameSite=Lax`.
- Nessun token è restituito al browser o leggibile da JavaScript.

## Sicurezza HTTP

- Header su API e SPA: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, CSP e HSTS quando la richiesta arriva in HTTPS.
- Forwarded headers accettati solo dalle reti in `ForwardedHeaders:KnownNetworks` (default loopback; il Compose passa le reti private Docker).
- Rate limit: login 120/min per IP, callback 30/min per IP, azioni remote 30/min per utente, reachability 10/min per utente, console 5/min per utente.
- Le mutazioni autenticate richiedono `Origin` tra `Frontend:AllowedOrigins` (o `Frontend:BaseUrl`); Origin mancante o diversa = 403 `origin_failed`.

## Osservabilità

- Fuori da Development i log sono JSON su console, con scope e `X-Correlation-ID` (header del client o `TraceIdentifier`); il correlation id compare anche nella risposta.
- OpenTelemetry (tracce ASP.NET Core/HttpClient e metriche) si attiva solo impostando `OTEL_EXPORTER_OTLP_ENDPOINT`; nessun collector embedded.
- `/health/live` controlla solo il processo; `/health/ready` controlla MySQL e la discovery Keycloak e risponde JSON con lo stato dei check.
- In produzione i comandi SQL EF sono filtrati a `Warning`.

## Riferimenti

- `../AllInfraOrgIT/docs/security/permissions.md`
- ADR 003, 005, 007, 008

## Immagine Docker

Il `Dockerfile` costruisce un'immagine non-root su `aspnet:10.0`.

- workflow `.github/workflows/docker.yml` sui tag `v*`
- immagine `ghcr.io/senseikiritof/infraorgit-api`, tag `X.Y.Z`, `X.Y`, `sha-<commit>`, `latest`
- build locale: `docker build --build-arg VERSION=v0.8.0 -t infraorgit-api:0.8.0 .`
- versione: label `org.opencontainers.image.version`, env `APP_VERSION`, restituita da `GET /`
