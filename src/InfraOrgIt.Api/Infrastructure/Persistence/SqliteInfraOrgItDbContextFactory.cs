using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InfraOrgIt.Api.Infrastructure.Persistence;

public sealed class SqliteInfraOrgItDbContextFactory
    : IDesignTimeDbContextFactory<SqliteInfraOrgItDbContext>
{
    public SqliteInfraOrgItDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite")
            ?? "Data Source=/tmp/infraorgit-spike.db";
        var options = new DbContextOptionsBuilder<SqliteInfraOrgItDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new SqliteInfraOrgItDbContext(options);
    }
}
