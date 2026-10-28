namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Application : EntityBase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string BasePath { get; set; } = "";
    public int ListeningOnPort { get; set; }
    /// <summary>Origin used for the test calls, e.g. <c>http://host:8080</c>. Null means "use the stored API URL".</summary>
    public string? Hostname { get; set; }
    public List<ApplicationVersion> Versions { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
    public List<ApplicationApiKey> ApiKeys { get; set; } = [];
}
