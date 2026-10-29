namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ProxyRoute : EntityBase
{
    public int ProxyConnectionId { get; set; }
    public string Route { get; set; } = "";
    public string? VirtualHost { get; set; }
    public int? ListenPort { get; set; }
    public string? MappedTo { get; set; }
    public int? TargetServerApplicationId { get; set; }
    public int? TargetProxyConnectionId { get; set; }
    public ProxyConnection? ProxyConnection { get; set; }
    public ServerApplication? TargetServerApplication { get; set; }
    public ProxyConnection? TargetProxyConnection { get; set; }
}
