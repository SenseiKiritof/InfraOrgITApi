using InfraOrgIt.Api.BuildingBlocks.Authorization;
using InfraOrgIt.Api.BuildingBlocks.Errors;
using InfraOrgIt.Api.BuildingBlocks.Validation;
using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Topology;

public sealed class TopologyService(InfraOrgItDbContext db)
{
    private IQueryable<Server> ServersQuery(TopologyFilter filter)
    {
        var query = db.Servers
            .AsNoTrackingWithIdentityResolution()
            .Include(x => x.ProviderEnvironment!).ThenInclude(x => x!.Provider)
            .Include(x => x.ProviderEnvironment!).ThenInclude(x => x!.EnvironmentDefinition)
            .Include(x => x.ServerApplications).ThenInclude(x => x.ApplicationVersion!)
            .ThenInclude(x => x!.Application!).ThenInclude(x => x!.Projects)
            .AsQueryable();

        if (filter.ProviderId is not null)
            query = query.Where(x => x.ProviderEnvironment!.ProviderId == filter.ProviderId);
        if (filter.EnvironmentId is not null)
            query = query.Where(x => x.ProviderEnvironment!.EnvironmentDefinitionId == filter.EnvironmentId);
        if (filter.ProjectId is not null)
            query = query.Where(x => x.ServerApplications.Any(sa =>
                sa.ApplicationVersion!.Application!.Projects.Any(p => p.Id == filter.ProjectId)));

        return query;
    }

    public async Task<TopologyDto> BuildAsync(TopologyFilter filter, CancellationToken ct)
    {
        var servers = await ServersQuery(filter).ToListAsync(ct);

        if (filter.ProjectId is not null)
        {
            var projectServerIds = servers.Select(x => x.Id).ToList();
            var links = await db.ProxyConnections
                .Where(c => projectServerIds.Contains(c.SourceServerId) ||
                            projectServerIds.Contains(c.TargetServerId))
                .Select(c => new { c.SourceServerId, c.TargetServerId })
                .ToListAsync(ct);

            var linkedIds = links
                .SelectMany(x => new[] { x.SourceServerId, x.TargetServerId })
                .Except(projectServerIds)
                .ToList();

            servers.AddRange(await ServersQuery(new TopologyFilter(
                    filter.ProviderId, filter.EnvironmentId, null))
                .Where(x => linkedIds.Contains(x.Id))
                .ToListAsync(ct));
        }

        var serverIds = servers.Select(x => x.Id).ToHashSet();
        var serversByEnvironment = servers.ToLookup(x => x.ProviderEnvironmentId);
        var keepEmpty = filter.ProjectId is null;

        var dbProviders = await db.Providers
            .AsNoTracking()
            .Include(x => x.ProviderEnvironments).ThenInclude(x => x.EnvironmentDefinition)
            .Where(x => filter.ProviderId == null || x.Id == filter.ProviderId)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var providers = dbProviders
            .Select(provider =>
            {
                var environments = provider.ProviderEnvironments
                    .Where(pe => filter.EnvironmentId == null ||
                                 pe.EnvironmentDefinitionId == filter.EnvironmentId)
                    .Where(pe => keepEmpty || serversByEnvironment.Contains(pe.Id))
                    .OrderBy(pe => pe.Id)
                    .Select(pe =>
                    {
                        var serverModels = serversByEnvironment[pe.Id]
                            .OrderBy(s => s.Id)
                            .Select(s =>
                            {
                                var applications = s.ServerApplications.Select(sa =>
                                {
                                    var version = sa.ApplicationVersion!;
                                    var projects = version.Application!.Projects
                                        .Select(p => new TopologyProjectDto(p.Id, p.Name))
                                        .ToList();
                                    return new TopologyApplicationDto(
                                        sa.Id,
                                        version.Id,
                                        version.Application!.Name,
                                        version.VersionNumber,
                                        sa.ExposedPort,
                                        projects);
                                }).ToList();

                                return new TopologyServerDto(
                                    s.Id,
                                    s.Name,
                                    s.IpAddress,
                                    s.Hostname,
                                    applications,
                                    applications.SelectMany(a => a.Projects)
                                        .DistinctBy(p => p.Id)
                                        .ToList());
                            })
                            .ToList();

                        return new TopologyEnvironmentDto(
                            pe.Id,
                            pe.EnvironmentDefinitionId,
                            pe.EnvironmentDefinition!.Name,
                            serverModels,
                            serverModels.SelectMany(s => s.Projects)
                                .DistinctBy(p => p.Id)
                                .ToList());
                    })
                    .ToList();

                return new TopologyProviderDto(
                    provider.Id,
                    provider.Name,
                    environments,
                    environments.SelectMany(e => e.Projects).DistinctBy(p => p.Id).ToList());
            })
            .Where(p => p.Environments.Count != 0 || (keepEmpty && filter.EnvironmentId == null))
            .ToList();

        var connections = await db.ProxyConnections
            .AsNoTracking()
            .Include(x => x.Routes)
            .Where(x => serverIds.Contains(x.SourceServerId) && serverIds.Contains(x.TargetServerId))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        var projectsByServerId = providers
            .SelectMany(p => p.Environments)
            .SelectMany(e => e.Servers)
            .ToDictionary(s => s.Id, s => s.Projects);

        return new TopologyDto(
            providers,
            connections.Select(c => new TopologyProxyConnectionDto(
                c.Id,
                c.SourceServerId,
                c.TargetServerId,
                c.Direction,
                projectsByServerId.GetValueOrDefault(c.SourceServerId, [])
                    .Concat(projectsByServerId.GetValueOrDefault(c.TargetServerId, []))
                    .DistinctBy(p => p.Id)
                    .ToList(),
                c.Routes.OrderBy(r => r.Id).Select(r => new TopologyProxyRouteDto(
                    r.Id, r.Route, r.MappedTo, r.TargetServerApplicationId,
                    r.VirtualHost, r.ListenPort)).ToList()))
            .ToList());
    }

