namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ProxyConnection : EntityBase
{
    public int SourceServerId { get; set; }
    public int TargetServerId { get; set; }
    public string Direction { get; set; } = "Outgoing";
    public Server? SourceServer { get; set; }
    public Server? TargetServer { get; set; }
    public List<ProxyRoute> Routes { get; set; } = [];
}
