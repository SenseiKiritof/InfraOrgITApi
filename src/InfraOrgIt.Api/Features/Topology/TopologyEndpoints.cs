using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;

namespace InfraOrgIt.Api.Features.Topology;

public static class TopologyEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/topology", async (
            int? providerId,
            int? environmentId,
            int? projectId,
            TopologyService topology,
            CancellationToken ct) =>
        {
            var model = await topology.BuildAsync(
                new TopologyFilter(providerId, environmentId, projectId), ct);
            return Results.Ok(model);
        }).RequireAuthorization();

        app.MapGet("/api/topology/nodes/{nodeType}/{id:int}/delete-preview", async (
            DiagramNodeType nodeType,
            int id,
            TopologyService topology,
            CancellationToken ct) =>
            Results.Ok(await topology.GetDeletePreviewAsync(nodeType, id, ct)))
            .RequireAuthorization();

        app.MapDelete("/api/topology/nodes/{nodeType}/{id:int}", async (
            DiagramNodeType nodeType,
            int id,
            ClaimsPrincipal user,
            TopologyService topology,
            EnvironmentAccessService access,
            CancellationToken ct) =>
        {
            await topology.DeleteNodeAsync(nodeType, id, access, user, ct);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
