using System.Security.Claims;
using System.Text;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Auth;
using InfraOrgIt.Api.BuildingBlocks.Security;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Features.Devices;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using System.Text.Json;

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
        credentials.MapGet("/downloadable", ListDownloadableAsync);
        credentials.MapPost("/{id:int}/download", DownloadAsync)
            .RequireRateLimiting("tunnels");

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
                x.EncryptedSecret != "", x.EncryptedSudoSecret != null,
                x.Servers.Count, x.NeedsReauth,
                x.OwnerUserId,
                x.Owner != null ? x.Owner.Username : null,
                x.AllowOffline,
                x.Version))
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
                x.EncryptedSecret != "", x.EncryptedSudoSecret != null,
                x.Servers.Count, x.NeedsReauth,
                x.OwnerUserId,
                x.Owner != null ? x.Owner.Username : null,
                x.AllowOffline,
                x.Version))
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
        await ValidateOwnerAsync(db, input.OwnerUserId, ct);

        var (ciphertext, keyVersion) = cipher.Encrypt(secret);
        string? sudoCiphertext = null;
        int? sudoKeyVersion = null;
        if (!string.IsNullOrEmpty(input.SudoPassword))
        {
            var (value, version) = cipher.Encrypt(input.SudoPassword);
            sudoCiphertext = value;
            sudoKeyVersion = version;
        }
        var entity = new ServerCredential
        {
            Name = name,
            Username = username,
            AuthType = authType,
            EncryptedSecret = ciphertext,
            EncryptedPassphrase = string.IsNullOrEmpty(input.Passphrase)
                ? null
                : cipher.Encrypt(input.Passphrase).Ciphertext,
            EncryptedSudoSecret = sudoCiphertext,
            SudoKeyVersion = sudoKeyVersion,
            KeyVersion = keyVersion,
            OwnerUserId = input.OwnerUserId,
            AllowOffline = input.AllowOffline
        };
        db.ServerCredentials.Add(entity);
        await db.SaveChangesAsync(ct);

        var owner = entity.OwnerUserId is null
            ? null
            : await db.Users.Where(x => x.Id == entity.OwnerUserId)
                .Select(x => x.Username).SingleOrDefaultAsync(ct);
        return Results.Ok(new ServerCredentialDto(
            entity.Id, entity.Name, entity.Username, entity.AuthType,
            true, entity.EncryptedSudoSecret != null, 0, entity.NeedsReauth,
            entity.OwnerUserId, owner, entity.AllowOffline, entity.Version));
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
        await ValidateOwnerAsync(db, input.OwnerUserId, ct);
        if (input.OwnerUserId is not null &&
            await db.Servers.AnyAsync(
                x => x.CredentialId == id || x.FileTransferCredentialId == id, ct))
            throw Guard.Unprocessable(
                "Rimuovi le assegnazioni ai server prima di rendere la credenziale personale.");
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

        if (input.SudoPassword is not null)
        {
            if (input.SudoPassword.Length == 0)
            {
                entity.EncryptedSudoSecret = null;
                entity.SudoKeyVersion = null;
            }
            else
            {
                var (value, version) = cipher.Encrypt(input.SudoPassword);
                entity.EncryptedSudoSecret = value;
                entity.SudoKeyVersion = version;
            }
        }

        entity.Name = name;
        entity.Username = username;
        entity.AuthType = authType;
        entity.NeedsReauth = false;
        entity.OwnerUserId = input.OwnerUserId;
        entity.AllowOffline = input.AllowOffline;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListDownloadableAsync(
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var actor = await ActorResolver.ResolveAsync(user, db, ct);
        var environmentAccess = await access.GetAsync(user, ct);
        var sharedIds = new List<int>();
        if (environmentAccess.OperatorEnvironmentIds.Count > 0)
        {
            var assignments = await db.Servers.AsNoTracking()
                .Where(x => environmentAccess.OperatorEnvironmentIds.Contains(
                    x.ProviderEnvironmentId))
                .Select(x => new
                {
                    x.CredentialId,
                    x.FileTransferCredentialId
                })
                .ToListAsync(ct);
            sharedIds = assignments
                .SelectMany(x => new int?[]
                {
                    x.CredentialId,
                    x.FileTransferCredentialId
                })
                .Where(x => x is not null)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();
        }

        var items = await db.ServerCredentials.AsNoTracking()
            .Where(x => x.OwnerUserId == actor.Id ||
                        (x.OwnerUserId == null && sharedIds.Contains(x.Id)))
            .OrderBy(x => x.Name)
            .Select(x => new DownloadableCredentialDto(
                x.Id, x.Name, x.Username, x.AuthType,
                x.OwnerUserId, x.Owner != null ? x.Owner.Username : null,
                x.AllowOffline, x.Servers.Count, x.Version))
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> DownloadAsync(
        int id,
        CredentialDownloadInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        CredentialCipher cipher,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        var actor = await ActorResolver.ResolveAsync(user, db, ct);
        var credential = await db.ServerCredentials.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Credenziale", id);
        var device = await db.DesktopDevices
            .SingleOrDefaultAsync(
                x => x.Id == input.DeviceId && x.UserId == actor.Id, ct)
            ?? throw Guard.NotFound("Dispositivo", input.DeviceId);
        if (device.RevokedAt is not null)
            throw Guard.Forbidden("Dispositivo revocato: registra una nuova chiave.");

        if (!await CanDownloadAsync(credential, actor, db, access, ct))
            throw Guard.Forbidden("Credenziale non accessibile a questo utente.");

        var secret = cipher.Decrypt(credential.EncryptedSecret, credential.KeyVersion);
        var passphrase = string.IsNullOrWhiteSpace(credential.EncryptedPassphrase)
            ? null
            : cipher.Decrypt(credential.EncryptedPassphrase, credential.KeyVersion);
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            version = 1,
            credentialId = credential.Id,
            deviceId = device.Id,
            name = credential.Name,
            username = credential.Username,
            authType = credential.AuthType,
            secret,
            passphrase,
            owner = credential.OwnerUserId is null ? "shared" : "personal",
            allowOffline = credential.AllowOffline,
            credentialVersion = credential.Version,
            downloadedAt = now,
            expiresAt = now.AddMinutes(5)
        });
        var aad = $"infraorgit-credential:{credential.Id}:{device.Id}:{credential.KeyVersion}";
        var encrypted = DeviceCipher.Encrypt(device.PublicKeyPem, payload, aad);

        device.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry(
            "CredentialDownload",
            "credential.download",
            "Ok",
            ActorUserId: actor.Id,
            ActorUsername: actor.Username,
            ClientId: $"device:{device.Id}",
            TargetType: "ServerCredential",
            TargetId: credential.Id,
            Detail: new
            {
                deviceId = device.Id,
                deviceName = device.Name,
                owner = credential.OwnerUserId is null ? "shared" : "personal"
            }), ct);

        return Results.Ok(new DeviceSecretDto(
            1, credential.Id, device.Id, credential.KeyVersion,
            now.AddMinutes(5), encrypted));
    }

    private static async Task<bool> CanDownloadAsync(
        ServerCredential credential,
        InfraOrgIt.Api.Infrastructure.Persistence.Entities.User actor,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        if (credential.OwnerUserId is int owner) return owner == actor.Id;
        var environmentAccess = await access.GetForUsernameAsync(
            actor.Username, ct);
        if (environmentAccess.OperatorEnvironmentIds.Count == 0) return false;
        return await db.Servers.AsNoTracking().AnyAsync(
            x => environmentAccess.OperatorEnvironmentIds.Contains(
                     x.ProviderEnvironmentId) &&
                 (x.CredentialId == credential.Id ||
                  x.FileTransferCredentialId == credential.Id), ct);
    }

    private static async Task ValidateOwnerAsync(
        InfraOrgItDbContext db, int? ownerUserId, CancellationToken ct)
    {
        if (ownerUserId is null) return;
        if (!await db.Users.AnyAsync(x => x.Id == ownerUserId, ct))
            throw Guard.Unprocessable("Utente proprietario non valido.");
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

        var servers = await db.Servers.CountAsync(
            x => x.CredentialId == id || x.FileTransferCredentialId == id, ct);
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

        if (input.CredentialId is not null)
        {
            var credential = await db.ServerCredentials.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == input.CredentialId, ct)
                ?? throw Guard.NotFound("Credenziale", input.CredentialId.Value);
            if (credential.OwnerUserId is not null)
                throw Guard.Unprocessable(
                    "Una credenziale personale non può essere assegnata a un server: usa una condivisa.");
        }

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
    string? Passphrase,
    string? SudoPassword,
    int? OwnerUserId = null,
    bool AllowOffline = false);

public sealed record ServerCredentialDto(
    int Id,
    string Name,
    string Username,
    string AuthType,
    bool HasSecret,
    bool HasSudoPassword,
    int ServersCount,
    bool NeedsReauth,
    int? OwnerUserId,
    string? OwnerUsername,
    bool AllowOffline,
    int Version);

public sealed record DownloadableCredentialDto(
    int Id,
    string Name,
    string Username,
    string AuthType,
    int? OwnerUserId,
    string? OwnerUsername,
    bool AllowOffline,
    int ServersCount,
    int Version);

public sealed record CredentialDownloadInput(int DeviceId);

public sealed record DeviceSecretDto(
    int Version,
    int CredentialId,
    int DeviceId,
    int KeyVersion,
    DateTimeOffset ExpiresAt,
    DeviceCipherResult Payload);

public sealed record ServerSshInput(int? CredentialId, int SshPort);
