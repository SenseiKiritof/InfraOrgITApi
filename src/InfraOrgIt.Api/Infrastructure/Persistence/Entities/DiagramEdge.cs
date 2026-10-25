namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class DiagramEdge : EntityBase
{
    public int DiagramId { get; set; }
    public int ProxyConnectionId { get; set; }
    public string TargetNodeType { get; set; } = "Server";
    public int TargetEntityId { get; set; }
    public string? SourceHandle { get; set; }
    public string? TargetHandle { get; set; }
    public double? BendX { get; set; }
    public double? BendY { get; set; }
    public Diagram? Diagram { get; set; }
}
