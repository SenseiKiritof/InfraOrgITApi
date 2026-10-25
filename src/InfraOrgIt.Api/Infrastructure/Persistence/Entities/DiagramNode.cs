namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

public sealed class DiagramNode : EntityBase
{
    public int DiagramId { get; set; }
    public string NodeType { get; set; } = "Provider";
    public int EntityId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public bool Collapsed { get; set; }
    public Diagram? Diagram { get; set; }
}
