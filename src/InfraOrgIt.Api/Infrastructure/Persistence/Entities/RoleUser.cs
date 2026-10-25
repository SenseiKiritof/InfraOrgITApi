namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class RoleUser
{
    public int RolesId { get; set; }
    public Role Role { get; set; } = null!;
    public int UsersId { get; set; }
    public User User { get; set; } = null!;
}
