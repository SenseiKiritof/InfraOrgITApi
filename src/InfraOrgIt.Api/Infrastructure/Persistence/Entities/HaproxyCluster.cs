namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>HAProxy nodes managed together (active/active or active/passive).</summary>
public sealed class HaproxyCluster : EntityBase
{
    public int ProviderEnvironmentId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>active-active or active-passive.</summary>
    public string Mode { get; set; } = "active-active";
    public string? Vip { get; set; }
    public string? Notes { get; set; }
    public ProviderEnvironment? ProviderEnvironment { get; set; }
    public List<HaproxyInstance> Instances { get; set; } = [];
}
