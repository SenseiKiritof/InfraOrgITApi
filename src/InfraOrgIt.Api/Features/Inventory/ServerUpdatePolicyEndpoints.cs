using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Audit;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Inventory;

public static class ServerUpdatePolicyEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/server-update-policy").RequireAuthorization();
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
    }

    private static async Task<IResult> GetAsync(
        InfraOrgItDbContext db, CancellationToken ct)
    {
        var policy = await db.ServerUpdatePolicies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        return Results.Ok(new ServerUpdatePolicyDto(
            policy?.Enabled ?? true,
            policy?.LtsOnly ?? true,
            policy?.UpdatedAt,
            policy?.UpdatedBy ?? ""));
    }

    private static async Task<IResult> UpdateAsync(
        ServerUpdatePolicyInput input,
        ClaimsPrincipal user,
        EnvironmentAccessService access,
        InfraOrgItDbContext db,
        IAuditService audit,
        CancellationToken ct)
    {
        await access.RequireSystemAdminAsync(user, ct);
        var policy = await db.ServerUpdatePolicies
            .SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (policy is null)
        {
            policy = new ServerUpdatePolicy { Id = 1 };
            db.ServerUpdatePolicies.Add(policy);
        }
        if (input.Enabled is bool enabled) policy.Enabled = enabled;
        if (input.LtsOnly is bool ltsOnly) policy.LtsOnly = ltsOnly;
        policy.UpdatedBy = user.Identity?.Name ?? "";
        policy.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditEntries.Change(
            user, "server-update-policy.update", "ServerUpdatePolicy", 1,
            detail: new { policy.Enabled, policy.LtsOnly }), ct);
        return Results.Ok(new ServerUpdatePolicyDto(
            policy.Enabled, policy.LtsOnly, policy.UpdatedAt, policy.UpdatedBy));
    }
}

public sealed record ServerUpdatePolicyInput(bool? Enabled, bool? LtsOnly);

public sealed record ServerUpdatePolicyDto(
    bool Enabled,
    bool LtsOnly,
    DateTime? UpdatedAt,
    string UpdatedBy);
