namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ApplicationProject
{
    public int ApplicationsId { get; set; }
    public int ProjectsId { get; set; }
    public Application? Application { get; set; }
    public Project? Project { get; set; }
}
