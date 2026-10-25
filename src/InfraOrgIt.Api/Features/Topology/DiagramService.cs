using System.Security.Claims;
using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Topology;

public sealed class DiagramService(
    InfraOrgItDbContext db,
    TopologyService topology,
    EnvironmentAccessService access)
{
    public Task<TopologyDto> BuildTopologyAsync(Diagram diagram, CancellationToken ct) =>
        topology.BuildAsync(ToFilter(diagram.ScopeItems), ct);

    public async Task RequireWriteAsync(
        System.Security.Claims.ClaimsPrincipal user,
        IReadOnlyCollection<DiagramScopeItem> current,
        DiagramInput input,
        bool currentIsDefault,
        CancellationToken ct,
        bool isNew = false)
    {
        var incoming = input.ScopeItems
            ?.Select(x => (
                ScopeType: Guard.Enum<DiagramScopeType>(x.ScopeType, "ScopeType"),
                EntityId: x.EntityId))
            .ToList() ?? [];
        var currentKeys = current.Select(x => (x.ScopeType, x.EntityId)).ToList();

        var special = incoming.Concat(currentKeys)
            .Any(x => x.ScopeType is nameof(DiagramScopeType.Provider) or nameof(DiagramScopeType.Project));
        if (special || input.IsDefault || currentIsDefault ||
            incoming.Count == 0 || (!isNew && current.Count == 0))
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            return;
        }

        var environmentIds = incoming.Concat(currentKeys)
            .Where(x => x.ScopeType == nameof(DiagramScopeType.ProviderEnvironment))
            .Select(x => x.EntityId)
            .Distinct()
            .ToList();

        var providerEnvironmentIds = await db.ProviderEnvironments
            .Where(x => environmentIds.Contains(x.EnvironmentDefinitionId))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (providerEnvironmentIds.Count == 0)
            throw Guard.Forbidden("Nessun ambiente amministrabile negli scope del diagramma.");

        await access.RequireAdminAsync(user, providerEnvironmentIds, ct);
    }

    public async Task ValidateScopesAsync(
        IReadOnlyCollection<DiagramScopeInput>? scopeItems,
        CancellationToken ct)
    {
        if (scopeItems is null)
            return;

        foreach (var item in scopeItems)
        {
            var type = Guard.Enum<DiagramScopeType>(item.ScopeType, "ScopeType");
            var exists = type switch
            {
                "Provider" => await db.Providers.AnyAsync(x => x.Id == item.EntityId, ct),
                "ProviderEnvironment" => await db.EnvironmentDefinitions.AnyAsync(x => x.Id == item.EntityId, ct),
                "Project" => await db.Projects.AnyAsync(x => x.Id == item.EntityId, ct),
                _ => false
            };
            if (!exists)
                throw Guard.NotFound(type, item.EntityId);
        }
    }

    public async Task ClearDefaultAsync(int? exceptId, CancellationToken ct)
    {
        var current = await db.Diagrams
            .Where(x => x.IsDefault && (exceptId == null || x.Id != exceptId))
            .ToListAsync(ct);
        foreach (var diagram in current)
            diagram.IsDefault = false;
    }

    public static DiagramScopeItem NewScope(int diagramId, DiagramScopeInput input, string scopeType) => new()
    {
        DiagramId = diagramId,
        ScopeType = scopeType,
        EntityId = input.EntityId
    };

    public async Task<DiagramGraphDto> GetGraphAsync(Diagram diagram, CancellationToken ct)
    {
        var model = await topology.BuildAsync(ToFilter(diagram.ScopeItems), ct);
        var inScope = InScope(model);

        return new DiagramGraphDto(
            ToDto(diagram),
            model,
            diagram.Nodes
                .Where(x => inScope.Keys.Contains((x.NodeType, x.EntityId)))
                .OrderBy(x => x.Id)
                .Select(ToDto)
                .ToList(),
            diagram.Edges
                .Where(x => inScope.HasEdge(x.ProxyConnectionId, x.TargetNodeType, x.TargetEntityId))
                .OrderBy(x => x.Id)
                .Select(ToDto)
                .ToList());
    }

    public async Task ReplaceNodesAsync(
        Diagram diagram,
        IReadOnlyCollection<DiagramNodeInput>? nodes,
        CancellationToken ct)
    {
        var model = await topology.BuildAsync(ToFilter(diagram.ScopeItems), ct);
        var inScope = InScope(model);

        foreach (var incoming in nodes ?? [])
        {
            var nodeType = Guard.Enum<DiagramNodeType>(incoming.NodeType, "NodeType");
            if (!inScope.Keys.Contains((nodeType, incoming.EntityId)))
                continue;

            if (!double.IsFinite(incoming.X) || !double.IsFinite(incoming.Y))
                throw Guard.Unprocessable("Coordinate del nodo non valide.");

            var existing = diagram.Nodes
                .FirstOrDefault(x => x.NodeType == nodeType && x.EntityId == incoming.EntityId);
            if (existing is null)
            {
                diagram.Nodes.Add(new DiagramNode
                {
                    DiagramId = diagram.Id,
                    NodeType = nodeType,
                    EntityId = incoming.EntityId,
                    X = incoming.X,
                    Y = incoming.Y,
                    Width = incoming.Width,
                    Height = incoming.Height,
                    Collapsed = incoming.Collapsed
                });
                continue;
            }

            existing.X = incoming.X;
            existing.Y = incoming.Y;
            existing.Width = incoming.Width;
            existing.Height = incoming.Height;
            existing.Collapsed = incoming.Collapsed;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task ReplaceEdgesAsync(
        Diagram diagram,
        IReadOnlyCollection<DiagramEdgeInput>? edges,
        CancellationToken ct)
    {
        var incoming = edges ?? [];
        if (incoming.Any(x => x.SourceHandle?.Length > 50 || x.TargetHandle?.Length > 50))
            throw Guard.Unprocessable("Handle del collegamento non valido.");

        var model = await topology.BuildAsync(ToFilter(diagram.ScopeItems), ct);
        var inScope = InScope(model);

        db.DiagramEdges.RemoveRange(diagram.Edges
            .Where(x => inScope.HasEdge(x.ProxyConnectionId, x.TargetNodeType, x.TargetEntityId)));

        db.DiagramEdges.AddRange(incoming
            .Select(x => new
            {
                Edge = x,
                TargetNodeType = Guard.Enum<DiagramNodeType>(x.TargetNodeType, "TargetNodeType")
            })
            .Where(x => inScope.HasEdge(x.Edge.ProxyConnectionId, x.TargetNodeType, x.Edge.TargetEntityId))
            .GroupBy(x => (x.Edge.ProxyConnectionId, x.TargetNodeType, x.Edge.TargetEntityId))
            .Select(g => g.Last())
            .Select(x => new DiagramEdge
            {
                DiagramId = diagram.Id,
                ProxyConnectionId = x.Edge.ProxyConnectionId,
                TargetNodeType = x.TargetNodeType,
                TargetEntityId = x.Edge.TargetEntityId,
                SourceHandle = x.Edge.SourceHandle,
                TargetHandle = x.Edge.TargetHandle,
                BendX = x.Edge.BendX,
                BendY = x.Edge.BendY
            }));

        await db.SaveChangesAsync(ct);
    }

    public static DiagramDto ToDto(Diagram diagram) => new(
        diagram.Id,
        diagram.Name,
        diagram.Description,
        diagram.IsDefault,
        diagram.ScopeItems.OrderBy(x => x.Id)
            .Select(x => new DiagramScopeItemDto(x.ScopeType, x.EntityId))
            .ToList(),
        diagram.CreatedAt,
        diagram.UpdatedAt,
        diagram.Version);

    public static DiagramNodeDto ToDto(DiagramNode node) => new(
        node.NodeType, node.EntityId, node.X, node.Y, node.Width, node.Height, node.Collapsed);

    public static DiagramEdgeDto ToDto(DiagramEdge edge) => new(
        edge.ProxyConnectionId,
        edge.TargetNodeType,
        edge.TargetEntityId,
        edge.SourceHandle,
        edge.TargetHandle,
        edge.BendX,
        edge.BendY);

    private static TopologyFilter ToFilter(IReadOnlyCollection<DiagramScopeItem> scopeItems)
    {
        int? EntityOf(string type) => scopeItems
            .FirstOrDefault(x => x.ScopeType == type)?.EntityId;

        return new TopologyFilter(
            EntityOf(nameof(DiagramScopeType.Provider)),
            EntityOf(nameof(DiagramScopeType.ProviderEnvironment)),
            EntityOf(nameof(DiagramScopeType.Project)));
    }

    private static (HashSet<(string, int)> Keys, Func<int, string, int, bool> HasEdge) InScope(TopologyDto model)
    {
        var keys = new HashSet<(string, int)>();
        foreach (var provider in model.Providers)
        {
            keys.Add((nameof(DiagramNodeType.Provider), provider.Id));
            foreach (var environment in provider.Environments)
            {
                keys.Add((nameof(DiagramNodeType.ProviderEnvironment), environment.Id));
                foreach (var server in environment.Servers)
                {
                    keys.Add((nameof(DiagramNodeType.Server), server.Id));
                    foreach (var application in server.Applications)
                        keys.Add((nameof(DiagramNodeType.ServerApplication), application.Id));
                }
            }
        }

        var connectionIds = model.ProxyConnections.Select(x => x.Id).ToHashSet();
        return (keys, (connectionId, targetType, targetId) =>
            connectionIds.Contains(connectionId) && keys.Contains((targetType, targetId)));
    }
}

public sealed record DiagramInput(
    string? Name,
    string? Description,
    bool IsDefault,
    List<DiagramScopeInput>? ScopeItems);

public sealed record DiagramScopeInput(string? ScopeType, int EntityId);

public sealed record DiagramDto(
    int Id,
    string Name,
    string? Description,
    bool IsDefault,
    List<DiagramScopeItemDto> ScopeItems,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record DiagramScopeItemDto(string ScopeType, int EntityId);

public sealed record DiagramNodeInput(
    string? NodeType,
    int EntityId,
    double X,
    double Y,
    double? Width,
    double? Height,
    bool Collapsed);

public sealed record DiagramNodeDto(
    string NodeType,
    int EntityId,
    double X,
    double Y,
    double? Width,
    double? Height,
    bool Collapsed);

public sealed record DiagramEdgeInput(
    int ProxyConnectionId,
    string? TargetNodeType,
    int TargetEntityId,
    string? SourceHandle,
    string? TargetHandle,
    double? BendX,
    double? BendY);

public sealed record DiagramEdgeDto(
    int ProxyConnectionId,
    string TargetNodeType,
    int TargetEntityId,
    string? SourceHandle,
    string? TargetHandle,
    double? BendX,
    double? BendY);

public sealed record DiagramGraphDto(
    DiagramDto Diagram,
    TopologyDto Topology,
    List<DiagramNodeDto> Nodes,
    List<DiagramEdgeDto> Edges);
