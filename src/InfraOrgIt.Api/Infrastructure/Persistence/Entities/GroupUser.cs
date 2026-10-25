namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class GroupUser
{
    public int GroupsId { get; set; }
    public Group Group { get; set; } = null!;
    public int UsersId { get; set; }
    public User User { get; set; } = null!;
}
