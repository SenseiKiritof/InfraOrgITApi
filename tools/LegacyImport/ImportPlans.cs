using System.Text.Json;

namespace InfraOrgIt.LegacyImport;

public sealed class TablePlan
{
    public required string Legacy { get; init; }
    public required string Target { get; init; }
    public required Dictionary<string, string> Columns { get; init; }
    public Dictionary<string, Func<JsonElement, object?>> Transforms { get; init; } = [];
    public string[] KeyColumns { get; init; } = ["Id"];
    public string[] NaturalKey { get; init; } = [];
    public bool Audit { get; init; } = true;
    public bool Redact { get; init; }
    public List<(string TargetColumn, string LegacyColumn)> Deferred { get; init; } = [];
}

public static class ImportPlans
{
    // Natural keys are target columns. On an Id collision the importer compares them and
    // refuses to reuse the existing row for a different entity.
    private static readonly Dictionary<string, string[]> NaturalKeys = new(StringComparer.Ordinal)
    {
        ["Environments"] = ["Name"],
        ["Providers"] = ["Name"],
        ["ProviderEnvironments"] = ["ProviderId", "EnvironmentDefinitionId"],
        ["Roles"] = ["Name"],
        ["Menus"] = ["Path"],
        ["Users"] = ["Username"],
        ["Groups"] = ["Name"],
        ["ProviderEnvironmentGroupRoles"] = ["ProviderEnvironmentId", "GroupId", "RoleId"],
        ["Applications"] = ["Name"],
        ["ApplicationVersions"] = ["ApplicationId", "VersionNumber"],
        ["ApplicationVersionApis"] = ["ApplicationVersionId", "Url"],
        ["ApplicationApiQueryParams"] = ["ApplicationApiId", "Name"],
        ["Projects"] = ["Name"],
        ["CommandTemplates"] = ["Name"],
        ["ApplicationActions"] = ["ApplicationId", "Label"],
        ["ServerCredentials"] = ["Name", "Username"],
        ["Servers"] = ["ProviderEnvironmentId", "Name"],
        ["ProxyConnections"] = ["SourceServerId", "TargetServerId", "Direction"],
        ["ServerApplications"] = ["ServerId", "ApplicationVersionId", "ExposedPort"],
        ["ProxyRoutes"] = ["ProxyConnectionId", "Route"],
        ["ReachabilityTests"] = ["ServerId", "Hostname", "Port"],
        ["Diagrams"] = ["Name"],
        ["DiagramScopeItems"] = ["DiagramId", "ScopeType", "EntityId"],
        ["DiagramNodes"] = ["DiagramId", "NodeType", "EntityId"],
        ["DiagramEdges"] = ["DiagramId", "ProxyConnectionId", "TargetNodeType", "TargetEntityId"]
    };

