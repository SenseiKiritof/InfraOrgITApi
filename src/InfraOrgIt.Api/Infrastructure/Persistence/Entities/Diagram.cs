namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class Diagram : EntityBase
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public List<DiagramScopeItem> ScopeItems { get; set; } = [];
    public List<DiagramNode> Nodes { get; set; } = [];
    public List<DiagramEdge> Edges { get; set; } = [];
}
