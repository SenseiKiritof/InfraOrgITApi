using System.Security.Claims;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;

namespace InfraOrgIt.Api.Features.Remote;

public static class ServerCredentialsEndpoints
{
    public static void Map(WebApplication app)
    {
        var credentials = app.MapGroup("/api/server-credentials").RequireAuthorization();
        credentials.MapGet("", ListAsync);
        credentials.MapGet("/{id:int}", GetAsync);
        credentials.MapPost("", CreateAsync);
        credentials.MapPut("/{id:int}", UpdateAsync);
        credentials.MapDelete("/{id:int}", DeleteAsync);

        var servers = app.MapGroup("/api/servers").RequireAuthorization();
        servers.MapPut("/{id:int}/ssh", AssignSshAsync);
        servers.MapDelete("/{id:int}/host-key", ResetHostKeyAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var page = PageRequest.From(http);
        var query = db.ServerCredentials.AsNoTracking();
        if (page.Q is not null)
            query = query.Where(x => x.Name.Contains(page.Q) ||
                                     x.Username.Contains(page.Q) ||
                                     x.AuthType.Contains(page.Q));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(x => x.Name)
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .Select(x => new ServerCredentialDto(
                x.Id, x.Name, x.Username, x.AuthType,
                x.EncryptedSecret != "", x.Servers.Count, x.NeedsReauth))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<ServerCredentialDto>(
            items, total, page.Page, page.PageSize));
    }

    private static async Task<IResult> GetAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var dto = await db.ServerCredentials.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ServerCredentialDto(
                x.Id, x.Name, x.Username, x.AuthType,
                x.EncryptedSecret != "", x.Servers.Count, x.NeedsReauth))
            .SingleOrDefaultAsync(ct);
        return dto is null ? throw Guard.NotFound("Credenziale", id) : Results.Ok(dto);
    }

    private static async Task<IResult> CreateAsync(
        ServerCredentialInput input,
        ClaimsPrincipal user,
        CredentialCipher cipher,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var name = Guard.Text(input.Name, "Name", 200);
        var username = Guard.Text(input.Username, "Username", 200);
        var authType = Guard.Enum<CredentialAuthType>(input.AuthType, "AuthType");
        var secret = Guard.Text(input.Secret, "Secret", 100_000);

        await EnsureUniqueAsync(db, name, null, ct);
        ValidatePrivateKey(authType, secret, input.Passphrase);

        var (ciphertext, keyVersion) = cipher.Encrypt(secret);
        var entity = new ServerCredential
        {
            Name = name,
            Username = username,
            AuthType = authType,
            EncryptedSecret = ciphertext,
            EncryptedPassphrase = string.IsNullOrEmpty(input.Passphrase)
                ? null
                : cipher.Encrypt(input.Passphrase).Ciphertext,
            KeyVersion = keyVersion
        };
        db.ServerCredentials.Add(entity);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new ServerCredentialDto(
            entity.Id, entity.Name, entity.Username, entity.AuthType,
            true, 0, entity.NeedsReauth));
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ServerCredentialInput input,
        ClaimsPrincipal user,
        CredentialCipher cipher,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ServerCredentials.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Credenziale", id);

        var name = Guard.Text(input.Name, "Name", 200);
        var username = Guard.Text(input.Username, "Username", 200);
        var authType = Guard.Enum<CredentialAuthType>(input.AuthType, "AuthType");

        await EnsureUniqueAsync(db, name, id, ct);
        if (authType != entity.AuthType && string.IsNullOrWhiteSpace(input.Secret))
            throw Guard.Unprocessable("Cambiando AuthType va reinserito il segreto.");

        if (!string.IsNullOrEmpty(input.Secret))
        {
            ValidatePrivateKey(authType, input.Secret, input.Passphrase);
            var (ciphertext, keyVersion) = cipher.Encrypt(input.Secret);
            entity.EncryptedSecret = ciphertext;
            entity.EncryptedPassphrase = string.IsNullOrEmpty(input.Passphrase)
                ? null
                : cipher.Encrypt(input.Passphrase).Ciphertext;
            entity.KeyVersion = keyVersion;
        }

        entity.Name = name;
        entity.Username = username;
        entity.AuthType = authType;
        entity.NeedsReauth = false;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var entity = await db.ServerCredentials.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Credenziale", id);

        var servers = await db.Servers.CountAsync(x => x.CredentialId == id, ct);
        if (servers > 0)
            throw Guard.Conflict($"Credenziale usata da {servers} server: rimuovere l'assegnazione prima.");

        db.ServerCredentials.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AssignSshAsync(
        int id,
        ServerSshInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        if (input.CredentialId is not null &&
            !await db.ServerCredentials.AnyAsync(x => x.Id == input.CredentialId, ct))
            throw Guard.NotFound("Credenziale", input.CredentialId.Value);

        server.CredentialId = input.CredentialId;
        server.SshPort = Guard.Port(input.SshPort, "SshPort");
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetHostKeyAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        server.HostKeyFingerprint = null;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static void ValidatePrivateKey(string authType, string secret, string? passphrase)
    {
        if (authType != nameof(CredentialAuthType.PrivateKey))
            return;

        try
        {
            using var key = new PrivateKeyFile(
                new MemoryStream(Encoding.UTF8.GetBytes(secret)),
                string.IsNullOrEmpty(passphrase) ? null : passphrase);
        }
        catch (Exception exception)
        {
            throw Guard.Unprocessable($"Chiave privata non valida: {exception.Message}");
        }
    }

    private static async Task EnsureUniqueAsync(
        InfraOrgItDbContext db, string name, int? id, CancellationToken ct)
    {
        var exists = await db.ServerCredentials.AnyAsync(
            x => x.Name == name && (id == null || x.Id != id), ct);
        if (exists)
            throw Guard.Conflict($"Esiste già una credenziale chiamata {name}.");
    }
}

public enum CredentialAuthType
{
    Password,
    PrivateKey
}

public sealed record ServerCredentialInput(
    string? Name,
    string? Username,
    string? AuthType,
    string? Secret,
    string? Passphrase);

public sealed record ServerCredentialDto(
    int Id,
    string Name,
    string Username,
    string AuthType,
    bool HasSecret,
    int ServersCount,
    bool NeedsReauth);

public sealed record ServerSshInput(int? CredentialId, int SshPort);
