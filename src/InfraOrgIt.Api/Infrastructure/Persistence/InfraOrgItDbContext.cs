using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Infrastructure.Persistence;

public sealed class InfraOrgItDbContext(DbContextOptions<InfraOrgItDbContext> options) : DbContext(options)
{
    private static readonly DateTime SeedDate = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    public DbSet<User> Users => Set<User>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<GroupUser> GroupUsers => Set<GroupUser>();
    public DbSet<RoleUser> RoleUsers => Set<RoleUser>();
    public DbSet<MenuRole> MenuRoles => Set<MenuRole>();
    public DbSet<ProviderEnvironmentGroupRole> ProviderEnvironmentGroupRoles => Set<ProviderEnvironmentGroupRole>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<EnvironmentDefinition> EnvironmentDefinitions => Set<EnvironmentDefinition>();
    public DbSet<ProviderEnvironment> ProviderEnvironments => Set<ProviderEnvironment>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<ServerApplication> ServerApplications => Set<ServerApplication>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationVersion> ApplicationVersions => Set<ApplicationVersion>();
    public DbSet<ApplicationVersionApi> ApplicationVersionApis => Set<ApplicationVersionApi>();
    public DbSet<ApplicationApiQueryParam> ApplicationApiQueryParams => Set<ApplicationApiQueryParam>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProxyConnection> ProxyConnections => Set<ProxyConnection>();
    public DbSet<ProxyRoute> ProxyRoutes => Set<ProxyRoute>();
    public DbSet<Diagram> Diagrams => Set<Diagram>();
    public DbSet<DiagramScopeItem> DiagramScopeItems => Set<DiagramScopeItem>();
    public DbSet<DiagramNode> DiagramNodes => Set<DiagramNode>();
    public DbSet<DiagramEdge> DiagramEdges => Set<DiagramEdge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Surname).HasMaxLength(100);
            e.Property(x => x.Username).HasMaxLength(100);
            e.HasIndex(x => x.Username).IsUnique();
            e.HasMany(x => x.Groups).WithMany(x => x.Users)
                .UsingEntity<GroupUser>(
                    j => j.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupsId),
                    j => j.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UsersId),
                    j => { j.ToTable("GroupUser"); j.HasKey(x => new { x.GroupsId, x.UsersId }); });
            e.HasMany(x => x.Roles).WithMany(x => x.Users)
                .UsingEntity<RoleUser>(
                    j => j.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RolesId),
                    j => j.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UsersId),
                    j => { j.ToTable("RoleUser"); j.HasKey(x => new { x.RolesId, x.UsersId }); });
        });

        modelBuilder.Entity<Group>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.ScopeEnum).HasMaxLength(20);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Menu>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Path).HasMaxLength(200);
            e.HasIndex(x => x.ParentId);
            e.HasOne(x => x.Parent).WithMany(x => x.Childrens)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Roles).WithMany()
                .UsingEntity<MenuRole>(
                    j => j.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RolesId),
                    j => j.HasOne(x => x.Menu).WithMany().HasForeignKey(x => x.MenuId),
                    j => { j.ToTable("MenuRole"); j.HasKey(x => new { x.MenuId, x.RolesId }); });
        });

        modelBuilder.Entity<Provider>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<EnvironmentDefinition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(250);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<ProviderEnvironment>(e =>
        {
            e.HasIndex(x => new { x.ProviderId, x.EnvironmentDefinitionId }).IsUnique();
            e.HasOne(x => x.Provider).WithMany(x => x.ProviderEnvironments)
                .HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EnvironmentDefinition).WithMany(x => x.ProviderEnvironments)
                .HasForeignKey(x => x.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Label);
        });

        modelBuilder.Entity<ProviderEnvironmentGroupRole>(e =>
        {
            e.HasIndex(x => new { x.ProviderEnvironmentId, x.GroupId, x.RoleId }).IsUnique();
            e.HasOne(x => x.ProviderEnvironment).WithMany(x => x.GroupRoles)
                .HasForeignKey(x => x.ProviderEnvironmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany()
                .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany()
                .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Server>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.IpAddress).HasMaxLength(200);
            e.Property(x => x.Hostname).HasMaxLength(200);
            e.Property(x => x.HostKeyFingerprint).HasMaxLength(100);
            e.Property(x => x.SshPort).HasDefaultValue(22);
            e.HasIndex(x => x.ProviderEnvironmentId);
            e.HasOne(x => x.ProviderEnvironment).WithMany(x => x.Servers)
                .HasForeignKey(x => x.ProviderEnvironmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerApplication>(e =>
        {
            e.HasIndex(x => x.ServerId);
            e.HasIndex(x => x.ApplicationVersionId);
            e.HasOne(x => x.Server).WithMany(x => x.ServerApplications)
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApplicationVersion).WithMany(x => x.ServerApplications)
                .HasForeignKey(x => x.ApplicationVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProxyRouteId).IsUnique();
            e.HasOne(x => x.ProxyRoute).WithMany()
                .HasForeignKey(x => x.ProxyRouteId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Application>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.BasePath).HasMaxLength(50);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Projects).WithMany(x => x.Applications)
                .UsingEntity<ApplicationProject>(
                    j => j.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectsId),
                    j => j.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationsId),
                    j => { j.ToTable("ApplicationProject"); j.HasKey(x => new { x.ApplicationsId, x.ProjectsId }); });
        });

        modelBuilder.Entity<ApplicationVersion>(e =>
        {
            e.Property(x => x.VersionNumber).HasMaxLength(25);
            e.HasIndex(x => new { x.ApplicationId, x.VersionNumber }).IsUnique();
            e.HasOne(x => x.Application).WithMany(x => x.Versions)
                .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationVersionApi>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(200);
            e.Property(x => x.RequestTypeEnum).HasMaxLength(10);
            e.Property(x => x.Description).HasMaxLength(200);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_ApplicationVersionApis_RequestTypeEnum",
                "`RequestTypeEnum` IN ('GET','POST','PUT','PATCH','DELETE')"));
            e.HasOne(x => x.ApplicationVersion).WithMany(x => x.Apis)
                .HasForeignKey(x => x.ApplicationVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationApiQueryParam>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.ParamType).HasMaxLength(20);
            e.ToTable(t => t.HasCheckConstraint(
                "CK_ApplicationApiQueryParams_ParamType",
                "`ParamType` IN ('Int','String','Bool','DateTime','Double','Decimal')"));
            e.HasOne(x => x.ApplicationApi).WithMany(x => x.QueryParams)
                .HasForeignKey(x => x.ApplicationApiId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<ProxyConnection>(e =>
        {
            e.Property(x => x.Direction).HasMaxLength(20);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ProxyConnections_Direction",
                    "`Direction` IN ('Outgoing','Incoming')");
                t.HasCheckConstraint("CK_ProxyConnections_DifferentServers",
                    "`SourceServerId` <> `TargetServerId`");
            });
            e.HasIndex(x => x.SourceServerId);
            e.HasIndex(x => x.TargetServerId);
            e.HasOne(x => x.SourceServer).WithMany()
                .HasForeignKey(x => x.SourceServerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetServer).WithMany()
                .HasForeignKey(x => x.TargetServerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProxyRoute>(e =>
        {
            e.Property(x => x.Route).HasMaxLength(200);
            e.Property(x => x.MappedTo).HasMaxLength(50);
            e.HasIndex(x => new { x.ProxyConnectionId, x.Route }).IsUnique();
            e.HasIndex(x => x.TargetServerApplicationId);
            e.HasIndex(x => x.TargetProxyConnectionId);
            e.HasOne(x => x.ProxyConnection).WithMany(x => x.Routes)
                .HasForeignKey(x => x.ProxyConnectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetServerApplication).WithMany()
                .HasForeignKey(x => x.TargetServerApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TargetProxyConnection).WithMany()
                .HasForeignKey(x => x.TargetProxyConnectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Diagram>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<DiagramScopeItem>(e =>
        {
            e.Property(x => x.ScopeType).HasMaxLength(30);
            e.ToTable(t => t.HasCheckConstraint("CK_DiagramScopeItems_ScopeType",
                "`ScopeType` IN ('Provider','ProviderEnvironment','Project')"));
            e.HasIndex(x => new { x.DiagramId, x.ScopeType, x.EntityId }).IsUnique();
            e.HasOne(x => x.Diagram).WithMany(x => x.ScopeItems)
                .HasForeignKey(x => x.DiagramId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiagramNode>(e =>
        {
            e.Property(x => x.NodeType).HasMaxLength(30);
            e.ToTable(t => t.HasCheckConstraint("CK_DiagramNodes_NodeType",
                "`NodeType` IN ('Provider','ProviderEnvironment','Server','ServerApplication')"));
            e.HasIndex(x => new { x.DiagramId, x.NodeType, x.EntityId }).IsUnique();
            e.HasOne(x => x.Diagram).WithMany(x => x.Nodes)
                .HasForeignKey(x => x.DiagramId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiagramEdge>(e =>
        {
            e.Property(x => x.TargetNodeType).HasMaxLength(30);
            e.Property(x => x.SourceHandle).HasMaxLength(50);
            e.Property(x => x.TargetHandle).HasMaxLength(50);
            e.ToTable(t => t.HasCheckConstraint("CK_DiagramEdges_TargetNodeType",
                "`TargetNodeType` IN ('Provider','ProviderEnvironment','Server','ServerApplication')"));
            e.HasIndex(x => new { x.DiagramId, x.ProxyConnectionId, x.TargetNodeType, x.TargetEntityId }).IsUnique();
            e.HasOne(x => x.Diagram).WithMany(x => x.Edges)
                .HasForeignKey(x => x.DiagramId).OnDelete(DeleteBehavior.Cascade);
        });

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(EntityBase).IsAssignableFrom(t.ClrType)))
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.Property(nameof(EntityBase.CreatedAt)).HasColumnType("datetime(6)");
            entity.Property(nameof(EntityBase.UpdatedAt)).HasColumnType("datetime(6)");
            entity.Property(nameof(EntityBase.Version)).IsConcurrencyToken();
        }

        Seed(modelBuilder);
    }

    private static void Seed(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, Name = "ServerAdmin", Label = "Amministratore server", ScopeEnum = "Environment", CreatedAt = SeedDate, UpdatedAt = SeedDate },
            new Role { Id = 2, Name = "ServerOperator", Label = "Operatore server", ScopeEnum = "Environment", CreatedAt = SeedDate, UpdatedAt = SeedDate },
            new Role { Id = 3, Name = "SystemAdmin", Label = "Amministratore di sistema", ScopeEnum = "System", CreatedAt = SeedDate, UpdatedAt = SeedDate });

        modelBuilder.Entity<Menu>().HasData(
            Menu(1, "Home", "/", true, true, true, 0, null),
            Menu(2, "Ambienti", "/environments", true, true, true, 1, null),
            Menu(3, "Applicazioni", "/applications", true, true, true, 1, null),
            Menu(4, "Providers", "/providers", true, true, true, 1, null),
            Menu(5, "Servers", "/servers", true, true, true, 1, null),
            Menu(6, "Ambiente {name}", "/environments/{id}", true, false, false, 0, 2),
            Menu(7, "Applicazione {name}", "/applications/{id}", true, false, false, 0, 3),
            Menu(8, "Server {name}", "/servers/{id}", true, false, false, 0, 5),
            Menu(9, "Provider {name}", "/providers/{id}", true, false, false, 0, 4),
            Menu(10, "Diagrammi", "/diagrams", true, true, true, 10, null),
            Menu(12, "Progetti", "/projects", true, true, true, 30, null),
            Menu(13, "Users", "/users", true, true, true, 40, null),
            Menu(14, "Groups", "/groups", true, true, true, 50, null),
            Menu(15, "Roles", "/roles", true, true, true, 60, null),
            Menu(16, "Menu", "/menu", true, true, true, 70, null),
            Menu(18, "Progetto {name}", "/projects/{id}", true, false, false, 0, 12),
            Menu(19, "Credenziali", "/credentials", true, true, true, 80, null),
            Menu(20, "Comandi", "/commands", true, true, true, 81, null),
            Menu(21, "Test di rete", "/reachability", true, true, true, 82, null),
            Menu(22, "Registro esecuzioni", "/executions", true, true, true, 83, null));

        var systemAdminMenus = new[] { 13, 14, 15, 16, 19, 20, 21, 22 };
        modelBuilder.Entity<MenuRole>().HasData(
            systemAdminMenus.Select(menuId => new MenuRole { MenuId = menuId, RolesId = 3 }).ToArray());
    }

    private static Menu Menu(int id, string name, string path, bool isActive, bool hideBackButton, bool isVisible,
        int? order, int? parentId) => new()
    {
        Id = id,
        Name = name,
        Path = path,
        IsActive = isActive,
        HideBackButton = hideBackButton,
        IsVisible = isVisible,
        Order = order,
        ParentId = parentId,
        CreatedAt = SeedDate,
        UpdatedAt = SeedDate
    };

    public override int SaveChanges()
    {
        ApplyAudit();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAudit()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                    entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version++;
            }
        }
    }
}
