namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Extra permission for MCP action tools. It never overrides the environment
// RBAC: it only enables an action the user could already run in the browser.
public sealed class McpGrant
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public int? GroupId { get; set; }
    public string Action { get; set; } = "";
    public int? EnvironmentDefinitionId { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public Group? Group { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
}
