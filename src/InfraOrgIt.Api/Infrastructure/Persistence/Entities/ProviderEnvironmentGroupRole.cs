namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class ProviderEnvironmentGroupRole : EntityBase
{
    public int ProviderEnvironmentId { get; set; }
    public int GroupId { get; set; }
    public int RoleId { get; set; }
    public ProviderEnvironment? ProviderEnvironment { get; set; }
    public Group? Group { get; set; }
    public Role? Role { get; set; }
}
