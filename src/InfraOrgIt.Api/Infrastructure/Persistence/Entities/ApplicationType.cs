namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ApplicationType : EntityBase
{
    /// <summary>Codice salvato in <see cref="Application.Type"/>; immutabile dopo la creazione.</summary>
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public int SortOrder { get; set; }
    public List<Application> Applications { get; set; } = [];
}
