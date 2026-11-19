using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Infrastructure.Persistence;

/// <summary>
/// SQLite flavour used by the standalone spike. Keeping a derived context gives
/// the local database its own migration set while every feature keeps
/// injecting <see cref="InfraOrgItDbContext"/>.
/// </summary>
public sealed class SqliteInfraOrgItDbContext(
    DbContextOptions<SqliteInfraOrgItDbContext> options)
    : InfraOrgItDbContext(options);
