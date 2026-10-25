namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ReachabilityTest : EntityBase
{
    public int? ServerId { get; set; }
    public string Hostname { get; set; } = "";
    public int Port { get; set; }
    public string Description { get; set; } = "";
    public Server? Server { get; set; }
}
