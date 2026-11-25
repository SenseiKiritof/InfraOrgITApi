FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
WORKDIR /src

ARG APP_VERSION=0.1.0

COPY global.json Directory.Build.props ./
COPY src/InfraOrgIt.Api/InfraOrgIt.Api.csproj src/InfraOrgIt.Api/
RUN dotnet restore src/InfraOrgIt.Api/InfraOrgIt.Api.csproj

COPY src/ src/
RUN dotnet publish src/InfraOrgIt.Api/InfraOrgIt.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false \
    /p:Version=${APP_VERSION#v} /p:InformationalVersion=${APP_VERSION#v}

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21 AS final

ARG APP_VERSION=0.1.0
LABEL org.opencontainers.image.title="InfraOrgIT API" \
      org.opencontainers.image.description="API .NET di InfraOrgIT" \
      org.opencontainers.image.version=$APP_VERSION \
      org.opencontainers.image.source="https://github.com/SenseiKiritof/InfraOrgITApi"

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
