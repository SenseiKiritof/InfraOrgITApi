using InfraOrgIt.Api.Infrastructure.Persistence;
using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Features.Menus;

public sealed record MenuDto(
    int Id,
    string Name,
    string Path,
    bool IsActive,
    bool HideBackButton,
    bool IsVisible,
    int? Order,
    int? ParentId,
    List<MenuDto> Children);

public sealed class MenuService(InfraOrgItDbContext db)
{
    public async Task<List<MenuDto>> GetForRolesAsync(IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var menus = await db.Menus
            .AsNoTracking()
            .Include(x => x.Roles)
            .Where(x => x.IsActive && x.IsVisible)
            .ToListAsync(ct);

        var visible = menus
            .Where(x => x.Roles.Count == 0 ||
                        x.Roles.Any(role => roles.Contains(role.Name, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        var byParent = visible.ToLookup(x => x.ParentId);
        return BuildTree(null, byParent);
    }

    private static List<MenuDto> BuildTree(int? parentId, ILookup<int?, Menu> byParent) =>
        byParent[parentId]
            .OrderBy(x => x.Order ?? int.MaxValue)
            .ThenBy(x => x.Id)
            .Select(x => new MenuDto(
                x.Id,
                x.Name,
                x.Path,
                x.IsActive,
                x.HideBackButton,
                x.IsVisible,
                x.Order,
                x.ParentId,
                BuildTree(x.Id, byParent)))
            .ToList();
}
