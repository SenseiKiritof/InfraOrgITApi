# InfraOrgITApi

InfraOrgIT backend: .NET 10 API, Minimal APIs, EF Core 10, MySQL LTS, Keycloak.

## Status

Phase 7 complete on the available scope — 2026-10-26. Backend complete through Phase 6 (identity, inventory, topology/diagrams, remote management, admin/RBAC) plus the `tools/LegacyImport` importer: legacy MySQL source (`LEGACY_SOURCE_CONNECTION`) or legacy-shaped JSON, atomic dry-run/apply, IDs preserved with natural key checking, blocking collisions, two phases for FK cycles, credentials re-encrypted with a versioned key (`NeedsReauth` only with `--allow-reauth`), redacted audit, JSON report. The fake legacy dump is in Compose (`mysql-legacy`). Tests: 16 unit and 38 integration (real SSH included). Details in `tools/LegacyImport/README.md` and `../AllInfraOrgIT/docs/migration/phase7-plan.md`.

## Stack

- .NET 10 LTS, ASP.NET Core Minimal APIs
- EF Core 10 + `MySql.EntityFrameworkCore` (Oracle) 10.0.x
- Current MySQL LTS (8.4)
- Keycloak 26 OIDC, BFF with server-side session in memory
- xunit, Testcontainers MySQL, WebApplicationFactory

## Structure

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

## Commands

```bash
dotnet build -warnaserror
dotnet test
dotnet ef migrations add <Nome> --project src/InfraOrgIt.Api --startup-project src/InfraOrgIt.Api
./scripts/openapi.sh   # regenerates openapi/InfraOrgIt.Api.json; CI fails if it drifts
```

The OpenAPI contract is generated from the code into `openapi/InfraOrgIt.Api.json` (148 operations, 3.1.1); the meta repo's `docs/api/openapi.yaml` remains the domain draft.

To start and migrate with the Compose infra, use the meta repo:

```bash
cd ../AllInfraOrgIT
make dev       # infra + API :5298 + Vite :3000
make migrate
make test
make legacy-rehearsal  # dry-run importer against the fake legacy dump
```

`dotnet ef` is in the meta repo's `.config/dotnet-tools.json` manifest; alternatively `dotnet tool restore` from the workspace root.

## Local configuration

- `appsettings.Development.json` contains non-secret values only.
- `Oidc:ClientSecret` and `ConnectionStrings:MySql` come from env vars (`Oidc__ClientSecret`, `ConnectionStrings__MySql`) via `scripts/env.sh`.
- `ServerAccess:CredentialKeys:<n>` (32-byte base64) and `ServerAccess:CurrentKeyVersion` encrypt the SSH credentials. In development `scripts/env.sh` generates the key in the ignored `.env`; without keys the API does not start.
- SystemAdmin bootstrap: `Auth:BootstrapAdminUsername=admin` in Development; the first login of the Keycloak user `admin` assigns the role if a SystemAdmin does not exist yet. In production the variable stays empty.
- Sessions in MySQL (`Sessions`) with payload encrypted via DataProtection, expiry and per-user limit (`SessionStore:MaxSessionsPerUser`, default 10). DataProtection keys live in the `api-dp-keys` volume in production (`DataProtection:KeysPath`); in development the user profile is used.

## Auth BFF

- `GET /auth/login` → Keycloak redirect with PKCE S256.
- `GET /auth/callback` → code exchange, user JIT provisioning, HttpOnly session cookie.
- `POST /auth/logout` → revokes Keycloak and deletes the session; requires CSRF.
- `GET /api/session` → user, expiry, CSRF token; renews the token server-side.
- Mutations: `X-CSRF-Token` header + allowed `Origin`; `SameSite=Lax`.
- No token is returned to the browser or readable from JavaScript.

## HTTP security

- Headers on API and SPA: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, CSP and HSTS when the request arrives over HTTPS.
- Forwarded headers accepted only from the networks in `ForwardedHeaders:KnownNetworks` (default loopback; Compose passes the private Docker networks).
- Rate limits: login 120/min per IP, callback 30/min per IP, remote actions 30/min per user, reachability 10/min per user, console 5/min per user.
- Authenticated mutations require `Origin` among `Frontend:AllowedOrigins` (or `Frontend:BaseUrl`); missing or different Origin = 403 `origin_failed`.

## Observability

- Outside Development logs are JSON on console, with scopes and `X-Correlation-ID` (client header or `TraceIdentifier`); the correlation id also appears in the response.
- OpenTelemetry (ASP.NET Core/HttpClient traces and metrics) is enabled only by setting `OTEL_EXPORTER_OTLP_ENDPOINT`; no embedded collector.
- `/health/live` checks the process only; `/health/ready` checks MySQL and Keycloak discovery and answers JSON with the check status.
- In production EF SQL commands are filtered to `Warning`.

## References

- `../AllInfraOrgIT/docs/security/permissions.md`
- ADR 003, 005, 007, 008

## Docker image

The `Dockerfile` builds a non-root image on `aspnet:10.0`.

- workflow `.github/workflows/docker.yml` on `v*` tags
- image `ghcr.io/senseikiritof/infraorgit-api`, tags `X.Y.Z`, `X.Y`, `sha-<commit>`, `latest`
- local build: `docker build --build-arg VERSION=v0.8.0 -t infraorgit-api:0.8.0 .`
- version: `org.opencontainers.image.version` label, `APP_VERSION` env var, returned by `GET /`
