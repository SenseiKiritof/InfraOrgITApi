namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Group : EntityBase
{
    public string Name { get; set; } = "";
    public List<User> Users { get; set; } = [];
}
