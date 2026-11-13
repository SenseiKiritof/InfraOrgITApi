using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Settings;

/// <summary>
/// Installation-wide connection policy. Reads are available to every signed-in
/// client because the desktop needs the mode before opening a tunnel; writes
/// are reserved to SystemAdmin. The row lives in the shared database, so all
/// API nodes in a cluster return the same value.
/// </summary>
public static class ConnectionPolicyEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/connection-policy");
        // The desktop may have only a device-flow token, so reads accept the
        // same schemes as the tunnel API. Writes remain BFF sessions.
        group.MapGet("", GetAsync)
            .RequireAuthorization("Tunnel")
            .Produces<ConnectionPolicyDto>();
        group.MapPut("", UpdateAsync)
            .RequireAuthorization()
            .Produces<ConnectionPolicyDto>();
    }

    private static async Task<IResult> GetAsync(
        InfraOrgItDbContext db,
        CancellationToken ct)
    {
        var policy = await db.ConnectionPolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        return Results.Ok(new ConnectionPolicyDto(
            policy?.Mode ?? nameof(ConnectionPolicyMode.Auto),
            policy?.UpdatedAt,
            policy?.UpdatedBy ?? ""));
    }

    private static async Task<IResult> UpdateAsync(
        ConnectionPolicyInput input,
        ClaimsPrincipal user,
        InfraOrgItDbContext db,
        EnvironmentAccessService access,
        IAuditService audit,
        CancellationToken ct)
    {
        await access.RequireSystemAdminAsync(user, ct);
        // Missing or empty input keeps the documented default. An explicit
        // unknown value is still rejected by Guard.
        var mode = string.IsNullOrWhiteSpace(input.Mode)
            ? nameof(ConnectionPolicyMode.Auto)
            : Guard.Enum<ConnectionPolicyMode>(input.Mode, "Mode");

        var policy = await db.ConnectionPolicies
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (policy is null)
        {
            policy = new ConnectionPolicy { Id = 1 };
            db.ConnectionPolicies.Add(policy);
        }

        policy.Mode = mode;
        policy.UpdatedBy = user.Identity?.Name ?? "";
        policy.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditEntries.Change(
            user, "connection-policy.update", "ConnectionPolicy", 1,
            detail: new { mode }), ct);

        return Results.Ok(new ConnectionPolicyDto(
            policy.Mode, policy.UpdatedAt, policy.UpdatedBy));
    }
}

public enum ConnectionPolicyMode
{
    Server,
    Client,
    Auto
}

public sealed record ConnectionPolicyDto(
    string Mode,
    DateTime? UpdatedAt,
    string UpdatedBy);

public sealed record ConnectionPolicyInput(string? Mode);
