using System.Diagnostics;
using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Files;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace InfraOrgIt.Api.Features.Remote;

/// <summary>
/// Signed, one-time download URLs for the desktop bridge. Ticket creation uses
/// the browser session and CSRF; the download itself is anonymous because the
/// ticket is the bearer credential.
/// </summary>
public static class DesktopDownloadEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/desktop/downloads");
        group.MapGet("/capabilities", Capabilities)
            .RequireAuthorization("Desktop");
        group.MapPost("/tickets", CreateTicketAsync)
            .RequireAuthorization("Session")
            .RequireRateLimiting("files");
        group.MapGet("/{token}", DownloadAsync)
            .AllowAnonymous()
            .RequireRateLimiting("desktop-download");
    }

    private static IResult Capabilities(IOptions<DesktopDownloadsOptions> options) =>
        Results.Ok(new { signedUrl = options.Value.Enabled });

    private static async Task<IResult> CreateTicketAsync(
        DownloadTicketInput input,
        HttpContext http,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IAuditService audit,
        IOptions<DesktopDownloadsOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.Enabled)
            throw DesktopDownloadsDisabled();

        if (input.ServerId <= 0)
            throw Guard.Unprocessable("Indicare il server.");
        if (string.IsNullOrWhiteSpace(input.Path))
            throw Guard.Unprocessable("Indicare il file da scaricare.");

        var normalized = RemotePath.Normalize(input.Path);
        if (normalized == "/")
            throw Guard.Unprocessable("Indicare il file da scaricare.");
        if (normalized.Length > 1024)
            throw Guard.Unprocessable("Il percorso supera 1024 caratteri.");

        var started = Stopwatch.GetTimestamp();
        Server server;
        try
        {
            server = await FileTransferEndpoints.LoadAuthorizedServerAsync(
                input.ServerId, user, db, access, ct);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status403Forbidden)
        {
            var (deniedUserId, deniedUsername, deniedClientId) = AuditEntries.Actor(user);
            await audit.RecordAsync(
                TransferEntry(
                    deniedUserId, deniedUsername, deniedClientId,
                    "file.download.ticket", "Denied", input.ServerId, started,
                    new { path = normalized }, "forbidden"),
                ct);
            throw;
        }

        var username = user.Identity?.Name ?? "";
        var userId = await db.Users.AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw new ApiException(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "Utente della sessione non trovato.");

        var now = DateTime.UtcNow;
        var rawToken = DownloadTicketTokens.Create();
        var expiresAt = now.AddSeconds(options.Value.TicketLifetimeSeconds);
        db.DownloadTickets.Add(new DownloadTicket
        {
            TokenHash = DownloadTicketTokens.Hash(rawToken),
            UserId = userId,
            ServerId = server.Id,
            Path = normalized,
            ExpiresAt = expiresAt
        });
        await db.SaveChangesAsync(ct);

        var (_, actorUsername, clientId) = AuditEntries.Actor(user);
        await audit.RecordAsync(
            TransferEntry(
                userId, actorUsername, clientId,
                "file.download.ticket", "Ok", server.Id, started,
                new { path = normalized, expiresAt }),
            ct);

        return Results.Ok(new
        {
            url = BuildUrl(http.Request, options.Value, rawToken),
            expiresAt
        });
    }

    private static async Task DownloadAsync(
        HttpContext http,
        string token,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IFileTransferConnector connector,
        IAuditService audit,
        IOptions<DesktopDownloadsOptions> options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            throw TicketInvalid();

        var started = Stopwatch.GetTimestamp();
        var hash = DownloadTicketTokens.Hash(token);
        var now = DateTime.UtcNow;
        var ticket = await db.DownloadTickets.AsNoTracking()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (ticket is null)
            throw TicketInvalid();

        // The kill switch also invalidates tickets issued before it was turned
        // off: no content is sent and the ticket stays unconsumed.
        if (!options.Value.Enabled)
            throw await DeniedTicketAsync(
                audit, ticket, started, "desktop_downloads_disabled", ct,
                DesktopDownloadsDisabled());

        if (ticket.ConsumedAt is not null)
            throw await DeniedTicketAsync(
                audit, ticket, started, "ticket_consumed", ct);
        if (ticket.ExpiresAt <= now)
            throw await DeniedTicketAsync(
                audit, ticket, started, "ticket_expired", ct);

        var server = await db.Servers.AsNoTracking()
            .Include(x => x.Credential)
            .Include(x => x.FileTransferCredential)
            .SingleOrDefaultAsync(x => x.Id == ticket.ServerId, ct);
        if (ticket.User is null || server is null)
            throw await DeniedTicketAsync(
                audit, ticket, started, "ticket_target_missing", ct);

        // The grant can change between issuance and download (both are short
        // lived, but revocation must win). Anonymous requests carry no role
        // claims, so resolve the stored user from the database.
        var ticketUser = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, ticket.User.Username)], "DownloadTicket"));
        var effectiveAccess = await access.GetAsync(ticketUser, ct);
        if (!effectiveAccess.AdminEnvironmentIds.Contains(server.ProviderEnvironmentId))
            throw await DeniedTicketAsync(
                audit, ticket, started, "forbidden", ct);

        var consumed = await db.DownloadTickets
            .Where(x => x.Id == ticket.Id &&
                        x.ConsumedAt == null &&
                        x.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ConsumedAt, now)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.Version, x => x.Version + 1), ct);
        if (consumed == 0)
            throw await DeniedTicketAsync(
                audit, ticket, started, "ticket_consumed", ct);

        var name = RemotePath.Name(ticket.Path);
        IFileTransferSession session;
        try
        {
            session = await connector.ConnectAsync(server, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordFailureAsync(audit, ticket, "file.download", started,
                new { path = ticket.Path }, exception);
            throw FileTransferEndpoints.Failure(exception);
        }

        await using (session)
        {
            http.Response.ContentType = ContentTypes.TryGetContentType(name, out var contentType)
                ? contentType
                : "application/octet-stream";
            http.Response.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileNameStar = name }.ToString();

            try
            {
                await session.DownloadAsync(ticket.Path, http.Response.Body, ct);
                await audit.RecordAsync(
                    DownloadEntry(ticket, "file.download", "Ok", started,
                        new { path = ticket.Path }), ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await RecordFailureAsync(audit, ticket, "file.download", started,
                    new { path = ticket.Path }, exception);
                if (!http.Response.HasStarted)
                    throw FileTransferEndpoints.Failure(exception);
                throw;
            }
        }
    }

    private static Task RecordFailureAsync(
        IAuditService audit,
        DownloadTicket ticket,
        string action,
        long started,
        object detail,
        Exception exception) =>
        audit.RecordAsync(
            DownloadEntry(ticket, action, "Error", started, detail,
                exception is ApiException api ? api.Code : "file_transfer_failed"),
            CancellationToken.None);

    private static async Task<ApiException> DeniedTicketAsync(
        IAuditService audit,
        DownloadTicket ticket,
        long started,
        string errorCode,
        CancellationToken ct,
        ApiException? response = null)
    {
        await audit.RecordAsync(
            DownloadEntry(ticket, "file.download", "Denied", started,
                new { path = ticket.Path }, errorCode),
            ct);
        return response ?? TicketInvalid();
    }

    private static ApiException DesktopDownloadsDisabled() =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            "desktop_downloads_disabled",
            "I download firmati per il desktop sono disattivati.");

    private static ApiException TicketInvalid() =>
        new(
            StatusCodes.Status404NotFound,
            "download_ticket_invalid",
            "Ticket di download mancante, scaduto o già usato.");

    private static AuditEntry TransferEntry(
        int? actorUserId,
        string actorUsername,
        string? clientId,
        string action,
        string outcome,
        int serverId,
        long started,
        object detail,
        string? errorCode = null) =>
        new(
            "FileTransfer",
            action,
            outcome,
            ActorUserId: actorUserId,
            ActorUsername: actorUsername,
            ClientId: clientId,
            TargetType: "Server",
            TargetId: serverId,
            ServerId: serverId,
            ErrorCode: errorCode,
            DurationMs: (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Detail: detail);

    private static AuditEntry DownloadEntry(
        DownloadTicket ticket,
        string action,
        string outcome,
        long started,
        object detail,
        string? errorCode = null) =>
        TransferEntry(
            ticket.UserId,
            ticket.User?.Username ?? "",
            null,
            action,
            outcome,
            ticket.ServerId,
            started,
            detail,
            errorCode);

    internal static string BuildUrl(
        HttpRequest request,
        DesktopDownloadsOptions options,
        string token)
    {
        var path = $"/api/desktop/downloads/{token}";
        return string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? $"{request.Scheme}://{request.Host}{request.PathBase}{path}"
            : options.PublicBaseUrl.Trim().TrimEnd('/') + path;
    }

    public sealed record DownloadTicketInput(int ServerId, string? Path);
}
