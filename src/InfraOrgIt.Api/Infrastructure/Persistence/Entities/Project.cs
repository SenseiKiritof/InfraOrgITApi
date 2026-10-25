namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Project : EntityBase
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<Application> Applications { get; set; } = [];
}
