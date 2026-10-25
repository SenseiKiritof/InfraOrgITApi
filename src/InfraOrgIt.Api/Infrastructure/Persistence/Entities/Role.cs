namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Role : EntityBase
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string ScopeEnum { get; set; } = "System";
    public List<User> Users { get; set; } = [];
}
