namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ApplicationApiQueryParam : EntityBase
{
    public int ApplicationApiId { get; set; }
    public string Name { get; set; } = "";
    public string ParamType { get; set; } = "String";
    public ApplicationVersionApi? ApplicationApi { get; set; }
}
