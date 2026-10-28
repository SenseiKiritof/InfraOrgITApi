namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Per-environment target of the application: the hostname used for the test calls
/// and whether TLS certificate validation has to be skipped.
/// </summary>
public sealed class ApplicationEnvironment : EntityBase
{
    public int ApplicationId { get; set; }
    public int EnvironmentDefinitionId { get; set; }
    /// <summary>Origin used for the test calls, e.g. <c>http://host:8080</c>.</summary>
    public string? Hostname { get; set; }
    /// <summary>Skip TLS certificate validation on the test calls (self-signed internal hosts).</summary>
    public bool IgnoreSslErrors { get; set; }
    public Application? Application { get; set; }
    public EnvironmentDefinition? EnvironmentDefinition { get; set; }
}
