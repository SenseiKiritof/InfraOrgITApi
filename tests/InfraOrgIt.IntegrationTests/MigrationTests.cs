using InfraOrgIt.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.IntegrationTests;

/// <summary>
/// The suite clones a pre-migrated template; this test keeps the real
/// migrations covered on an empty database.
/// </summary>
public sealed class MigrationTests
{
    [Fact]
    public async Task Migrations_apply_to_an_empty_database()
    {
        var connectionString = await TestMySql.CreateEmptyDatabaseAsync("Migrations");
        var options = new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(connectionString)
            .Options;
        await using var db = new InfraOrgItDbContext(options);
        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Roles.AsNoTracking().ToListAsync());
        Assert.NotEmpty(await db.Menus.AsNoTracking().ToListAsync());
    }
}
