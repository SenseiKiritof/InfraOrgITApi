namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class DiagramScopeItem : EntityBase
{
    public int DiagramId { get; set; }
    public string ScopeType { get; set; } = "Provider";
    public int EntityId { get; set; }
    public Diagram? Diagram { get; set; }
}
