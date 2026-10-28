namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Application : EntityBase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BasePath { get; set; } = "";
    public int ListeningOnPort { get; set; }
    public List<ApplicationVersion> Versions { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    public List<ApplicationApiKey> ApiKeys { get; set; } = [];
    public List<ApplicationEnvironment> Environments { get; set; } = [];
}
