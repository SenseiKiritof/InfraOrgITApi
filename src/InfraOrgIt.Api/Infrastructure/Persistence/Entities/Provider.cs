namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Provider : EntityBase
{
    public string Name { get; set; } = "";
    public List<ProviderEnvironment> ProviderEnvironments { get; set; } = [];
}
