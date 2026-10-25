namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class MenuRole
{
    public int MenuId { get; set; }
    public Menu Menu { get; set; } = null!;
    public int RolesId { get; set; }
    public Role Role { get; set; } = null!;
}
