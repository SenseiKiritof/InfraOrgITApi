namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Saved test call executed by a user against an application API.
/// The response body is stored capped; <see cref="Truncated"/> marks the cap hit.
/// </summary>
public sealed class ApplicationApiCall : EntityBase
{
    public string Username { get; set; } = "";
    public int? ApplicationVersionApiId { get; set; }
    public int EnvironmentDefinitionId { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string? QueryJson { get; set; }
    public string? RequestBody { get; set; }
    public int? Status { get; set; }
    public string? ContentType { get; set; }
    public string? Reason { get; set; }
    public long DurationMs { get; set; }
    public string? ResponseBody { get; set; }
    public long ResponseSizeBytes { get; set; }
    public bool Truncated { get; set; }
    /// <summary>ResponseBody is base64 (binary payload) instead of text.</summary>
    public bool Binary { get; set; }
    public ApplicationVersionApi? ApplicationVersionApi { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
}
