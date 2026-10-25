namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ApplicationVersion : EntityBase
{
    public int ApplicationId { get; set; }
    public string VersionNumber { get; set; } = "";
    public Application? Application { get; set; }
    public List<ApplicationVersionApi> Apis { get; set; } = [];
    public List<ServerApplication> ServerApplications { get; set; } = [];
}
