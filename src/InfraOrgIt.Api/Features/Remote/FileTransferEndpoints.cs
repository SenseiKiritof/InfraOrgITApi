using System.Net.Mime;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Remote;

public static class FileTransferEndpoints
{
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/servers/{id:int}").RequireAuthorization();

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
        var port = input.Port is null or 0
            ? protocol == nameof(FileTransferProtocol.Ftp) ? 21 : 22
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
        CancellationToken ct)
    {
        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        await using var session = await ConnectAsync(connector, server, ct);
        var normalized = RemotePath.Normalize(path);
        try
        {
            return Results.Ok(new { path = normalized, items = await session.ListAsync(normalized, ct) });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw Guard.Unprocessable("Indicare il file da scaricare.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        await using var session = await ConnectAsync(connector, server, ct);
        var normalized = RemotePath.Normalize(path);
        var name = RemotePath.Name(normalized);
        http.Response.ContentType = MediaTypeNames.Application.Octet;
        http.Response.Headers.ContentDisposition =
            new ContentDispositionHeaderValue("attachment") { FileNameStar = name }.ToString();

        try
        {
            await session.DownloadAsync(normalized, http.Response.Body, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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

        await using var session = await ConnectAsync(connector, server, ct);
        var target = RemotePath.Combine(RemotePath.Normalize(path), name);
        try
        {
            await using var stream = file.OpenReadStream();
            await session.UploadAsync(target, stream, ct);
            return Results.Ok(new { path = target });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
        CancellationToken ct)
    {
        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        await using var session = await ConnectAsync(connector, server, ct);
        var target = RemotePath.Normalize(input.Path);
        try
        {
            await session.CreateDirectoryAsync(target, ct);
            return Results.Ok(new { path = target });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
        CancellationToken ct)
    {
        var name = Guard.Text(input.NewName, "NewName", 255);
        if (name.Contains('/') || name.Contains('\\') || name is "." or "..")
            throw Guard.Unprocessable("Il nuovo nome non può contenere separatori di percorso.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        await using var session = await ConnectAsync(connector, server, ct);
        var source = RemotePath.Normalize(input.Path);
        var separator = source.LastIndexOf('/');
        var parent = separator <= 0 ? "/" : source[..separator];
        var target = RemotePath.Combine(parent, name);
        try
        {
            await session.RenameAsync(source, target, input.IsDirectory, ct);
            return Results.Ok(new { path = target });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
        CancellationToken ct)
    {
        var normalized = RemotePath.Normalize(path);
        if (normalized == "/")
            throw Guard.Unprocessable("Non è possibile eliminare la radice.");

        var server = await LoadAuthorizedServerAsync(id, user, db, access, ct);
        await using var session = await ConnectAsync(connector, server, ct);
        try
        {
            await session.DeleteAsync(normalized, isDirectory ?? false, ct);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw Failure(exception);
        }
    }

    private static async Task<Server> LoadAuthorizedServerAsync(
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

    private static ApiException Failure(Exception exception) =>
        exception as ApiException
        ?? new ApiException(
            StatusCodes.Status502BadGateway,
            "file_transfer_failed",
            $"Operazione sul file non riuscita: {exception.Message}");

    public enum FileTransferProtocol
    {
        Sftp,
        Ftp
    }

    public sealed record FileTransferInput(
        string? Protocol,
        int? Port = null,
        int? CredentialId = null);

    public sealed record FilePathInput(string? Path);
    public sealed record FileRenameInput(string? Path, string? NewName, bool IsDirectory = false);
}
