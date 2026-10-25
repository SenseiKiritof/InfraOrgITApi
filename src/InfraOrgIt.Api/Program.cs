using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.Features.Auth;
using InfraOrgIt.Api.Features.Menus;
using InfraOrgIt.Api.Features.ServerAccess;
using InfraOrgIt.Api.Features.Session;
using InfraOrgIt.Api.Features.Users;
using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MySql")
                       ?? throw new InvalidOperationException(
                           "ConnectionStrings:MySql non configurata. Usare env ConnectionStrings__MySql o user-secrets.");

builder.Services.AddDbContext<InfraOrgItDbContext>(options => options.UseMySQL(connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<InfraOrgItDbContext>("mysql");

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<LoginStateStore>();
builder.Services.AddScoped<UserProvisioningService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddHttpClient<OidcClient>(client => client.Timeout = TimeSpan.FromSeconds(15));

builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection("Auth"));
builder.Services.AddOptions<OidcOptions>()
    .Bind(builder.Configuration.GetSection("Oidc"))
    .Validate(o => Uri.TryCreate(o.Issuer, UriKind.Absolute, out _), "Oidc:Issuer non è un URI assoluto.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Oidc:ClientId mancante.")
    .Validate(o => Uri.TryCreate(o.RedirectUri, UriKind.Absolute, out _), "Oidc:RedirectUri non è un URI assoluto.")
    .ValidateOnStart();
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

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<InfraOrgItDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<CsrfMiddleware>();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { name = "InfraOrgIT API", status = "ok" })).AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

AuthEndpoints.Map(app);
SessionEndpoints.Map(app);
MenuEndpoints.Map(app);
ServerAccessEndpoints.Map(app);

app.Run();

public partial class Program;
