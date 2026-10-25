namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ProviderEnvironment : EntityBase
{
    public int ProviderId { get; set; }
    public int EnvironmentDefinitionId { get; set; }
    public Provider? Provider { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
    public List<Server> Servers { get; set; } = [];
    public List<ProviderEnvironmentGroupRole> GroupRoles { get; set; } = [];

    public string Label => $"{Provider?.Name} / {EnvironmentDefinition?.Name}";
}
