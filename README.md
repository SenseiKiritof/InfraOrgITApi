# InfraOrgIT API

InfraOrgIT API is the backend and MCP server of InfraOrgIT. It owns the
inventory model, permissions, credentials, SSH/SFTP operations, audit log and
the tools an AI client can call.

The API has no separate user interface: the pages shown below are served by
[InfraOrgIT Web](https://github.com/SenseiKiritof/InfraOrgITWeb). Screenshots
use fictitious demo data.

## What it does

- **Inventory**: providers, environments, servers, applications, versions,
  deployments, proxy routes and projects, with the relationships the diagram
  and the project pages use.
- **Access control**: Keycloak login through the backend-for-frontend,
  `SystemAdmin` master data, `ServerAdmin`/`ServerOperator` per environment and
  a generated OpenAPI contract.
- **Remote operations**: SSH console, SFTP/FTPS file transfer, command
  templates, application actions and network reachability tests executed from
  the server.
- **MCP**: inventory tools, resources and prompts over `/mcp` (Streamable
  HTTP), for IDE assistants and agents.
- **Human approval**: an MCP action requested by an AI client needs a human
  decision before it runs.
- **Audit**: MCP calls, console sessions, actions, tests and tunnel events,
  with a tamper-evident hash chain and an optional external anchor.

## Connect an MCP client

1. Sign in to the web app and create a personal token from the user menu
   (`/tokens`). Choose `mcp:read` for read-only clients or
   `mcp:read mcp:operate` when the client must request actions. The token is
   shown once.
2. Point the MCP client at `https://<your-instance>/mcp` and send the token as
   a bearer token. OAuth clients can discover the resource metadata at
   `/.well-known/oauth-protected-resource` and use Keycloak for authorization
   instead of a personal token.
3. Read tools and resources work immediately. Action tools also need an
   `McpGrant` for the user or group, the environment operator right and human
   approval.

![Personal API tokens with scopes, creation, expiry, last use and state](docs/screenshots/tokens.png)

### Action approval

`confirm=true` is not a human confirmation. The action tools
`run_reachability_tests` and `run_application_action`:

1. check scope, MCP grant and environment role;
2. create a **Pending** approval bound to the user, action, server, target and
   parameters, and return `{ "status": "approval_required", "approvalId": … }`;
3. wait for the owner to approve or deny the request from `/approvals` in a
   browser session (cookie + CSRF, one-time, 10 minutes);
4. execute only when called again with the approved `approvalId`.

```mermaid
sequenceDiagram
    participant AI as MCP client
    participant API as InfraOrgIT API
    participant You as You (browser)

    AI->>API: run_reachability_tests(confirm=true)
    API-->>AI: approval_required + approvalId
    You->>API: POST /api/mcp/approvals/{id}/approve
    AI->>API: run_reachability_tests(approvalId)
    API-->>AI: result (approval used once)
```

`get_mcp_approval` returns the status and expiry of a request without executing
it, so a client can poll instead of retrying the action.

## Desktop sign-in

InfraOrgIT Desktop signs in with the Keycloak device flow on the public client
`infraorgit-mcp-desktop` (no client secret), then exchanges the refresh token
for a BFF cookie:

- `POST /auth/desktop-session` is anonymous and rate-limited per client IP.
  The body is `{ "refreshToken": "..." }`; the API redeems it at Keycloak with
  the public desktop client, validates the returned `id_token`, provisions the
  user and sets the usual HttpOnly session cookie. It returns `204`.
- An invalid, expired or revoked refresh token returns `401`
  `desktop_session_invalid`; an unreachable or failing Keycloak returns `502`
  `oidc_error`. Arbitrary access tokens are never accepted and `id_token`
  signature, issuer, audience and lifetime are always validated.
- The rotated refresh token stays server-side in the encrypted session; it is
  never returned to the caller or written to logs. The BFF session lives for
  `Auth:DesktopSessionDays` (default 7, valid 1..90), independently of the
  shorter Keycloak refresh-token lifetime. Calling the endpoint again with the
  same desktop cookie extends that same session, so the desktop can renew it
  periodically without creating orphan sessions.
- `Oidc:DesktopClientId` defaults to `infraorgit-mcp-desktop` and must match the
  public Keycloak client; the confidential BFF client is not used for this
  exchange.

## Session theme

- `GET /api/session` returns `theme` (`light`, `dark` or `system`).
- `PATCH /api/session/theme` requires the `Session` policy plus CSRF and Origin,
  accepts `{ "theme": "light|dark|system" }` and returns `204`.
- The preference is stored per user in `Users.Theme`; existing users default to
  `system`.

## Desktop downloads

The web UI can hand a server file to InfraOrgIT Desktop through a one-time
signed URL instead of relying on the embedded webview:

- `GET /api/desktop/downloads/capabilities` reports whether signed URLs are
  enabled.
- `POST /api/desktop/downloads/tickets` creates a short-lived ticket bound to
  the current user, server and path. It accepts only a cookie session with
  CSRF; an API token cannot create tickets.
- `GET /api/desktop/downloads/{token}` is anonymous by design because the
  ticket is the credential. It is one-time, expires, streams the file without
  holding a lock, and is audited.

Set `DesktopDownloads:Enabled=false` to disable new tickets and invalidate
already-issued ones; `TicketLifetimeSeconds` defaults to 120 and
`PublicBaseUrl` is needed only behind a proxy with a different origin. The
desktop can instead use its native webview download when the server disables
the bridge.

Tickets are metadata only: user, server, normalized path and timestamps; the
raw token and file content are never stored. For GDPR storage limitation,
`DesktopDownloads:RetentionDays` defaults to 30 and a background job deletes
expired ticket rows past that window (consumed or not).
`DesktopDownloads:CleanupIntervalMinutes` defaults to 1440 and the first pass
runs shortly after startup. Audit rows keep their separate 180-day retention
policy.

![MCP approvals page with a pending action, Approve and Deny buttons, and the human-approval warning](docs/screenshots/approvals.png)

## Run the API

The supported way to run the whole product is the published-image stack in
[AllInfraOrgIT](https://github.com/SenseiKiritof/AllInfraOrgIT):

```bash
git clone https://github.com/SenseiKiritof/AllInfraOrgIT.git
cd AllInfraOrgIT
cp deploy/.env.example deploy/.env   # replace the CHANGE_ME_* secrets and image tags
deploy/scripts/check-env.sh deploy/.env
make up-images
```

The API image runs the EF migrations. For local development with source,
Hot reload and the test suite, see [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md).

## API contract

The executable OpenAPI contract is
[`openapi/InfraOrgIt.Api.json`](openapi/InfraOrgIt.Api.json) (OpenAPI 3.1.1).
It is generated from the code; CI fails if it drifts. The domain draft and the
route inventory are in
[`../AllInfraOrgIT/docs/api`](../AllInfraOrgIT/docs/api).

## Learn more

- [InfraOrgIT Web](https://github.com/SenseiKiritof/InfraOrgITWeb) — the user
  interface.
- [`../AllInfraOrgIT/docs/security/permissions.md`](../AllInfraOrgIT/docs/security/permissions.md)
  — roles and environment permissions.
- [`../AllInfraOrgIT/docs/operations/mcp-tunnel-hardening.md`](../AllInfraOrgIT/docs/operations/mcp-tunnel-hardening.md)
  — MCP and tunnel hardening.
- [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) — stack, structure, commands,
  configuration, migrations, credential rewrap, security internals and
  observability.
