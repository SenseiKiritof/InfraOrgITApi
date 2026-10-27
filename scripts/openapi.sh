#!/usr/bin/env bash
# Genera docs/api OpenAPI dal codice. Config fittizia: non contatta DB né Keycloak.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE/.."

env ASPNETCORE_ENVIRONMENT=Testing \
  ConnectionStrings__MySql='Server=localhost;Database=infraorgit;User ID=ds;Password=ds;' \
  Oidc__Issuer='http://localhost:8080/realms/test' \
  Oidc__ClientId='test' \
  Oidc__ClientSecret='test' \
  Oidc__RedirectUri='http://localhost:3000/auth/callback' \
  Frontend__BaseUrl='http://localhost:3000' \
  ServerAccess__CurrentKeyVersion=1 \
  ServerAccess__CredentialKeys__1='AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=' \
  dotnet build src/InfraOrgIt.Api/InfraOrgIt.Api.csproj \
    -p:OpenApiGenerateDocuments=true \
    -p:OpenApiDocumentsDirectory="$PWD/openapi" -warnaserror
