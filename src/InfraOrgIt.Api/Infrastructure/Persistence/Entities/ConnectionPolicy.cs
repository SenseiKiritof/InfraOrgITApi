namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Singleton row (Id = 1) shared by every API node in a cluster. The mode is
/// installation-wide, not per node, so a load-balanced deployment reads the
/// same value from MySQL.
/// </summary>
public sealed class ConnectionPolicy : EntityBase
{
    public string Mode { get; set; } = "Auto";
    public string UpdatedBy { get; set; } = "";
}
