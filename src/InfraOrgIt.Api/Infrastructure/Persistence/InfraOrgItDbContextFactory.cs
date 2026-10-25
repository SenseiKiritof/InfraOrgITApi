using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InfraOrgIt.Api.Infrastructure.Persistence;

public sealed class InfraOrgItDbContextFactory : IDesignTimeDbContextFactory<InfraOrgItDbContext>
{
    public InfraOrgItDbContext CreateDbContext(string[] args)
    {
        // Migrations add does not open a connection; database update uses the real env var.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MySql")
                               ?? "Server=localhost;Port=3306;Database=infraorgit;User Id=not-used;Password=not-used;";

        var options = new DbContextOptionsBuilder<InfraOrgItDbContext>()
            .UseMySQL(connectionString)
            .Options;

        return new InfraOrgItDbContext(options);
    }
}
