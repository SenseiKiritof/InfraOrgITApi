namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class User : EntityBase
{
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string Username { get; set; } = "";
    public List<Group> Groups { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
}
