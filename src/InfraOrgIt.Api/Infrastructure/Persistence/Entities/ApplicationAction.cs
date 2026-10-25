namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ApplicationAction : EntityBase
{
    public int ApplicationId { get; set; }
    public string Label { get; set; } = "";
    public int Order { get; set; }
    public int? CommandTemplateId { get; set; }
    public string? CustomScript { get; set; }
    public string ParameterValuesJson { get; set; } = "{}";
    public bool RequiresConfirmation { get; set; }
    public Application? Application { get; set; }
    public CommandTemplate? CommandTemplate { get; set; }
}
