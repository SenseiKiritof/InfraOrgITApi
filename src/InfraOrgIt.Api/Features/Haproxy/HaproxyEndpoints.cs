using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Pagination;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Haproxy;

public static class HaproxyEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/haproxy").RequireAuthorization();

        group.MapGet("/instances", ListAsync);
        group.MapPut("/instances/{id:int}/monitoring", SetMonitoringAsync)
            .RequireRateLimiting("remote-run");
        group.MapPost("/discovery/run", RunDiscoveryAsync)
            .RequireRateLimiting("remote-run");
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var serverId = PageRequest.IntQuery(http, "serverId");
        var accessInfo = await access.GetAsync(user, ct);
        if (accessInfo.AdminEnvironmentIds.Count == 0) return Results.Ok(Array.Empty<HaproxyInstanceDto>());

        var adminIds = accessInfo.AdminEnvironmentIds.ToList();
        var query = db.HaproxyInstances.AsNoTracking()
            .Where(x => adminIds.Contains(x.Server!.ProviderEnvironmentId));
        if (serverId is not null)
            query = query.Where(x => x.ServerId == serverId.Value);

        var items = await query
            .OrderBy(x => x.Server!.Name)
            .ThenBy(x => x.Id)
            .Select(x => new HaproxyInstanceDto(
                x.Id,
                x.ServerId,
                x.Server!.Name,
                x.Server.ProviderEnvironmentId,
                x.Server.ProviderEnvironment!.Provider!.Name + " / " +
                x.Server.ProviderEnvironment.EnvironmentDefinition!.Name,
                x.ServerApplicationId,
                x.Name,
                x.BaseUrl,
                x.Transport,
                x.ApiHost,
                x.ApiPort,
                x.ApiVersion,
                x.MonitoringChoice,
                x.DiscoveryStatus,
                x.DetectedVersion,
                x.DetectedConfigPath,
                x.LastSeenAt,
                x.LastTestAt,
                x.LastTestStatus,
                x.LastTestDetail,
                x.Username != null && x.EncryptedPassword != null,
                x.AllowSelfSigned))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    private static async Task<IResult> SetMonitoringAsync(
        int id,
        MonitoringInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        var instance = await db.HaproxyInstances
            .Include(x => x.Server)
            .SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw Guard.NotFound("Istanza HAProxy", id);

        await access.RequireAdminAsync(
            user, [instance.Server!.ProviderEnvironmentId], ct);

        var choice = input.Choice?.Trim() ?? "";
        if (choice is not ("Undecided" or "Enabled" or "Disabled"))
            throw Guard.Unprocessable(
                "Choice ammessa: Undecided, Enabled, Disabled.");

        instance.MonitoringChoice = choice;
        instance.MonitoringChangedAt = DateTime.UtcNow;
        instance.MonitoringChangedBy = user.Identity?.Name ?? "";
        if (choice == "Enabled")
        {
            instance.DiscoveryStatus =
                instance.Username is not null && instance.EncryptedPassword is not null
                    ? instance.LastTestStatus == "Ok" ? "Ready" : "Found"
                    : "CredentialsRequired";
        }
        else if (choice == "Disabled")
        {
            instance.DiscoveryStatus = "Inactive";
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user,
            "haproxy.monitoring",
            "HaproxyInstance",
            instance.Id,
            detail: new { choice }), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> RunDiscoveryAsync(
        int? serverId,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        HaproxyDiscoveryRunner runner,
        CancellationToken ct)
    {
        if (serverId is null)
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            _ = Task.Run(() => runner.RunAllAsync(CancellationToken.None));
            return Results.Accepted(value: new { started = true });
        }

        var server = await db.Servers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == serverId.Value, ct)
            ?? throw Guard.NotFound("Server", serverId.Value);
        await access.RequireAdminAsync(user, [server.ProviderEnvironmentId], ct);
        if (!runner.Start(server.Id))
            throw Guard.Conflict("Scoperta HAProxy già in corso per questo server.");
        return Results.Accepted(value: new { started = true });
    }
}

public sealed record MonitoringInput(string? Choice);

public sealed record HaproxyInstanceDto(
    int Id,
    int ServerId,
    string ServerName,
    int ProviderEnvironmentId,
    string EnvironmentLabel,
    int? ServerApplicationId,
    string Name,
    string BaseUrl,
    string Transport,
    string ApiHost,
    int ApiPort,
    string ApiVersion,
    string MonitoringChoice,
    string DiscoveryStatus,
    string? DetectedVersion,
    string? DetectedConfigPath,
    DateTime? LastSeenAt,
    DateTime? LastTestAt,
    string? LastTestStatus,
    string? LastTestDetail,
    bool HasCredentials,
    bool AllowSelfSigned);
