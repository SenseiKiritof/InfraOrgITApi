using InfraOrgIt.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace InfraOrgIt.Api.Infrastructure.Persistence;

public class InfraOrgItDbContext(DbContextOptions options) : DbContext(options)
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
    public DbSet<HaproxyInstance> HaproxyInstances => Set<HaproxyInstance>();
    public DbSet<HaproxyRouteSnapshot> HaproxyRouteSnapshots => Set<HaproxyRouteSnapshot>();
    public DbSet<HaproxyServerSnapshot> HaproxyServerSnapshots => Set<HaproxyServerSnapshot>();
    public DbSet<HaproxyCertificateSnapshot> HaproxyCertificateSnapshots => Set<HaproxyCertificateSnapshot>();
    public DbSet<HaproxyMetricSample> HaproxyMetricSamples => Set<HaproxyMetricSample>();
    public DbSet<HaproxyProcessMetricSample> HaproxyProcessMetricSamples => Set<HaproxyProcessMetricSample>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationType> ApplicationTypes => Set<ApplicationType>();
    public DbSet<ApplicationVersion> ApplicationVersions => Set<ApplicationVersion>();
    public DbSet<ApplicationVersionApi> ApplicationVersionApis => Set<ApplicationVersionApi>();
    public DbSet<ApplicationApiQueryParam> ApplicationApiQueryParams => Set<ApplicationApiQueryParam>();
    public DbSet<ApplicationApiKey> ApplicationApiKeys => Set<ApplicationApiKey>();
    public DbSet<ApplicationEnvironment> ApplicationEnvironments => Set<ApplicationEnvironment>();
    public DbSet<ApplicationApiCall> ApplicationApiCalls => Set<ApplicationApiCall>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProxyConnection> ProxyConnections => Set<ProxyConnection>();
    public DbSet<ProxyRoute> ProxyRoutes => Set<ProxyRoute>();
    public DbSet<Diagram> Diagrams => Set<Diagram>();
    public DbSet<DiagramScopeItem> DiagramScopeItems => Set<DiagramScopeItem>();
    public DbSet<DiagramNode> DiagramNodes => Set<DiagramNode>();
    public DbSet<DiagramEdge> DiagramEdges => Set<DiagramEdge>();
    public DbSet<ServerCredential> ServerCredentials => Set<ServerCredential>();
    public DbSet<CommandTemplate> CommandTemplates => Set<CommandTemplate>();
    public DbSet<ApplicationAction> ApplicationActions => Set<ApplicationAction>();
    public DbSet<ReachabilityTest> ReachabilityTests => Set<ReachabilityTest>();
    public DbSet<CommandExecution> CommandExecutions => Set<CommandExecution>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<DownloadTicket> DownloadTickets => Set<DownloadTicket>();
    public DbSet<ClientEvent> ClientEvents => Set<ClientEvent>();
    public DbSet<DesktopDevice> DesktopDevices => Set<DesktopDevice>();
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<McpGrant> McpGrants => Set<McpGrant>();
    public DbSet<McpAgent> McpAgents => Set<McpAgent>();
    public DbSet<McpAgentPermission> McpAgentPermissions => Set<McpAgentPermission>();
    public DbSet<McpPermissionRequest> McpPermissionRequests => Set<McpPermissionRequest>();
    public DbSet<TunnelSession> TunnelSessions => Set<TunnelSession>();
    public DbSet<TunnelService> TunnelServices => Set<TunnelService>();
    public DbSet<ConnectionPolicy> ConnectionPolicies => Set<ConnectionPolicy>();
    public DbSet<ServerUpdatePolicy> ServerUpdatePolicies => Set<ServerUpdatePolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var sqlite = Database.ProviderName?.Contains(
            "Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Surname).HasMaxLength(100);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.Theme).HasMaxLength(10).HasDefaultValue("system");
            e.HasIndex(x => x.Username).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Users_Theme",
                "`Theme` IN ('light','dark','system')"));
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
            e.Property(x => x.PublicIpAddress).HasMaxLength(45);
            e.Property(x => x.OperatingSystem).HasMaxLength(100);
            e.Property(x => x.OsName).HasMaxLength(100);
            e.Property(x => x.OsVersion).HasMaxLength(50);
            e.Property(x => x.OsUpdateTarget).HasMaxLength(50);
            e.Property(x => x.OsUpdateStatus).HasMaxLength(20);
            e.Property(x => x.OsUpdateDetail).HasMaxLength(300);
            e.Property(x => x.HaNode).HasMaxLength(20);
            e.Property(x => x.ManagedBy).HasMaxLength(100);
            e.Property(x => x.SshPort).HasDefaultValue(22);
            e.Property(x => x.FileTransferProtocol).HasMaxLength(10);
            e.HasIndex(x => x.ProviderEnvironmentId);
            e.HasIndex(x => x.CredentialId);
            e.HasIndex(x => x.FileTransferCredentialId);
            e.HasIndex(x => x.JumpServerId);
            e.ToTable(t => t.HasCheckConstraint("CK_Servers_FileTransferProtocol",
                "`FileTransferProtocol` IS NULL OR `FileTransferProtocol` IN ('Sftp','Ftp','Ftps')"));
            e.HasOne(x => x.ProviderEnvironment).WithMany(x => x.Servers)
                .HasForeignKey(x => x.ProviderEnvironmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Credential).WithMany(x => x.Servers)
                .HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FileTransferCredential).WithMany()
                .HasForeignKey(x => x.FileTransferCredentialId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JumpServer).WithMany()
                .HasForeignKey(x => x.JumpServerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServerApplication>(e =>
        {
            e.Property(x => x.Runtime).HasMaxLength(20);
            e.Property(x => x.ContainerImage).HasMaxLength(300);
            e.Property(x => x.ContainerName).HasMaxLength(200);
            e.Property(x => x.ComposeDirectory).HasMaxLength(300);
            e.Property(x => x.HaRole).HasMaxLength(20);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ServerApplications_Runtime",
                    "`Runtime` IN ('Container','Native')");
                t.HasCheckConstraint("CK_ServerApplications_HaRole",
                    "`HaRole` IN ('Primary','Secondary')");
            });
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

        modelBuilder.Entity<HaproxyInstance>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.BaseUrl).HasMaxLength(300);
            e.Property(x => x.Transport).HasMaxLength(20);
            e.Property(x => x.ApiHost).HasMaxLength(200);
            e.Property(x => x.ApiVersion).HasMaxLength(10);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.StatsUsername).HasMaxLength(100);
            e.Property(x => x.MonitoringChoice).HasMaxLength(10);
            e.Property(x => x.MonitoringChangedBy).HasMaxLength(100);
            e.Property(x => x.DiscoverySource).HasMaxLength(10);
            e.Property(x => x.DiscoveryStatus).HasMaxLength(20);
            e.Property(x => x.DetectedVersion).HasMaxLength(200);
            e.Property(x => x.DetectedConfigPath).HasMaxLength(300);
            e.Property(x => x.EffectiveApiVersion).HasMaxLength(10);
            e.Property(x => x.LastRefreshStatus).HasMaxLength(20);
            e.Property(x => x.LastRefreshDetail).HasMaxLength(500);
            e.Property(x => x.LastTestStatus).HasMaxLength(20);
            e.Property(x => x.LastTestDetail).HasMaxLength(500);
            e.HasIndex(x => x.ServerId).IsUnique();
            e.HasIndex(x => x.ServerApplicationId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_HaproxyInstances_Transport",
                    "`Transport` IN ('Direct','SshForward','DesktopRelay')");
                t.HasCheckConstraint("CK_HaproxyInstances_MonitoringChoice",
                    "`MonitoringChoice` IN ('Undecided','Enabled','Disabled')");
                t.HasCheckConstraint("CK_HaproxyInstances_DiscoverySource",
                    "`DiscoverySource` IN ('Auto','Manual')");
                t.HasCheckConstraint("CK_HaproxyInstances_DiscoveryStatus",
                    "`DiscoveryStatus` IN ('Found','Inactive','CredentialsRequired','Unreachable','Ready')");
                t.HasCheckConstraint("CK_HaproxyInstances_ApiVersion",
                    "`ApiVersion` IN ('auto','v2','v3','stats')");
            });
            e.HasOne(x => x.Server).WithMany()
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ServerApplication).WithMany()
                .HasForeignKey(x => x.ServerApplicationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HaproxyRouteSnapshot>(e =>
        {
            e.Property(x => x.Frontend).HasMaxLength(100);
            e.Property(x => x.Bind).HasMaxLength(200);
            e.Property(x => x.Host).HasMaxLength(200);
            e.Property(x => x.Path).HasMaxLength(255);
            e.Property(x => x.Backend).HasMaxLength(100);
            e.Property(x => x.RawCondition).HasMaxLength(500);
            e.HasIndex(x => new { x.HaproxyInstanceId, x.Frontend, x.Host, x.Path, x.Backend }).IsUnique();
            e.HasOne(x => x.HaproxyInstance).WithMany(x => x.Routes)
                .HasForeignKey(x => x.HaproxyInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HaproxyServerSnapshot>(e =>
        {
            e.Property(x => x.Backend).HasMaxLength(100);
            e.Property(x => x.Server).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.CheckStatus).HasMaxLength(50);
            e.HasIndex(x => new { x.HaproxyInstanceId, x.Backend, x.Server }).IsUnique();
            e.HasOne(x => x.HaproxyInstance).WithMany(x => x.Servers)
                .HasForeignKey(x => x.HaproxyInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HaproxyCertificateSnapshot>(e =>
        {
            e.Property(x => x.StorageName).HasMaxLength(200);
            e.Property(x => x.Path).HasMaxLength(500);
            e.Property(x => x.Domains).HasMaxLength(1000);
            e.Property(x => x.Issuer).HasMaxLength(500);
            e.HasIndex(x => new { x.HaproxyInstanceId, x.StorageName }).IsUnique();
            e.HasOne(x => x.HaproxyInstance).WithMany(x => x.Certificates)
                .HasForeignKey(x => x.HaproxyInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HaproxyMetricSample>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Proxy).HasMaxLength(100);
            e.Property(x => x.Server).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.CheckStatus).HasMaxLength(50);
            e.HasIndex(x => new
            {
                x.HaproxyInstanceId,
                x.ResolutionMinutes,
                x.Bucket,
                x.Kind,
                x.Proxy,
                x.Server
            }).IsUnique();
            e.HasIndex(x => new { x.HaproxyInstanceId, x.Bucket });
            e.HasOne(x => x.HaproxyInstance).WithMany()
                .HasForeignKey(x => x.HaproxyInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HaproxyProcessMetricSample>(e =>
        {
            e.HasIndex(x => new
            {
                x.HaproxyInstanceId,
                x.ResolutionMinutes,
                x.Bucket
            }).IsUnique();
            e.HasOne(x => x.HaproxyInstance).WithMany()
                .HasForeignKey(x => x.HaproxyInstanceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationType>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(20);
            e.Property(x => x.Label).HasMaxLength(50);
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Application>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.BasePath).HasMaxLength(50);
            e.Property(x => x.Type).HasMaxLength(20);
            e.HasOne<ApplicationType>().WithMany(x => x.Applications)
                .HasForeignKey(x => x.Type)
                .HasPrincipalKey(x => x.Name)
                .OnDelete(DeleteBehavior.Restrict);
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

        modelBuilder.Entity<ApplicationApiKey>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Location).HasMaxLength(10);
            e.Property(x => x.EncryptedValue).HasMaxLength(1000);
            e.HasIndex(x => new { x.ApplicationId, x.EnvironmentDefinitionId, x.Name, x.Location }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint(
                "CK_ApplicationApiKeys_Location",
                "`Location` IN ('Header','Query')"));
            e.HasOne(x => x.Application).WithMany(x => x.ApiKeys)
                .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EnvironmentDefinition).WithMany()
                .HasForeignKey(x => x.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationEnvironment>(e =>
        {
            e.Property(x => x.Hostname).HasMaxLength(300);
            e.HasIndex(x => new { x.ApplicationId, x.EnvironmentDefinitionId }).IsUnique();
            e.HasOne(x => x.Application).WithMany(x => x.Environments)
                .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EnvironmentDefinition).WithMany()
                .HasForeignKey(x => x.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApplicationApiCall>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.Method).HasMaxLength(10);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.Property(x => x.QueryJson).HasColumnType("longtext");
            e.Property(x => x.RequestBody).HasColumnType("longtext");
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Property(x => x.ResponseBody).HasColumnType("longtext");
            e.HasIndex(x => new { x.Username, x.CreatedAt });
            e.HasIndex(x => x.ApplicationVersionApiId);
            e.HasOne(x => x.ApplicationVersionApi).WithMany()
                .HasForeignKey(x => x.ApplicationVersionApiId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.EnvironmentDefinition).WithMany()
                .HasForeignKey(x => x.EnvironmentDefinitionId).OnDelete(DeleteBehavior.Restrict);
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
            e.Property(x => x.VirtualHost).HasMaxLength(200);
            e.Property(x => x.MappedTo).HasMaxLength(50);
            e.HasIndex(x => new { x.ProxyConnectionId, x.VirtualHost, x.ListenPort, x.Route }).IsUnique();
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

        modelBuilder.Entity<ServerCredential>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Username).HasMaxLength(200);
            e.Property(x => x.AuthType).HasMaxLength(20);
            e.Property(x => x.EncryptedSecret).HasColumnType("longtext");
            e.Property(x => x.EncryptedPassphrase).HasColumnType("longtext");
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.OwnerUserId);
            e.ToTable(t => t.HasCheckConstraint("CK_ServerCredentials_AuthType",
                "`AuthType` IN ('Password','PrivateKey')"));
            e.HasOne(x => x.Owner).WithMany()
                .HasForeignKey(x => x.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DesktopDevice>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.PublicKeyPem).HasColumnType("longtext");
            e.Property(x => x.LastSeenAt).HasColumnType("datetime(6)");
            e.Property(x => x.RevokedAt).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.Fingerprint }).IsUnique();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommandTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Script).HasColumnType("longtext");
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<ApplicationAction>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(100);
            e.Property(x => x.CustomScript).HasColumnType("longtext");
            e.Property(x => x.ParameterValuesJson).HasColumnType("json");
            e.HasIndex(x => x.Order);
            e.HasOne(x => x.Application).WithMany()
                .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CommandTemplate).WithMany()
                .HasForeignKey(x => x.CommandTemplateId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReachabilityTest>(e =>
        {
            e.Property(x => x.Hostname).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasIndex(x => x.ServerId);
            e.HasOne(x => x.Server).WithMany(x => x.ReachabilityTests)
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_ReachabilityTests_Port",
                "`Port` BETWEEN 1 AND 65535"));
        });

        modelBuilder.Entity<CommandExecution>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.ServerName).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Script).HasColumnType("longtext");
            e.Property(x => x.Output).HasColumnType("longtext");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.HasIndex(x => x.ServerId);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_CommandExecutions_Kind",
                "`Kind` IN ('Action','Reachability','Console','Query')"));
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.ActorUsername).HasMaxLength(100);
            e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.TargetType).HasMaxLength(30);
            e.Property(x => x.TargetHost).HasMaxLength(300);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.CorrelationId).HasMaxLength(100);
            e.Property(x => x.DetailJson).HasColumnType("longtext");
            e.Property(x => x.PrevHash).HasMaxLength(64);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.Kind, x.OccurredAt });
            e.HasIndex(x => new { x.ActorUsername, x.OccurredAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_AuditEvents_Kind",
                    "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError','ClientCheck','CredentialDownload','FileTransfer','AdminChange')");
                t.HasCheckConstraint("CK_AuditEvents_Outcome",
                    "`Outcome` IN ('Ok','Error','Denied')");
                t.HasCheckConstraint("CK_AuditEvents_Hash",
                    $"`Hash` IS NULL OR {(sqlite ? "length" : "CHAR_LENGTH")}(`Hash`) = 64");
            });
        });

        modelBuilder.Entity<ApiToken>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Scopes).HasMaxLength(500);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<McpGrant>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(50);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => new { x.Action, x.EnvironmentDefinitionId });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_McpGrants_Subject",
                    "((`UserId` IS NULL) + (`GroupId` IS NULL)) = 1");
                t.HasCheckConstraint("CK_McpGrants_Action",
                    "`Action` IN ('reachability.run','actions.run','*')");
            });
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany()
                .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EnvironmentDefinition).WithMany()
                .HasForeignKey(x => x.EnvironmentDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<McpAgent>(e =>
        {
            e.Property(x => x.ClientKey).HasMaxLength(200);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.ClientKey }).IsUnique();
            e.HasIndex(x => x.Status);
            e.ToTable(t => t.HasCheckConstraint("CK_McpAgents_Status",
                "`Status` IN ('Pending','Approved','Denied')"));
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ApprovedByUser).WithMany()
                .HasForeignKey(x => x.ApprovedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<McpAgentPermission>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(50);
            e.HasIndex(x => x.AgentId);
            e.HasIndex(x => new { x.Action, x.EnvironmentDefinitionId });
            e.ToTable(t => t.HasCheckConstraint("CK_McpAgentPermissions_Action",
                "`Action` IN ('reachability.run','actions.run','*')"));
            e.HasOne(x => x.Agent).WithMany(x => x.Permissions)
                .HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EnvironmentDefinition).WithMany()
                .HasForeignKey(x => x.EnvironmentDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<McpPermissionRequest>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => new { x.AgentId, x.Status });
            e.ToTable(t => t.HasCheckConstraint("CK_McpPermissionRequests_Status",
                "`Status` IN ('Pending','Approved','Denied')"));
            e.HasOne(x => x.Agent).WithMany(x => x.PermissionRequests)
                .HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DecidedByUser).WithMany()
                .HasForeignKey(x => x.DecidedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.EnvironmentDefinition).WithMany()
                .HasForeignKey(x => x.EnvironmentDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Translation>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(50);
            e.Property(x => x.Field).HasMaxLength(50);
            e.Property(x => x.Language).HasMaxLength(10);
            e.Property(x => x.Value).HasMaxLength(200);
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.Field, x.Language }).IsUnique();
        });

        modelBuilder.Entity<TunnelSession>(e =>
        {
            e.Property(x => x.SessionKey).HasMaxLength(100);
            e.Property(x => x.ActorUsername).HasMaxLength(100);
            e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.Mode).HasMaxLength(10).HasDefaultValue("Local");
            e.Property(x => x.Transport).HasMaxLength(10).HasDefaultValue("Client");
            e.Property(x => x.TargetHost).HasMaxLength(255);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.StartedAt).HasColumnType("datetime(6)");
            e.Property(x => x.LastSeenAt).HasColumnType("datetime(6)");
            e.Property(x => x.EndedAt).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.SessionKey }).IsUnique();
            e.HasIndex(x => new { x.Outcome, x.LastSeenAt });
            e.HasIndex(x => x.ServerId);
            e.HasIndex(x => x.ServerApplicationId);
            e.HasIndex(x => x.TunnelServiceId);
            e.HasIndex(x => x.ActorUsername);
            e.HasOne(x => x.TunnelService).WithMany()
                .HasForeignKey(x => x.TunnelServiceId).OnDelete(DeleteBehavior.SetNull);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TunnelSessions_Outcome",
                    "`Outcome` IN ('Active','Stopped','Error','Stale')");
                t.HasCheckConstraint("CK_TunnelSessions_Mode",
                    "`Mode` IN ('Local','Dynamic','Remote')");
                t.HasCheckConstraint("CK_TunnelSessions_Transport",
                    "`Transport` IN ('Client','Server')");
            });
        });

        modelBuilder.Entity<ConnectionPolicy>(e =>
        {
            e.Property(x => x.Mode).HasMaxLength(20).HasDefaultValue("Auto");
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
            e.ToTable(t => t.HasCheckConstraint("CK_ConnectionPolicies_Mode",
                "`Mode` IN ('Server','Client','Auto')"));
        });

        modelBuilder.Entity<TunnelService>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Mode).HasMaxLength(10);
            e.Property(x => x.TargetHost).HasMaxLength(255).HasDefaultValue("127.0.0.1");
            e.Property(x => x.Kind).HasMaxLength(10).HasDefaultValue("Tcp");
            e.Property(x => x.OpenPath).HasMaxLength(200);
            e.HasIndex(x => new { x.ServerId, x.Name }).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_TunnelServices_Mode",
                    "`Mode` IN ('Local','Dynamic','Remote')");
                t.HasCheckConstraint("CK_TunnelServices_Kind",
                    "`Kind` IN ('Tcp','Http','Https')");
            });
            e.HasOne(x => x.Server).WithMany(x => x.TunnelServices)
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.Payload).HasColumnType("longtext");
            e.Property(x => x.ExpiresAt).HasColumnType("datetime(6)");
            e.Property(x => x.CreatedAt).HasColumnType("datetime(6)");
            e.HasIndex(x => x.Username);
            e.HasIndex(x => x.ExpiresAt);
        });

        modelBuilder.Entity<ClientEvent>(e =>
        {
            e.Property(x => x.EventId).HasMaxLength(100);
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.Kind).HasMaxLength(50);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.TargetHost).HasMaxLength(255);
            e.Property(x => x.ErrorCode).HasMaxLength(100);
            e.Property(x => x.OccurredAt).HasColumnType("datetime(6)");
            e.Property(x => x.ReceivedAt).HasColumnType("datetime(6)");
            e.Property(x => x.DetailJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.Username, x.EventId }).IsUnique();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.ServerId);
        });

        modelBuilder.Entity<DownloadTicket>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Path).HasMaxLength(1024);
            e.Property(x => x.ExpiresAt).HasColumnType("datetime(6)");
            e.Property(x => x.ConsumedAt).HasColumnType("datetime(6)");
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.ServerId);
            e.HasIndex(x => new { x.ExpiresAt, x.ConsumedAt });
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Server).WithMany()
                .HasForeignKey(x => x.ServerId).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<ApplicationType>().HasData(
            ApplicationType(1, "Backend", "Backend", 10),
            ApplicationType(2, "Frontend", "Frontend", 20),
            ApplicationType(3, "Sftp", "SFTP", 30),
            ApplicationType(4, "RabbitMq", "RabbitMQ", 40),
            ApplicationType(5, "Nginx", "NGINX", 50),
            ApplicationType(6, "HAProxy", "HAProxy", 60),
            ApplicationType(7, "Elastic", "Elastic", 70),
            ApplicationType(8, "Kibana", "Kibana", 80),
            ApplicationType(9, "Kafka", "Kafka", 90),
            ApplicationType(10, "MySql", "MySQL/MariaDB", 100),
            ApplicationType(11, "MongoDb", "MongoDB", 110),
            ApplicationType(12, "Redis", "Redis", 120),
            ApplicationType(13, "PostgreSql", "PostgreSQL", 130),
            ApplicationType(14, "SqlServer", "SQL Server", 140),
            ApplicationType(15, "Oracle", "Oracle", 150));

        modelBuilder.Entity<Menu>().HasData(
            Menu(1, "Home", "/", true, true, true, 0, null),
            Menu(2, "Ambienti", "/environments", true, true, true, 1, null),
            Menu(3, "Applicazioni", "/applications", true, true, true, 1, null),
            Menu(4, "Providers", "/providers", true, true, true, 1, null),
            Menu(5, "Servers", "/servers", true, true, true, 1, null),
            Menu(27, "HAProxy", "/haproxy", true, true, true, 6, null),
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
            Menu(22, "Registro esecuzioni", "/executions", true, true, true, 83, null),
            Menu(26, "Impostazioni", "/settings", true, true, true, 90, null));

        var systemAdminMenus = new[] { 13, 14, 15, 16, 19, 20, 21, 22, 26 };
        modelBuilder.Entity<MenuRole>().HasData(
            systemAdminMenus.Select(menuId => new MenuRole { MenuId = menuId, RolesId = 3 }).ToArray());

        modelBuilder.Entity<ConnectionPolicy>().HasData(new ConnectionPolicy
        {
            Id = 1,
            Mode = "Auto",
            UpdatedBy = "",
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        });

        modelBuilder.Entity<ServerUpdatePolicy>().HasData(new ServerUpdatePolicy
        {
            Id = 1,
            Enabled = true,
            LtsOnly = true,
            UpdatedBy = "",
            CreatedAt = SeedDate,
            UpdatedAt = SeedDate
        });
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

    private static ApplicationType ApplicationType(int id, string name, string label, int sortOrder) => new()
    {
        Id = id,
        Name = name,
        Label = label,
        SortOrder = sortOrder,
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