    public async Task<DeletePreviewDto> GetDeletePreviewAsync(DiagramNodeType nodeType, int id, CancellationToken ct)
    {
        var subtree = await CollectSubtreeAsync(nodeType, id, ct);
        return new DeletePreviewDto(
            subtree.Environments.Count,
            subtree.Servers.Select(s => s.Name).ToList(),
            subtree.Applications.Select(sa =>
                $"{sa.ApplicationVersion!.Application!.Name} {sa.ApplicationVersion.VersionNumber}").ToList(),
            subtree.Connections.Count,
            subtree.Connections.Sum(c => c.Routes.Count),
            subtree.ForwardingRoutes.Count,
            subtree.GroupRoles.Count);
    }

    public async Task DeleteNodeAsync(
        DiagramNodeType nodeType,
        int id,
        EnvironmentAccessService access,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        await RequireDeleteAsync(nodeType, id, access, user, ct);
        var subtree = await CollectSubtreeAsync(nodeType, id, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        foreach (var route in subtree.ForwardingRoutes)
        {
            if (route.TargetServerApplicationId is not null &&
                subtree.Applications.Any(sa => sa.Id == route.TargetServerApplicationId))
                route.TargetServerApplicationId = null;
            if (route.TargetProxyConnectionId is not null &&
                subtree.Connections.Any(c => c.Id == route.TargetProxyConnectionId))
                route.TargetProxyConnectionId = null;
        }

        foreach (var deploy in subtree.DeploysOnRemovedRoutes)
            deploy.ProxyRouteId = null;
        await db.SaveChangesAsync(ct);

        db.ProxyRoutes.RemoveRange(subtree.Connections.SelectMany(c => c.Routes));
        db.ProxyConnections.RemoveRange(subtree.Connections);
        db.ServerApplications.RemoveRange(subtree.Applications);
        db.Servers.RemoveRange(subtree.Servers);
        db.ProviderEnvironmentGroupRoles.RemoveRange(subtree.GroupRoles);
        db.ProviderEnvironments.RemoveRange(subtree.Environments);
        if (subtree.Provider is not null)
            db.Providers.Remove(subtree.Provider);
        db.DiagramNodes.RemoveRange(subtree.DiagramNodes);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    private async Task RequireDeleteAsync(
        DiagramNodeType nodeType,
        int id,
        EnvironmentAccessService access,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (nodeType is DiagramNodeType.Provider or DiagramNodeType.ProviderEnvironment)
        {
            EnvironmentAccessService.RequireSystemAdmin(user);
            return;
        }

        var environmentId = nodeType switch
        {
            DiagramNodeType.Server => await db.Servers
                .Where(x => x.Id == id)
                .Select(x => (int?)x.ProviderEnvironmentId)
                .SingleOrDefaultAsync(ct),
            DiagramNodeType.ServerApplication => await db.ServerApplications
                .Where(x => x.Id == id)
                .Select(x => (int?)x.Server!.ProviderEnvironmentId)
                .SingleOrDefaultAsync(ct),
            _ => null
        } ?? throw Guard.NotFound("Nodo", id);

        await access.RequireAdminAsync(user, [environmentId], ct);
    }

    private sealed record Subtree(
        Provider? Provider,
        List<ProviderEnvironment> Environments,
        List<ProviderEnvironmentGroupRole> GroupRoles,
        List<Server> Servers,
        List<ServerApplication> Applications,
        List<ProxyConnection> Connections,
        List<ProxyRoute> ForwardingRoutes,
        List<ServerApplication> DeploysOnRemovedRoutes,
        List<DiagramNode> DiagramNodes);

    private async Task<Subtree> CollectSubtreeAsync(DiagramNodeType nodeType, int id, CancellationToken ct)
    {
        Provider? provider = null;
        List<ProviderEnvironment> environments = [];
        List<Server> servers = [];

        switch (nodeType)
        {
            case DiagramNodeType.Provider:
                provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id, ct)
                           ?? throw Guard.NotFound("Provider", id);
                environments = await db.ProviderEnvironments
                    .Where(x => x.ProviderId == id).ToListAsync(ct);
                break;
            case DiagramNodeType.ProviderEnvironment:
                environments =
                [
                    await db.ProviderEnvironments.SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw Guard.NotFound("Ambiente provider", id)
                ];
                break;
            case DiagramNodeType.Server:
                servers =
                [
                    await db.Servers.SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw Guard.NotFound("Server", id)
                ];
                break;
        }

        var environmentIds = environments.Select(x => x.Id).ToList();
        if (environmentIds.Count != 0)
        {
            servers = await db.Servers
                .Where(x => environmentIds.Contains(x.ProviderEnvironmentId))
                .ToListAsync(ct);
        }

        var serverIds = servers.Select(x => x.Id).ToList();
        var applicationsQuery = db.ServerApplications
            .Include(x => x.ApplicationVersion)!.ThenInclude(x => x!.Application);

        var applications = nodeType == DiagramNodeType.ServerApplication
            ? [await applicationsQuery.SingleOrDefaultAsync(x => x.Id == id, ct)
               ?? throw Guard.NotFound("Applicazione deployata", id)]
            : await applicationsQuery.Where(x => serverIds.Contains(x.ServerId)).ToListAsync(ct);

        var applicationIds = applications.Select(x => x.Id).ToList();

        var connections = await db.ProxyConnections
            .Include(x => x.Routes)
            .Where(x => serverIds.Contains(x.SourceServerId) || serverIds.Contains(x.TargetServerId))
            .ToListAsync(ct);

        var connectionIds = connections.Select(x => x.Id).ToList();
        var routeIds = connections.SelectMany(x => x.Routes).Select(x => x.Id).ToList();

        var forwardingRoutes = await db.ProxyRoutes
            .Where(x => !connectionIds.Contains(x.ProxyConnectionId) &&
                        ((x.TargetServerApplicationId != null &&
                          applicationIds.Contains(x.TargetServerApplicationId.Value)) ||
                         (x.TargetProxyConnectionId != null &&
                          connectionIds.Contains(x.TargetProxyConnectionId.Value))))
            .ToListAsync(ct);

        var deploysOnRemovedRoutes = await db.ServerApplications
            .Where(x => x.ProxyRouteId != null && routeIds.Contains(x.ProxyRouteId.Value))
            .ToListAsync(ct);

        var groupRoles = await db.ProviderEnvironmentGroupRoles
            .Where(x => environmentIds.Contains(x.ProviderEnvironmentId))
            .ToListAsync(ct);

        var removedKeys = environmentIds.Select(x => (DiagramNodeType.ProviderEnvironment, x))
            .Concat(serverIds.Select(x => (DiagramNodeType.Server, x)))
            .Concat(applicationIds.Select(x => (DiagramNodeType.ServerApplication, x)))
            .ToHashSet();
        if (provider is not null)
            removedKeys.Add((DiagramNodeType.Provider, provider.Id));

        var entityIds = removedKeys.Select(k => k.Item2).Distinct().ToList();
        var diagramNodes = (await db.DiagramNodes
                .Where(x => entityIds.Contains(x.EntityId))
                .ToListAsync(ct))
            .Where(x => removedKeys.Contains((Enum.Parse<DiagramNodeType>(x.NodeType), x.EntityId)))
            .ToList();

        return new Subtree(
            provider,
            environments,
            groupRoles,
            servers,
            applications,
            connections,
            forwardingRoutes,
            deploysOnRemovedRoutes,
            diagramNodes);
    }
}
