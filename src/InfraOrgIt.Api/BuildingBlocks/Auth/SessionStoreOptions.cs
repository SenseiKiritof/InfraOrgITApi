namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed class SessionStoreOptions
{
    public const string SectionName = "SessionStore";

    public int MaxSessionsPerUser { get; set; } = 10;
}