    public static readonly List<TablePlan> All =
    [
        Plan("Environments", "EnvironmentDefinitions", ("Id", "Id"), ("Name", "Name"), ("Description", "Description")),
        Plan("Providers", "Providers", ("Id", "Id"), ("Name", "Name")),
        Plan("ProviderEnvironments", "ProviderEnvironments",
            ("Id", "Id"), ("ProviderId", "ProviderId"), ("EnvironmentDefinitionId", "EnvironmentId")),
        Custom("Roles", "Roles",
            [("Id", "Id"), ("Name", "Name"), ("Label", "Label"), ("ScopeEnum", "ScopeEnum")],
            transforms: new() { ["ScopeEnum"] = value => EnumName(value, "System", "Environment") }),
        Custom("Menus", "Menus",
            [
                ("Id", "Id"), ("Name", "Name"), ("Path", "Path"), ("IsActive", "IsActive"),
                ("HideBackButton", "HideBackButton"), ("IsVisible", "IsVisible"), ("Order", "Order")
            ],
            deferred: [("ParentId", "ParentId")]),
        Join("MenuRole", "MenuRole", ("MenuId", "MenuId"), ("RolesId", "RolesId")),
        Plan("Users", "Users", ("Id", "Id"), ("Name", "Name"), ("Surname", "Surname"), ("Username", "Username")),
        Plan("Groups", "Groups", ("Id", "Id"), ("Name", "Name")),
        Join("GroupUser", "GroupUser", ("GroupsId", "GroupsId"), ("UsersId", "UsersId")),
        Join("RoleUser", "RoleUser", ("RolesId", "RolesId"), ("UsersId", "UsersId")),
        Plan("ProviderEnvironmentGroupRoles", "ProviderEnvironmentGroupRoles",
            ("Id", "Id"), ("ProviderEnvironmentId", "ProviderEnvironmentId"),
            ("GroupId", "GroupId"), ("RoleId", "RoleId")),
        Plan("Applications", "Applications",
            ("Id", "Id"), ("Name", "Name"), ("Description", "Description"),
            ("BasePath", "BasePath"), ("ListeningOnPort", "ListeningOnPort")),
        Plan("ApplicationVersions", "ApplicationVersions",
            ("Id", "Id"), ("ApplicationId", "ApplicationId"), ("VersionNumber", "VersionNumber")),
        Custom("ApplicationVersionApis", "ApplicationVersionApis",
            [
                ("Id", "Id"), ("ApplicationVersionId", "ApplicationVersionId"), ("Url", "Url"),
                ("RequestTypeEnum", "RequestTypeEnum"), ("Description", "Description"), ("Body", "Body")
            ],
            transforms: new()
            {
                ["RequestTypeEnum"] = value => EnumName(value, "GET", "POST", "PUT", "PATCH", "DELETE")
            }),
        Custom("ApplicationApiQueryParams", "ApplicationApiQueryParams",
            [("Id", "Id"), ("ApplicationApiId", "ApplicationApiId"), ("Name", "Name"), ("ParamType", "ParamType")],
            transforms: new()
            {
                ["ParamType"] = value => EnumName(value, "Int", "String", "Bool", "DateTime", "Double", "Decimal")
            }),
        Plan("Projects", "Projects", ("Id", "Id"), ("Name", "Name"), ("Description", "Description")),
        Join("ApplicationProject", "ApplicationProject",
            ("ApplicationsId", "ApplicationsId"), ("ProjectsId", "ProjectsId")),
        Plan("CommandTemplates", "CommandTemplates",
            ("Id", "Id"), ("Name", "Name"), ("Description", "Description"), ("Script", "Script")),
        Custom("ApplicationActions", "ApplicationActions",
            [
                ("Id", "Id"), ("ApplicationId", "ApplicationId"), ("Label", "Label"), ("Order", "Order"),
                ("CommandTemplateId", "CommandTemplateId"), ("CustomScript", "CustomScript"),
                ("ParameterValuesJson", "ParameterValues"), ("RequiresConfirmation", "RequiresConfirmation")
            ],
            transforms: new()
            {
                ["ParameterValuesJson"] = value => value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "{}"
                    : value.GetRawText()
            }),
        Plan("ServerCredentials", "ServerCredentials",
            ("Id", "Id"), ("Name", "Name"), ("Username", "Username"), ("AuthType", "AuthType"),
            ("EncryptedSecret", "EncryptedSecret"), ("EncryptedPassphrase", "EncryptedPassphrase")),
        Plan("Servers", "Servers",
            ("Id", "Id"), ("ProviderEnvironmentId", "ProviderEnvironmentId"), ("Name", "Name"),
            ("IpAddress", "IpAddress"), ("Hostname", "Hostname"), ("CredentialId", "CredentialId"),
            ("SshPort", "SshPort"), ("HostKeyFingerprint", "HostKeyFingerprint")),
        Plan("ProxyConnections", "ProxyConnections",
            ("Id", "Id"), ("SourceServerId", "SourceServerId"), ("TargetServerId", "TargetServerId"),
            ("Direction", "Direction"), ("CreatedAt", "CreatedAt")),
        Custom("ServerApplications", "ServerApplications",
            [
                ("Id", "Id"), ("ServerId", "ServerId"),
                ("ApplicationVersionId", "ApplicationVersionId"), ("ExposedPort", "ExposedPort")
            ],
            deferred: [("ProxyRouteId", "ProxyRouteId")]),
        Plan("ProxyRoutes", "ProxyRoutes",
            ("Id", "Id"), ("ProxyConnectionId", "ProxyConnectionId"), ("Route", "Route"),
            ("MappedTo", "MappedTo"), ("TargetServerApplicationId", "TargetServerApplicationId"),
            ("TargetProxyConnectionId", "TargetProxyConnectionId")),
        Plan("ReachabilityTests", "ReachabilityTests",
            ("Id", "Id"), ("ServerId", "ServerId"), ("Hostname", "Hostname"),
            ("Port", "Port"), ("Description", "Description")),
        Plan("Diagrams", "Diagrams",
            ("Id", "Id"), ("Name", "Name"), ("Description", "Description"),
            ("IsDefault", "IsDefault"), ("CreatedAt", "CreatedAt")),
        Plan("DiagramScopeItems", "DiagramScopeItems",
            ("Id", "Id"), ("DiagramId", "DiagramId"), ("ScopeType", "ScopeType"), ("EntityId", "EntityId")),
        Plan("DiagramNodes", "DiagramNodes",
            ("Id", "Id"), ("DiagramId", "DiagramId"), ("NodeType", "NodeType"), ("EntityId", "EntityId"),
            ("X", "X"), ("Y", "Y"), ("Width", "Width"), ("Height", "Height"), ("Collapsed", "Collapsed")),
        Plan("DiagramEdges", "DiagramEdges",
            ("Id", "Id"), ("DiagramId", "DiagramId"), ("ProxyConnectionId", "ProxyConnectionId"),
            ("TargetNodeType", "TargetNodeType"), ("TargetEntityId", "TargetEntityId"),
            ("SourceHandle", "SourceHandle"), ("TargetHandle", "TargetHandle"),
            ("BendX", "BendX"), ("BendY", "BendY")),
        Custom("CommandExecutions", "CommandExecutions",
            [
                ("Id", "Id"), ("Username", "Username"), ("ServerId", "ServerId"),
                ("ServerName", "ServerName"), ("Kind", "Kind"), ("Label", "Label"),
                ("Script", "Script"), ("ExitCode", "ExitCode"), ("Output", "Output"),
                ("StartedAt", "StartedAt"), ("EndedAt", "EndedAt")
            ],
            audit: false, redact: true)
    ];

