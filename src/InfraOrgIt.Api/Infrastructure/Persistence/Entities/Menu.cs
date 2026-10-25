namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Menu : EntityBase
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool HideBackButton { get; set; }
    public bool IsVisible { get; set; } = true;
    public int? Order { get; set; }
    public int? ParentId { get; set; }
    public Menu? Parent { get; set; }
    public List<Menu> Childrens { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
}
