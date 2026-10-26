namespace InfraOrgIt.Api.Features.Admin;

public sealed record UserRefGroupDto(int Id, string Name);

public sealed record UserRefRoleDto(int Id, string Name, string Label, string ScopeEnum);

public sealed record UserDto(
    int Id,
    string Username,
    string Name,
    string Surname,
    List<UserRefGroupDto> Groups,
    List<UserRefRoleDto> Roles,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record UserInput(
    string? Username,
    string? Name,
    string? Surname,
    List<int>? GroupIds,
    List<int>? RoleIds);

public sealed record GroupDto(
    int Id,
    string Name,
    int UsersCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record GroupInput(string? Name);

public sealed record RoleDto(
    int Id,
    string Name,
    string Label,
    string ScopeEnum,
    int UsersCount,
    int MenusCount,
    int AssignmentsCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record RoleInput(string? Name, string? Label, string? ScopeEnum);

public sealed record MenuAdminDto(
    int Id,
    string Name,
    string Path,
    bool IsActive,
    bool HideBackButton,
    bool IsVisible,
    int? Order,
    int? ParentId,
    List<int> RoleIds,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record MenuInput(
    string? Name,
    string? Path,
    bool IsActive,
    bool HideBackButton,
    bool IsVisible,
    int? Order,
    int? ParentId,
    List<int>? RoleIds);

public sealed record AdminGroupRoleDto(
    int Id,
    int ProviderEnvironmentId,
    string ProviderEnvironmentLabel,
    int GroupId,
    string GroupName,
    int RoleId,
    string RoleName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int Version);

public sealed record AdminGroupRoleInput(int ProviderEnvironmentId, int GroupId, int RoleId);