    private static TablePlan Plan(string legacy, string target, params (string Target, string Source)[] columns) =>
        Custom(legacy, target, columns);

    private static TablePlan Join(string legacy, string target, params (string Target, string Source)[] columns) =>
        Custom(
            legacy,
            target,
            columns,
            audit: false,
            keyColumns: columns.Select(x => x.Target).ToArray(),
            naturalKeys: columns.Select(x => x.Target).ToArray());

    private static TablePlan Custom(
        string legacy,
        string target,
        (string Target, string Source)[] columns,
        Dictionary<string, Func<JsonElement, object?>>? transforms = null,
        (string Target, string Source)[]? deferred = null,
        bool audit = true,
        bool redact = false,
        string[]? keyColumns = null,
        string[]? naturalKeys = null) => new()
    {
        Legacy = legacy,
        Target = target,
        Columns = columns.ToDictionary(x => x.Target, x => x.Source),
        Transforms = transforms ?? [],
        Deferred = deferred?.ToList() ?? [],
        Audit = audit,
        Redact = redact,
        KeyColumns = keyColumns ?? ["Id"],
        NaturalKey = naturalKeys ?? NaturalKeys.GetValueOrDefault(legacy, [])
    };

    private static string EnumName(JsonElement value, params string[] names)
    {
        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var index) &&
            index >= 0 && index < names.Length)
            return names[index];

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            var match = names.FirstOrDefault(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        throw new InvalidOperationException($"Valore enum non valido: {value.GetRawText()}");
    }
}
