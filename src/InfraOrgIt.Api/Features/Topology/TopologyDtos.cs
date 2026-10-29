namespace InfraOrgIt.Api.Features.Topology;

public sealed record TopologyDto(
    List<TopologyProviderDto> Providers,
    List<TopologyProxyConnectionDto> ProxyConnections);

public sealed record TopologyProviderDto(
    int Id,
    string Name,
    List<TopologyEnvironmentDto> Environments,
    List<TopologyProjectDto> Projects);

public sealed record TopologyEnvironmentDto(
    int Id,
    int EnvironmentId,
    string EnvironmentName,
    List<TopologyServerDto> Servers,
    List<TopologyProjectDto> Projects);

public sealed record TopologyServerDto(
    int Id,
    string Name,
    string IpAddress,
    string Hostname,
    List<TopologyApplicationDto> Applications,
    List<TopologyProjectDto> Projects);

public sealed record TopologyApplicationDto(
    int Id,
    int ApplicationVersionId,
    string ApplicationName,
    string VersionNumber,
    int ExposedPort,
    List<TopologyProjectDto> Projects);

public sealed record TopologyProjectDto(int Id, string Name);

public sealed record TopologyProxyConnectionDto(
    int Id,
    int SourceServerId,
    int TargetServerId,
    string Direction,
    List<TopologyProjectDto> Projects,
    List<TopologyProxyRouteDto> Routes);

public sealed record TopologyProxyRouteDto(
    int Id,
    string Route,
    string? MappedTo,
    int? TargetServerApplicationId,
    string? VirtualHost,
    int? ListenPort);

public sealed record TopologyFilter(int? ProviderId, int? EnvironmentId, int? ProjectId);

public sealed record DeletePreviewDto(
    int Environments,
    List<string> Servers,
    List<string> Applications,
    int ProxyConnections,
    int ProxyRoutes,
    int RoutesLosingTarget,
    int GroupRoles);

public enum DiagramNodeType
{
    Provider,
    ProviderEnvironment,
    Server,
    ServerApplication
}

public enum DiagramScopeType
{
    Provider,
    ProviderEnvironment,
    Project
}
