namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ApplicationVersionApi : EntityBase
{
    public int ApplicationVersionId { get; set; }
    public string Url { get; set; } = "";
    public string RequestTypeEnum { get; set; } = "GET";
    public string Description { get; set; } = "";
    public string? Body { get; set; }
    public ApplicationVersion? ApplicationVersion { get; set; }
    public List<ApplicationApiQueryParam> QueryParams { get; set; } = [];
}
