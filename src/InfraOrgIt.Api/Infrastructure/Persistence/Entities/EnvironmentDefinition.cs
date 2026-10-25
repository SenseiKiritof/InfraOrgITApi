namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class EnvironmentDefinition : EntityBase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ProviderEnvironment> ProviderEnvironments { get; set; } = [];
}
