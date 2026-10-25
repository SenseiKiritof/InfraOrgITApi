namespace InfraOrgIt.Api.BuildingBlocks.Auth;

public sealed record SessionData(
    string Username,
    string Name,
    string Surname,
    string? RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset? RefreshExpiresAt,
    string CsrfToken,
    IReadOnlyList<string> Roles);
