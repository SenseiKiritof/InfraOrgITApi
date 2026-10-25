# infraorgit

Backend di InfraOrgIT: API .NET 10, Minimal APIs, EF Core 10, MySQL LTS, Keycloak.

## Stato

Fase 0 — solo documentazione e CI skeleton. Nessun progetto, build o test disponibile.

## Stack

- .NET 10 LTS, ASP.NET Core Minimal APIs
- EF Core 10 + `MySql.EntityFrameworkCore` (Oracle) 10.0.x
- MySQL LTS corrente (8.4)
- Keycloak 26 OIDC, BFF con sessione server-side
- SSH.NET per la gestione remota
- xunit, Testcontainers MySQL, WebApplicationFactory
- Serilog + OpenTelemetry

## Struttura prevista

```
src/InfraOrgIt.Api/
  Features/<Feature>/{Endpoints.cs, Contracts.cs, Service.cs, Validator.cs}
  BuildingBlocks/{Auth, Authorization, Errors, Pagination, Audit, Time}
  Infrastructure/{Persistence, Ssh, Crypto}
  Program.cs
tests/InfraOrgIt.UnitTests/
tests/InfraOrgIt.IntegrationTests/
tools/LegacyImport/
```

## Comandi

Non ancora disponibili. Arrivano con lo scaffolding della Fase 2 e verranno documentati qui.

## Riferimenti

- `../PLAN.md` §5.3, §5.5, §5.8, §5.9
- `../AllInfraOrgIT/docs/adr/`
