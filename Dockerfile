FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src

ARG APP_VERSION=0.0.0

COPY global.json Directory.Build.props ./
COPY src/InfraOrgIt.Api/InfraOrgIt.Api.csproj src/InfraOrgIt.Api/
RUN dotnet restore src/InfraOrgIt.Api/InfraOrgIt.Api.csproj

COPY src/ src/
RUN dotnet publish src/InfraOrgIt.Api/InfraOrgIt.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false \
    /p:Version=${APP_VERSION#v} /p:InformationalVersion=${APP_VERSION#v}

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS final

ARG APP_VERSION=0.0.0
LABEL org.opencontainers.image.title="InfraOrgIT API" \
      org.opencontainers.image.description="API .NET di InfraOrgIT" \
      org.opencontainers.image.version=$APP_VERSION \
      org.opencontainers.image.source="https://github.com/SenseiKiritof/InfraOrgITApi"

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

RUN mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    APP_VERSION=$APP_VERSION

USER app
EXPOSE 8080

ENTRYPOINT ["dotnet", "InfraOrgIt.Api.dll"]
