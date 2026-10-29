using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.Features.Admin;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Features.Catalog;
using InfraOrgIt.Api.Features.Inventory;
using InfraOrgIt.Api.Features.Menus;
using InfraOrgIt.Api.Features.Remote;
using InfraOrgIt.Api.Features.ServerAccess;
using InfraOrgIt.Api.Features.Session;
using InfraOrgIt.Api.Features.Topology;
using InfraOrgIt.Api.Features.Users;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Health;
using InfraOrgIt.Api.Infrastructure.Ssh;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SSecureUtils.Extensions;
using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var secureSettingsFile = $"appsettings.{builder.Environment.EnvironmentName}.Secure.json";
var secureSettingsPath = Path.Combine(builder.Environment.ContentRootPath, secureSettingsFile);
if (File.Exists(secureSettingsPath))
{
    var encryptionKey = builder.Configuration["ENC_KEY"];
    if (string.IsNullOrWhiteSpace(encryptionKey))
        throw new InvalidOperationException($"{secureSettingsFile} esiste ma ENC_KEY non è impostata.");

    builder.Configuration.AddSecureConfig(secureSettingsFile, encryptionKey);
}

if (!builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    });
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
}

var connectionString = builder.Configuration.GetConnectionString("MySql")
                       ?? throw new InvalidOperationException(
                           "ConnectionStrings:MySql non configurata. Usare env ConnectionStrings__MySql o user-secrets.");

builder.Services.AddDbContext<InfraOrgItDbContext>(options => options.UseMySQL(connectionString));
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        var components = document.Components ??= new OpenApiComponents();
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes["session"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Cookie,
            Name = "infraorgit_session",
            Description = "Cookie di sessione BFF (HttpOnly); in produzione prefisso __Host-."
        };
        components.SecuritySchemes["csrf"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-CSRF-Token",
            Description = "Token CSRF richiesto dalle mutazioni autenticate."
        };

        var session = new OpenApiSecuritySchemeReference("session", document);
        var csrfScheme = new OpenApiSecuritySchemeReference("csrf", document);

        foreach (var path in document.Paths)
        {
            if (path.Key == "/" ||
                path.Key.StartsWith("/auth/", StringComparison.Ordinal) ||
                path.Key.StartsWith("/health/", StringComparison.Ordinal))
                continue;

            if (path.Value is null)
                continue;

            var operations = path.Value.Operations;
            if (operations is null)
                continue;

            foreach (var operation in operations)
            {
                var requirement = new OpenApiSecurityRequirement { [session] = [] };
                if (operation.Key == HttpMethod.Post || operation.Key == HttpMethod.Put ||
                    operation.Key == HttpMethod.Patch || operation.Key == HttpMethod.Delete)
                    requirement[csrfScheme] = [];

                operation.Value.Security ??= [];
                operation.Value.Security.Add(requirement);
            }
        }

        return Task.CompletedTask;
    });
});
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<InfraOrgItDbContext>("mysql", tags: ["ready"])
    .AddCheck<KeycloakHealthCheck>("keycloak", tags: ["ready"]);

if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("infraorgit"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter());
}

var dataProtection = builder.Services.AddDataProtection().SetApplicationName("InfraOrgIT");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddScoped<ISessionStore, MySqlSessionStore>();
builder.Services.AddOptions<SessionStoreOptions>()
    .Bind(builder.Configuration.GetSection(SessionStoreOptions.SectionName))
    .Validate(o => o.MaxSessionsPerUser is >= 1 and <= 100, "MaxSessionsPerUser tra 1 e 100.")
    .ValidateOnStart();
builder.Services.AddSingleton<LoginStateProtector>();
builder.Services.AddScoped<UserProvisioningService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<EnvironmentAccessService>();
builder.Services.AddSingleton<ConsoleSessionRegistry>();
builder.Services.AddScoped<TopologyService>();
builder.Services.AddScoped<DiagramService>();
builder.Services.AddHttpClient<OidcClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("application-api", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddHttpClient("application-api-insecure")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(sp =>
{
    var oidc = sp.GetRequiredService<IOptions<OidcOptions>>().Value;
    return new ConfigurationManager<OpenIdConnectConfiguration>(
        oidc.DiscoveryEndpoint,
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever
        {
            RequireHttps = !builder.Environment.IsDevelopment() && !oidc.AllowInsecureInternalDiscovery
        });
});
builder.Services.AddSingleton<IOidcIdentityValidator, OidcIdentityValidator>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth-login", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
    options.AddPolicy("auth-callback", context => Partition(ClientKey(context), 30));
    options.AddPolicy("remote-run", context => Partition(UserKey(context), 30));
    options.AddPolicy("reachability", context => Partition(UserKey(context), 10));
    options.AddPolicy("console", context => Partition(UserKey(context), 5));
});

var forwardedNetworks = builder.Configuration["ForwardedHeaders:KnownNetworks"];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    if (string.IsNullOrWhiteSpace(forwardedNetworks))
        return;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var network in forwardedNetworks.Split(
                 ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection("Auth"));
