using System.ComponentModel;
using ModelContextProtocol.Server;

namespace InfraOrgIt.Api.Features.Mcp;

/// <summary>
/// Operational prompt templates. They only describe how to use the existing
/// tools; they add no data access and no permissions.
/// </summary>
[McpServerPromptType]
public static class InventoryPrompts
{
    [McpServerPrompt(Name = "diagnose-server")]
    [Description("Guide an inventory-based diagnosis of one server.")]
    public static string DiagnoseServer(
        [Description("Server id")] string serverId) =>
        $"""
        Diagnose server {serverId} using only InfraOrgIT data.

        1. Call get_server with id {serverId}.
        2. Call list_deployments with serverId {serverId}.
        3. If reachability results are needed, ask the user before calling
           run_reachability_tests with serverId {serverId} and confirm=true.

        Summarize environment, sizing, OS, deployments and reachability.
        Separate observed data from hypotheses; do not invent values.
        """;

    [McpServerPrompt(Name = "application-impact")]
    [Description("Guide an inventory-based impact analysis of one application.")]
    public static string ApplicationImpact(
        [Description("Application id")] string applicationId) =>
        $"""
        Find where application {applicationId} runs using only InfraOrgIT data.

        1. Call list_applications to confirm the application name.
        2. Call list_deployments with applicationId {applicationId}.
        3. Call get_server for every server that hosts it.

        Summarize versions, servers, exposed ports and HA roles. List the
        affected environments first, then per-server detail; do not invent
        values.
        """;
}
