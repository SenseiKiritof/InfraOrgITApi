namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// API key coming from a Swagger/OpenAPI security scheme. The value is write-only:
/// it is encrypted at rest and never returned by the API.
/// <see cref="EnvironmentDefinitionId"/> null means the key applies to every environment.
/// </summary>
public sealed class ApplicationApiKey : EntityBase
{
    public int ApplicationId { get; set; }
    public int? EnvironmentDefinitionId { get; set; }
    public string Name { get; set; } = "";
    public string Location { get; set; } = "Header";
    public string EncryptedValue { get; set; } = "";
    public int KeyVersion { get; set; }
    public Application? Application { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
}
