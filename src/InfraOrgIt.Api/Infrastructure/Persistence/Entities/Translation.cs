namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Translation of a database-driven label (menu name, role label).
/// Adding a language means adding rows with a new <see cref="Language"/> code:
/// the schema does not change.
/// </summary>
public sealed class Translation : EntityBase
{
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Field { get; set; } = "";
    public string Language { get; set; } = "";
    public string Value { get; set; } = "";
}