builder.Services.AddOptions<OidcOptions>()
    .Bind(builder.Configuration.GetSection("Oidc"))
    .Validate(o => Uri.TryCreate(o.Issuer, UriKind.Absolute, out _), "Oidc:Issuer non è un URI assoluto.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Oidc:ClientId mancante.")
    .Validate(o => Uri.TryCreate(o.RedirectUri, UriKind.Absolute, out _), "Oidc:RedirectUri non è un URI assoluto.")
    .Validate(o => string.IsNullOrWhiteSpace(o.InternalIssuer) ||
                   Uri.TryCreate(o.InternalIssuer, UriKind.Absolute, out _),
        "Oidc:InternalIssuer non è un URI assoluto.")
    .ValidateOnStart();
builder.Services.AddOptions<ServerAccessOptions>()
    .Bind(builder.Configuration.GetSection(ServerAccessOptions.SectionName))
    .Validate(o => o.CredentialKeys.Count > 0, "ServerAccess:CredentialKeys mancante.")
    .Validate(o => o.CredentialKeys.Values.All(IsCredentialKey), "Ogni CredentialKeys deve essere base64 di 32 byte.")
    .Validate(o => o.CredentialKeys.ContainsKey(o.CurrentKeyVersion.ToString()),
        "ServerAccess:CurrentKeyVersion non ha una chiave corrispondente.")
    .Validate(o => o.CommandTimeoutMinutes is >= 1 and <= 60, "CommandTimeoutMinutes tra 1 e 60.")
    .Validate(o => o.MaxOutputBytes is >= 1_024 and <= 10_485_760, "MaxOutputBytes tra 1 KB e 10 MB.")
    .Validate(o => o.MaxAuditOutputBytes is >= 1_024 && o.MaxAuditOutputBytes <= o.MaxOutputBytes,
        "MaxAuditOutputBytes tra 1 KB e MaxOutputBytes.")
    .ValidateOnStart();
builder.Services.AddSingleton<CredentialCipher>();
builder.Services.AddScoped<ISshConnector, SshConnector>();
builder.Services.AddOptions<FrontendOptions>()
    .Bind(builder.Configuration.GetSection("Frontend"))
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Frontend:BaseUrl non è un URI assoluto.")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

if (args.Contains("migrate", StringComparer.Ordinal))
{
    using var migrateScope = app.Services.CreateScope();
    await migrateScope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.MigrateAsync();
    Console.WriteLine("Migrazioni applicate.");
    return;
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.Migrate();
}

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.IsHttps)
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

    await next(context);
});

app.Use(async (context, next) =>
{
    var incoming = context.Request.Headers["X-Correlation-ID"].ToString();
    var correlationId = incoming is { Length: > 0 and <= 64 } &&
                        incoming.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
        ? incoming
        : context.TraceIdentifier;

    context.TraceIdentifier = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    Activity.Current?.SetTag("correlation_id", correlationId);
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        await next(context);
});

app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
    }

    await next(context);
});
app.UseWebSockets();
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    name = "InfraOrgIT API",
    status = "ok",
    version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev"
})).AllowAnonymous();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = Program.WriteHealth
}).AllowAnonymous();

AuthEndpoints.Map(app);
SessionEndpoints.Map(app);
MenuEndpoints.Map(app);
ServerAccessEndpoints.Map(app);
ProvidersEndpoints.Map(app);
EnvironmentDefinitionsEndpoints.Map(app);
ProviderEnvironmentsEndpoints.Map(app);
ServersEndpoints.Map(app);
ServerApplicationsEndpoints.Map(app);
ApplicationsEndpoints.Map(app);
ApplicationEnvironmentsEndpoints.Map(app);
ApplicationApiKeysEndpoints.Map(app);
ApplicationApiCallEndpoints.Map(app);
ApiCallsEndpoints.Map(app);
ApplicationVersionsEndpoints.Map(app);
ApplicationVersionApisEndpoints.Map(app);
ApplicationApiQueryParamsEndpoints.Map(app);
ProjectsEndpoints.Map(app);
TopologyEndpoints.Map(app);
DiagramEndpoints.Map(app);
ServerCredentialsEndpoints.Map(app);
CommandTemplateEndpoints.Map(app);
ApplicationActionsEndpoints.Map(app);
ReachabilityEndpoints.Map(app);
CommandExecutionEndpoints.Map(app);
ConsoleEndpoints.Map(app);
UsersEndpoints.Map(app);
GroupsEndpoints.Map(app);
RolesEndpoints.Map(app);
MenusAdminEndpoints.Map(app);
ProviderEnvironmentGroupRolesEndpoints.Map(app);
ProxyEndpoints.Map(app);

app.Run();

public partial class Program
{
    private static RateLimitPartition<string> Partition(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });

    private static string ClientKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserKey(HttpContext context) =>
        context.User.Identity?.Name ?? ClientKey(context);

    private static Task WriteHealth(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString()
            })
        });
    }

    private static bool IsCredentialKey(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
