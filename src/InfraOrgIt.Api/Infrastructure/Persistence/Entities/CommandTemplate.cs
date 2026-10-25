namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class CommandTemplate : EntityBase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Script { get; set; } = "";
}
