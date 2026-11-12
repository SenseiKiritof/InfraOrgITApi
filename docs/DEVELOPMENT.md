# InfraOrgIT API — development

Technical notes for the API. The user-facing overview is in
[`../README.md`](../README.md).

InfraOrgIT backend: .NET 10 API, Minimal APIs, EF Core 10, MySQL LTS, Keycloak.

## Status

Phase 7 complete on the available scope — 2026-10-26. Backend complete through Phase 6 (identity, inventory, topology/diagrams, remote management, admin/RBAC) plus the `tools/LegacyImport` importer: legacy MySQL source (`LEGACY_SOURCE_CONNECTION`) or legacy-shaped JSON, atomic dry-run/apply, IDs preserved with natural key checking, blocking collisions, two phases for FK cycles, credentials re-encrypted with a versioned key (`NeedsReauth` only with `--allow-reauth`), redacted audit, JSON report. Tests: 16 unit and 38 integration (real SSH included). Details in `tools/LegacyImport/README.md` and `../AllInfraOrgIT/docs/migration/phase7-plan.md`.

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
tools/RewrapCredentials/
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
dotnet run --project tools/RewrapCredentials -- --self-check
```

The OpenAPI contract is generated from the code into `openapi/InfraOrgIt.Api.json` (148 operations, 3.1.1); the meta repo's `docs/api/openapi.yaml` remains the domain draft.

To start and migrate with the Compose infra, use the meta repo:

```bash
cd ../AllInfraOrgIT
make dev       # infra + API :5298 + Vite :3000
make migrate
make test
make legacy-dry-run  # dry-run importer on the JSON fixtures
```

`dotnet ef` is in the meta repo's `.config/dotnet-tools.json` manifest; alternatively `dotnet tool restore` from the workspace root.

## Local configuration

- `appsettings.Development.json` contains non-secret values only.
- `Oidc:ClientSecret` and `ConnectionStrings:MySql` come from env vars (`Oidc__ClientSecret`, `ConnectionStrings__MySql`) via `scripts/env.sh`.
- `ServerAccess:CredentialKeys:<n>` (32-byte base64) and `ServerAccess:CurrentKeyVersion` encrypt the SSH credentials with SSecureUtils 1.1.1 (AES-GCM, `v2:` prefix, key derived with SHA-256). In development `scripts/env.sh` generates the key in the ignored `.env`; without keys the API does not start.
- Encrypted configuration: `appsettings.<Environment>.Secure.json` is loaded when the file exists and every value is decrypted with `ENC_KEY` (SSecureUtils `AddSecureConfig`); if the file exists and `ENC_KEY` is missing the API does not start. Production mounts the file read-only, it is never baked into the image.
- SystemAdmin bootstrap: `Auth:BootstrapAdminUsername=admin` in Development; the first login of the Keycloak user `admin` assigns the role if a SystemAdmin does not exist yet. In production the variable stays empty.
- Sessions in MySQL (`Sessions`) with payload encrypted via DataProtection, expiry and per-user limit (`SessionStore:MaxSessionsPerUser`, default 10). DataProtection keys live in the `api-dp-keys` volume in production (`DataProtection:KeysPath`); in development the user profile is used.

## Credential rewrap

`tools/RewrapCredentials` converts credentials written with the pre-SSecureUtils format (`base64(nonce|tag|ciphertext)`, raw key) to the current one (`v2:` prefix, key derived with SHA-256). It is idempotent: rows already carrying `v2:` are skipped.

```bash
REWRAP_CONNECTION='Server=localhost;Port=3306;Database=infraorgit;User ID=...;Password=...;' \
ServerAccess__CredentialKeys__1=<32-byte base64> ServerAccess__CurrentKeyVersion=1 \
dotnet run --project tools/RewrapCredentials            # dry-run
# add --apply to write
```

Keys for every `KeyVersion` present in the table must be provided; secrets only from environment variables.

## Auth BFF

- `GET /auth/login` → Keycloak redirect with PKCE S256.
- `GET /auth/callback` → code exchange, user JIT provisioning, HttpOnly session cookie.
- `POST /auth/logout` → revokes Keycloak and deletes the session; requires CSRF.
- `GET /api/session` → user, expiry, CSRF token; renews the token server-side.
- Mutations: `X-CSRF-Token` header + allowed `Origin`; `SameSite=Lax`.
- No token is returned to the browser or readable from JavaScript.

## MCP human approval (PT-18)

`confirm=true` alone does not execute an MCP action: it only creates a Pending
approval, because a model-supplied boolean is not a human confirmation.

- `run_reachability_tests` and `run_application_action` keep the `mcp:operate`
  scope, the MCP grant and the environment `ServerOperator`/`ServerAdmin`
  checks, then return
  `{ "status": "approval_required", "approvalId": 1, "expiresAt": "...", "message": "..." }`
  without executing anything.
- Only the owner decides from a cookie session:
  `GET /api/mcp/approvals[?status=Pending]`,
  `POST /api/mcp/approvals/{id}/approve`,
  `POST /api/mcp/approvals/{id}/deny`. The group uses the `Session` policy, so
  an MCP bearer token cannot approve, and the mutations require CSRF + Origin.
- To execute, call the tool again with `approvalId`. The approval must be
  Approved and unexpired, and must match the user, tool, server, deployment,
  application action and parameters (including the idempotency key). It is
  consumed atomically and can be used once.
- Approvals expire after 10 minutes; Pending and Approved rows past their
  expiry are marked `Expired` when read. A failed execution still consumes the
  approval: request a new one.
- `get_mcp_approval` returns status/expiry/target for the caller without
  executing; clients poll it instead of retrying the action.
- Approval creation and decisions are audited as `AdminChange`
  (`mcp_approval_request`, `mcp_approval_approve`, `mcp_approval_deny`);
  rejected tool calls stay in `McpToolCall` with `approval_*` error codes.
- Breaking change for existing MCP clients: an action call that used to run on
  `confirm=true` now returns `approval_required` and needs the second call
  with `approvalId`.

## Desktop download tickets

`DesktopDownloads` (default `Enabled=true`, `TicketLifetimeSeconds=120`,
optional `PublicBaseUrl`) backs the desktop bridge:

- `GET /api/desktop/downloads/capabilities` is available to a cookie session,
  a personal API token or an OIDC bearer and returns `{ signedUrl }`.
- `POST /api/desktop/downloads/tickets` requires the `Session` policy, CSRF and
  the same server file-transfer authorization; it stores only a SHA-256 hash
  of a random `dt_` token and returns an absolute one-time URL.
- `GET /api/desktop/downloads/{token}` is anonymous: the ticket is the
  credential. It re-checks the stored user's RBAC, atomically consumes the
  ticket, streams with the existing connector and audits the download. A
  consumed, expired or disabled ticket is rejected; `Enabled=false` also
  invalidates already-issued tickets.
- The anonymous route is rate-limited per client IP and deliberately has no
  authentication requirement in OpenAPI. The raw token is visible in the URL
  path, so keep the lifetime short and avoid proxy access-log retention.
- A failed connector after consuming the ticket requires a new ticket; this is
  intentional anti-replay behavior. Old rows are not purged yet; the
  `(ExpiresAt, ConsumedAt)` index supports a future cleanup job.

## Audit anchoring (PT-10)

The `AuditEvents` hash chain is tamper-evident only inside this database. When
`Audit:Anchor:Enabled` is true, a hosted service reads the newest hashed row
every `Audit:Anchor:IntervalMinutes` (default 5) and POSTs:

```json
{
  "instanceId": "api-1",
  "lastId": 123,
  "chainHead": "…",
  "occurredAt": "2026-11-12T10:00:00Z",
  "checked": "2026-11-12T10:05:00Z"
}
```

- `Audit:Anchor:WebhookUrl` must be an absolute HTTPS URL; HTTP is rejected.
- The body is signed with HMAC-SHA256 using `Audit:Anchor:HmacKey` and sent as
  `X-InfraOrgIT-Signature: sha256=<hex>`. Verify the signature over the raw
  request body before parsing it.
- `Audit:Anchor:InstanceId` identifies the API instance in the sink.
- No audit detail or secret is exported. An empty chain is skipped; a webhook
  failure is logged and retried at the next interval without stopping the API.
- Keep `HmacKey` in a secret manager or the encrypted
  `appsettings.<Environment>.Secure.json`, never in `appsettings.json`.

Point the webhook at an **append-only SIEM or WORM sink** (for example an
object-locked bucket with no overwrite/delete during the retention period).
That external copy is what makes a later rewrite or deletion of the local
chain detectable. The API alone still cannot guarantee WORM, and retention
still removes old local rows.

## Plain FTP switch (PT-16)

`FileTransfer:AllowPlainFtp` defaults to `true` for development. Plain FTP is
cleartext: username, password, commands and file content cross the network
unencrypted. Deploy sets `FileTransfer__AllowPlainFtp=false`; saving `Ftp` and
connecting with an already-saved `Ftp` server then both fail with 422
`plain_ftp_disabled`. `Sftp` and `Ftps` are unaffected. Prefer SFTP, or FTPS
when the target only offers FTP.

## HTTP security

- Headers on API and SPA: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, CSP and HSTS when the request arrives over HTTPS.
- Forwarded headers accepted only from the networks in `ForwardedHeaders:KnownNetworks` (default loopback; Compose passes the private Docker networks).
- Rate limits: login 120/min per IP, callback 30/min per IP, remote actions 30/min per user, reachability 10/min per user, console 5/min per user.
- Authenticated mutations require `Origin` among `Frontend:AllowedOrigins` (or `Frontend:BaseUrl`); missing or different Origin = 403 `origin_failed`.

## File transfer and audit integrity

- File transfer protocols: `Sftp` (preferred), `Ftps` (explicit TLS on control and data connections, certificate validation on) and `Ftp` (cleartext, legacy exception).
- Every new `AuditEvents` row carries `PrevHash`/`Hash`: SHA-256 over the canonical audit fields plus the previous hash. Writes are serialized in one API process with a static semaphore; a multi-instance deployment needs a database lock or a single-writer queue.
- `GET /api/audit-events/verify` (SystemAdmin) recomputes the chain and returns `valid`, `checked`, `firstBrokenId` and `message`. Legacy rows with a null hash are skipped.
- The hash chain is tamper-evident, not WORM: a database writer can rewrite a suffix by recomputing hashes, and retention deletes older rows. Verification starts at the oldest remaining hashed row, so a deleted prefix or suffix is not detected by the chain alone; export to a SIEM/WORM store for stronger guarantees.

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
