namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>Singleton row (Id = 1) with the installation-wide OS update policy.</summary>
public sealed class ServerUpdatePolicy : EntityBase
{
    public bool Enabled { get; set; } = true;
    public bool LtsOnly { get; set; } = true;
    public string UpdatedBy { get; set; } = "";
}
