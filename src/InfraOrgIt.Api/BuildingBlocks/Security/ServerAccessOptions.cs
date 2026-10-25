namespace InfraOrgIt.Api.BuildingBlocks.Security;

public sealed class ServerAccessOptions
{
    public const string SectionName = "ServerAccess";

    public int CurrentKeyVersion { get; set; } = 1;
    public Dictionary<string, string> CredentialKeys { get; set; } = [];
    public int CommandTimeoutMinutes { get; set; } = 10;
    public int MaxOutputBytes { get; set; } = 1_048_576;
    public int MaxAuditOutputBytes { get; set; } = 65_536;
    public int ConsoleMaxMinutes { get; set; } = 30;
}
