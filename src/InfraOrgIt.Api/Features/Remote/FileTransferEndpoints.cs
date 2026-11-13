using System.Net.Mime;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Features.Settings;
using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InfraOrgIt.Api.Features.Remote;

public static class FileTransferEndpoints
{
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/servers/{id:int}").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var policy = context.HttpContext.RequestServices
                .GetRequiredService<ConnectionPolicyService>();
            await policy.RequireServerTransportAsync(
                context.HttpContext.RequestAborted);
            return await next(context);
        });

        group.MapPut("/file-transfer", ConfigureAsync);
        group.MapGet("/files", ListAsync).RequireRateLimiting("files");
        group.MapGet("/files/download", DownloadAsync).RequireRateLimiting("files");
        group.MapPost("/files/upload", UploadAsync).RequireRateLimiting("files");
        group.MapPost("/files/directory", CreateDirectoryAsync).RequireRateLimiting("files");
        group.MapPost("/files/rename", RenameAsync).RequireRateLimiting("files");
        group.MapDelete("/files", DeleteAsync).RequireRateLimiting("files");
    }

    private static async Task<IResult> ConfigureAsync(
        int id,
        FileTransferInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        IOptions<FileTransferOptions> fileTransfer,
        CancellationToken ct)
    {
        EnvironmentAccessService.RequireSystemAdmin(user);
        var server = await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);

        if (string.IsNullOrWhiteSpace(input.Protocol))
        {
            server.FileTransferProtocol = null;
            server.FileTransferPort = null;
            server.FileTransferCredentialId = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        var protocol = Guard.Enum<FileTransferProtocol>(input.Protocol, "Protocol");
        FileTransferPolicy.EnsureProtocolAllowed(
            protocol.ToString(), fileTransfer.Value.AllowPlainFtp);
        var port = input.Port is null or 0
            ? DefaultPort(protocol)
            : Guard.Port(input.Port.Value, "Port");

        if (input.CredentialId is not null &&
            !await db.ServerCredentials.AnyAsync(x => x.Id == input.CredentialId, ct))
            throw Guard.NotFound("Credenziale", input.CredentialId.Value);

        server.FileTransferProtocol = protocol.ToString();
        server.FileTransferPort = port;
        server.FileTransferCredentialId = input.CredentialId;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(
        int id,
        string? path,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        CancellationToken ct)
    {
        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        var started = Stopwatch.GetTimestamp();
        var normalized = RemotePath.Normalize(path);
        try
        {
            await using var session = await ConnectAsync(connector, server, ct);
            // Senza path si parte dalla home della sessione (per SFTP quella
            // dell'utente collegato); i path espliciti restano invariati.
            normalized = string.IsNullOrWhiteSpace(path)
                ? RemotePath.Normalize(session.HomePath)
                : RemotePath.Normalize(path);
            var items = await session.ListAsync(normalized, ct);
            await RecordAsync(audit, user, "file.list", server, started,
                new { path = normalized }, ct);
            return Results.Ok(new { path = normalized, items });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, user, "file.list", server, started,
                new { path = normalized }, exception);
            throw Failure(exception);
        }
    }

    private static async Task DownloadAsync(
        HttpContext http,
        int id,
        string? path,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Guard.Unprocessable("Indicare il file da scaricare.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        var normalized = RemotePath.Normalize(path);
        var started = Stopwatch.GetTimestamp();
        await using var session = await ConnectAsync(connector, server, ct);
        var name = RemotePath.Name(normalized);
        http.Response.ContentType = MediaTypeNames.Application.Octet;
        http.Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileNameStar = name }.ToString();

        try
        {
            await session.DownloadAsync(normalized, http.Response.Body, ct);
            await RecordAsync(audit, user, "file.download", server, started,
                new { path = normalized }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, user, "file.download", server, started,
                new { path = normalized }, exception);
            if (!http.Response.HasStarted)
                throw Failure(exception);
            throw;
        }
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http,
        int id,
        string? path,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Guard.Unprocessable("Indicare la cartella di destinazione.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        var size = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false })
            size.MaxRequestBodySize = MaxUploadBytes;
        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.SingleOrDefault()
                   ?? throw Guard.Unprocessable("Nessun file ricevuto.");
        if (file.Length > MaxUploadBytes)
            throw Guard.Unprocessable(
                $"File troppo grande: il limite è {MaxUploadBytes / (1024 * 1024)} MB.");

        var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            throw Guard.Unprocessable("Nome del file non valido.");

        var target = RemotePath.Combine(RemotePath.Normalize(path), name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var session = await ConnectAsync(connector, server, ct);
            await using var stream = file.OpenReadStream();
            await session.UploadAsync(target, stream, ct);
            await RecordAsync(audit, user, "file.upload", server, started,
                new { path = target, size = file.Length }, ct);
            return Results.Ok(new { path = target });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, user, "file.upload", server, started,
                new { path = target, size = file.Length }, exception);
            throw Failure(exception);
        }
    }

    private static async Task<IResult> CreateDirectoryAsync(
        int id,
        FilePathInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        CancellationToken ct)
    {
        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        var target = RemotePath.Normalize(input.Path);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var session = await ConnectAsync(connector, server, ct);
            await session.CreateDirectoryAsync(target, ct);
            await RecordAsync(audit, user, "file.mkdir", server, started,
                new { path = target }, ct);
            return Results.Ok(new { path = target });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, user, "file.mkdir", server, started,
                new { path = target }, exception);
            throw Failure(exception);
        }
    }

    private static async Task<IResult> RenameAsync(
        int id,
        FileRenameInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        CancellationToken ct)
    {
        var name = Guard.Text(input.NewName, "NewName", 255);
        if (name.Contains('/') || name.Contains('\\') || name is "." or "..")
            throw Guard.Unprocessable("Il nuovo nome non può contenere separatori di percorso.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        var source = RemotePath.Normalize(input.Path);
        var separator = source.LastIndexOf('/');
        var parent = separator <= 0 ? "/" : source[..separator];
        var target = RemotePath.Combine(parent, name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var session = await ConnectAsync(connector, server, ct);
            await session.RenameAsync(source, target, input.IsDirectory, ct);
            await RecordAsync(audit, user, "file.rename", server, started,
                new { from = source, to = target, isDirectory = input.IsDirectory }, ct);
            return Results.Ok(new { path = target });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, user, "file.rename", server, started,
                new { from = source, to = target, isDirectory = input.IsDirectory }, exception);
            throw Failure(exception);
        }
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        string? path,
        bool? isDirectory,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        CancellationToken ct)
    {
        var normalized = RemotePath.Normalize(path);
        if (normalized == "/")
            throw Guard.Unprocessable("Non è possibile eliminare la radice.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var session = await ConnectAsync(connector, server, ct);
            await session.DeleteAsync(normalized, isDirectory ?? false, ct);
            await RecordAsync(audit, user, "file.delete", server, started,
                new { path = normalized, isDirectory = isDirectory ?? false }, ct);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, user, "file.delete", server, started,
                new { path = normalized, isDirectory = isDirectory ?? false }, exception);
            throw Failure(exception);
        }
    }

    private static Task RecordAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        string action,
        Server server,
        long started,
        object detail,
        CancellationToken ct) =>
        audit.RecordAsync(TransferEntry(user, action, server, "Ok", started, detail), ct);

    private static Task RecordFailureAsync(
        IAuditService audit,
        ClaimsPrincipal user,
        string action,
        Server server,
        long started,
        object detail,
        Exception exception) =>
        audit.RecordAsync(
            TransferEntry(
                user, action, server, "Error", started, detail,
                exception is ApiException api ? api.Code : "file_transfer_failed"),
            CancellationToken.None);

    private static AuditEntry TransferEntry(
        ClaimsPrincipal user,
        string action,
        Server server,
        string outcome,
        long started,
        object detail,
        string? errorCode = null)
    {
        var (userId, username, clientId) = AuditEntries.Actor(user);
        return new AuditEntry(
            "FileTransfer",
            action,
            outcome,
            ActorUserId: userId,
            ActorUsername: username,
            ClientId: clientId,
            TargetType: "Server",
            TargetId: server.Id,
            ServerId: server.Id,
            ErrorCode: errorCode,
            DurationMs: (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Detail: detail);
    }

    internal static async Task<Server> LoadAuthorizedServerAsync(
        int id,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking()
                         .Include(x => x.Credential)
                         .Include(x => x.FileTransferCredential)
                         .SingleOrDefaultAsync(x => x.Id == id, ct)
                     ?? throw Guard.NotFound("Server", id);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);
        return server;
    }

    private static async Task<IFileTransferSession> ConnectAsync(
        IFileTransferConnector connector,
        Server server,
        CancellationToken ct)
    {
        try
        {
            return await connector.ConnectAsync(server, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(exception);
        }
    }

    internal static ApiException Failure(Exception exception) =>
        exception as ApiException
        ?? new ApiException(
            StatusCodes.Status502BadGateway,
            "file_transfer_failed",
            $"Operazione sul file non riuscita: {exception.Message}");

    internal static int DefaultPort(string protocol) =>
        protocol == nameof(FileTransferProtocol.Sftp) ? 22 : 21;

    public enum FileTransferProtocol
    {
        Sftp,
        Ftp,
        Ftps
    }

    public sealed record FileTransferInput(
        string? Protocol,
        int? Port = null,
        int? CredentialId = null);

    public sealed record FilePathInput(string? Path);
    public sealed record FileRenameInput(string? Path, string? NewName, bool IsDirectory = false);
}
