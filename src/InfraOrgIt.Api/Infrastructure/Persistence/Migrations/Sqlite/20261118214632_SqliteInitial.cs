using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BasePath = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ListeningOnPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.CheckConstraint("CK_Applications_Type", "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka','MySql','MongoDb','Redis')");
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    TargetId = table.Column<int>(type: "INTEGER", nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetHost = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    TargetPort = table.Column<int>(type: "INTEGER", nullable: true),
                    LocalPort = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DetailJson = table.Column<string>(type: "longtext", nullable: true),
                    PrevHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_Hash", "`Hash` IS NULL OR length(`Hash`) = 64");
                    table.CheckConstraint("CK_AuditEvents_Kind", "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError','ClientCheck','CredentialDownload','FileTransfer','AdminChange')");
                    table.CheckConstraint("CK_AuditEvents_Outcome", "`Outcome` IN ('Ok','Error','Denied')");
                });

            migrationBuilder.CreateTable(
                name: "ClientEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    TargetPort = table.Column<int>(type: "INTEGER", nullable: true),
                    LocalPort = table.Column<int>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    DetailJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommandExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Script = table.Column<string>(type: "longtext", nullable: false),
                    RequiresSudo = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Output = table.Column<string>(type: "longtext", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandExecutions", x => x.Id);
                    table.CheckConstraint("CK_CommandExecutions_Kind", "`Kind` IN ('Action','Reachability','Console')");
                });

            migrationBuilder.CreateTable(
                name: "CommandTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Script = table.Column<string>(type: "longtext", nullable: false),
                    RequiresSudo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConnectionPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false, defaultValue: "Auto"),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionPolicies", x => x.Id);
                    table.CheckConstraint("CK_ConnectionPolicies_Mode", "`Mode` IN ('Server','Client','Auto')");
                });

            migrationBuilder.CreateTable(
                name: "Diagrams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagrams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Menus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    HideBackButton = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menus_Menus_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ScopeEnum = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "longtext", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Translations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Translations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Surname = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "system"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Theme", "`Theme` IN ('light','dark','system')");
                });

            migrationBuilder.CreateTable(
                name: "ApplicationVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionNumber = table.Column<string>(type: "TEXT", maxLength: 25, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationVersions_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandTemplateId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomScript = table.Column<string>(type: "longtext", nullable: true),
                    ParameterValuesJson = table.Column<string>(type: "json", nullable: false),
                    RequiresConfirmation = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresSudo = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationActions_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationActions_CommandTemplates_CommandTemplateId",
                        column: x => x.CommandTemplateId,
                        principalTable: "CommandTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiagramEdges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiagramId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProxyConnectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetNodeType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TargetEntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceHandle = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TargetHandle = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    BendX = table.Column<double>(type: "REAL", nullable: true),
                    BendY = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagramEdges", x => x.Id);
                    table.CheckConstraint("CK_DiagramEdges_TargetNodeType", "`TargetNodeType` IN ('Provider','ProviderEnvironment','Server','ServerApplication')");
                    table.ForeignKey(
                        name: "FK_DiagramEdges_Diagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "Diagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiagramNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiagramId = table.Column<int>(type: "INTEGER", nullable: false),
                    NodeType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: true),
                    Height = table.Column<double>(type: "REAL", nullable: true),
                    Collapsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagramNodes", x => x.Id);
                    table.CheckConstraint("CK_DiagramNodes_NodeType", "`NodeType` IN ('Provider','ProviderEnvironment','Server','ServerApplication')");
                    table.ForeignKey(
                        name: "FK_DiagramNodes_Diagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "Diagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiagramScopeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiagramId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagramScopeItems", x => x.Id);
                    table.CheckConstraint("CK_DiagramScopeItems_ScopeType", "`ScopeType` IN ('Provider','ProviderEnvironment','Project')");
                    table.ForeignKey(
                        name: "FK_DiagramScopeItems_Diagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "Diagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationApiKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    EncryptedValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationApiKeys", x => x.Id);
                    table.CheckConstraint("CK_ApplicationApiKeys_Location", "`Location` IN ('Header','Query')");
                    table.ForeignKey(
                        name: "FK_ApplicationApiKeys_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationApiKeys_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationEnvironments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IgnoreSslErrors = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationEnvironments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationEnvironments_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationProject",
                columns: table => new
                {
                    ApplicationsId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationProject", x => new { x.ApplicationsId, x.ProjectsId });
                    table.ForeignKey(
                        name: "FK_ApplicationProject_Applications_ApplicationsId",
                        column: x => x.ApplicationsId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationProject_Projects_ProjectsId",
                        column: x => x.ProjectsId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProviderEnvironments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderId = table.Column<int>(type: "INTEGER", nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderEnvironments_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderEnvironments_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MenuRole",
                columns: table => new
                {
                    MenuId = table.Column<int>(type: "INTEGER", nullable: false),
                    RolesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuRole", x => new { x.MenuId, x.RolesId });
                    table.ForeignKey(
                        name: "FK_MenuRole_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuRole_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DesktopDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "longtext", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesktopDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesktopDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupUser",
                columns: table => new
                {
                    GroupsId = table.Column<int>(type: "INTEGER", nullable: false),
                    UsersId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupUser", x => new { x.GroupsId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_GroupUser_Groups_GroupsId",
                        column: x => x.GroupsId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupUser_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgents", x => x.Id);
                    table.CheckConstraint("CK_McpAgents_Status", "`Status` IN ('Pending','Approved','Denied')");
                    table.ForeignKey(
                        name: "FK_McpAgents_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_McpAgents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpGrants", x => x.Id);
                    table.CheckConstraint("CK_McpGrants_Action", "`Action` IN ('reachability.run','actions.run','*')");
                    table.CheckConstraint("CK_McpGrants_Subject", "((`UserId` IS NULL) + (`GroupId` IS NULL)) = 1");
                    table.ForeignKey(
                        name: "FK_McpGrants_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpGrants_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleUser",
                columns: table => new
                {
                    RolesId = table.Column<int>(type: "INTEGER", nullable: false),
                    UsersId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleUser", x => new { x.RolesId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_RoleUser_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleUser_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServerCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowOffline = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "longtext", nullable: false),
                    EncryptedPassphrase = table.Column<string>(type: "longtext", nullable: true),
                    EncryptedSudoSecret = table.Column<string>(type: "TEXT", nullable: true),
                    SudoKeyVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    KeyVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    NeedsReauth = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerCredentials", x => x.Id);
                    table.CheckConstraint("CK_ServerCredentials_AuthType", "`AuthType` IN ('Password','PrivateKey')");
                    table.ForeignKey(
                        name: "FK_ServerCredentials_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationVersionApis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationVersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestTypeEnum = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationVersionApis", x => x.Id);
                    table.CheckConstraint("CK_ApplicationVersionApis_RequestTypeEnum", "`RequestTypeEnum` IN ('GET','POST','PUT','PATCH','DELETE')");
                    table.ForeignKey(
                        name: "FK_ApplicationVersionApis_ApplicationVersions_ApplicationVersionId",
                        column: x => x.ApplicationVersionId,
                        principalTable: "ApplicationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderEnvironmentGroupRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderEnvironmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderEnvironmentGroupRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderEnvironmentGroupRoles_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProviderEnvironmentGroupRoles_ProviderEnvironments_ProviderEnvironmentId",
                        column: x => x.ProviderEnvironmentId,
                        principalTable: "ProviderEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProviderEnvironmentGroupRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpAgentPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    AgentId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgentPermissions", x => x.Id);
                    table.CheckConstraint("CK_McpAgentPermissions_Action", "`Action` IN ('reachability.run','actions.run','*')");
                    table.ForeignKey(
                        name: "FK_McpAgentPermissions_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpAgentPermissions_McpAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "McpAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpPermissionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpPermissionRequests", x => x.Id);
                    table.CheckConstraint("CK_McpPermissionRequests_Status", "`Status` IN ('Pending','Approved','Denied')");
                    table.ForeignKey(
                        name: "FK_McpPermissionRequests_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpPermissionRequests_McpAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "McpAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpPermissionRequests_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProviderEnvironmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SshPort = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 22),
                    PublicIpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    CpuCores = table.Column<int>(type: "INTEGER", nullable: true),
                    MemoryGb = table.Column<int>(type: "INTEGER", nullable: true),
                    DiskGb = table.Column<int>(type: "INTEGER", nullable: true),
                    OperatingSystem = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    HaNode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ManagedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CredentialId = table.Column<int>(type: "INTEGER", nullable: true),
                    HostKeyFingerprint = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    FileTransferProtocol = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    FileTransferPort = table.Column<int>(type: "INTEGER", nullable: true),
                    FileTransferCredentialId = table.Column<int>(type: "INTEGER", nullable: true),
                    JumpServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                    table.CheckConstraint("CK_Servers_FileTransferProtocol", "`FileTransferProtocol` IS NULL OR `FileTransferProtocol` IN ('Sftp','Ftp','Ftps')");
                    table.ForeignKey(
                        name: "FK_Servers_ProviderEnvironments_ProviderEnvironmentId",
                        column: x => x.ProviderEnvironmentId,
                        principalTable: "ProviderEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Servers_ServerCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "ServerCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Servers_ServerCredentials_FileTransferCredentialId",
                        column: x => x.FileTransferCredentialId,
                        principalTable: "ServerCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Servers_Servers_JumpServerId",
                        column: x => x.JumpServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationApiCalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ApplicationVersionApiId = table.Column<int>(type: "INTEGER", nullable: true),
                    EnvironmentDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    QueryJson = table.Column<string>(type: "longtext", nullable: true),
                    RequestBody = table.Column<string>(type: "longtext", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ResponseBody = table.Column<string>(type: "longtext", nullable: true),
                    ResponseSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    Binary = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationApiCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationApiCalls_ApplicationVersionApis_ApplicationVersionApiId",
                        column: x => x.ApplicationVersionApiId,
                        principalTable: "ApplicationVersionApis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApplicationApiCalls_EnvironmentDefinitions_EnvironmentDefinitionId",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationApiQueryParams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApplicationApiId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParamType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationApiQueryParams", x => x.Id);
                    table.CheckConstraint("CK_ApplicationApiQueryParams_ParamType", "`ParamType` IN ('Int','String','Bool','DateTime','Double','Decimal')");
                    table.ForeignKey(
                        name: "FK_ApplicationApiQueryParams_ApplicationVersionApis_ApplicationApiId",
                        column: x => x.ApplicationApiId,
                        principalTable: "ApplicationVersionApis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DownloadTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTickets_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTickets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProxyConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyConnections", x => x.Id);
                    table.CheckConstraint("CK_ProxyConnections_DifferentServers", "`SourceServerId` <> `TargetServerId`");
                    table.CheckConstraint("CK_ProxyConnections_Direction", "`Direction` IN ('Outgoing','Incoming')");
                    table.ForeignKey(
                        name: "FK_ProxyConnections_Servers_SourceServerId",
                        column: x => x.SourceServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProxyConnections_Servers_TargetServerId",
                        column: x => x.TargetServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReachabilityTests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachabilityTests", x => x.Id);
                    table.CheckConstraint("CK_ReachabilityTests_Port", "`Port` BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "FK_ReachabilityTests_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TunnelServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TargetHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false, defaultValue: "127.0.0.1"),
                    TargetPort = table.Column<int>(type: "INTEGER", nullable: true),
                    LocalPort = table.Column<int>(type: "INTEGER", nullable: true),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Tcp"),
                    OpenPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AutoStart = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelServices", x => x.Id);
                    table.CheckConstraint("CK_TunnelServices_Kind", "`Kind` IN ('Tcp','Http','Https')");
                    table.CheckConstraint("CK_TunnelServices_Mode", "`Mode` IN ('Local','Dynamic','Remote')");
                    table.ForeignKey(
                        name: "FK_TunnelServices_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TunnelSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    TunnelServiceId = table.Column<int>(type: "INTEGER", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Local"),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "Client"),
                    LocalPort = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetHost = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TargetPort = table.Column<int>(type: "INTEGER", nullable: true),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelSessions", x => x.Id);
                    table.CheckConstraint("CK_TunnelSessions_Mode", "`Mode` IN ('Local','Dynamic','Remote')");
                    table.CheckConstraint("CK_TunnelSessions_Outcome", "`Outcome` IN ('Active','Stopped','Error','Stale')");
                    table.CheckConstraint("CK_TunnelSessions_Transport", "`Transport` IN ('Client','Server')");
                    table.ForeignKey(
                        name: "FK_TunnelSessions_TunnelServices_TunnelServiceId",
                        column: x => x.TunnelServiceId,
                        principalTable: "TunnelServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProxyRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProxyConnectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Route = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    VirtualHost = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ListenPort = table.Column<int>(type: "INTEGER", nullable: true),
                    MappedTo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TargetServerApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetProxyConnectionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProxyRoutes_ProxyConnections_ProxyConnectionId",
                        column: x => x.ProxyConnectionId,
                        principalTable: "ProxyConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProxyRoutes_ProxyConnections_TargetProxyConnectionId",
                        column: x => x.TargetProxyConnectionId,
                        principalTable: "ProxyConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationVersionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExposedPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Runtime = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ContainerImage = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ContainerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ComposeDirectory = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    HostPort = table.Column<int>(type: "INTEGER", nullable: true),
                    HaRole = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ProxyRouteId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerApplications", x => x.Id);
                    table.CheckConstraint("CK_ServerApplications_HaRole", "`HaRole` IN ('Primary','Secondary')");
                    table.CheckConstraint("CK_ServerApplications_Runtime", "`Runtime` IN ('Container','Native')");
                    table.ForeignKey(
                        name: "FK_ServerApplications_ApplicationVersions_ApplicationVersionId",
                        column: x => x.ApplicationVersionId,
                        principalTable: "ApplicationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServerApplications_ProxyRoutes_ProxyRouteId",
                        column: x => x.ProxyRouteId,
                        principalTable: "ProxyRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServerApplications_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ConnectionPolicies",
                columns: new[] { "Id", "CreatedAt", "Mode", "UpdatedAt", "UpdatedBy", "Version" },
                values: new object[] { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Auto", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "", 0 });

            migrationBuilder.InsertData(
                table: "Menus",
                columns: new[] { "Id", "CreatedAt", "HideBackButton", "IsActive", "IsVisible", "Name", "Order", "ParentId", "Path", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Home", 0, null, "/", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 2, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Ambienti", 1, null, "/environments", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 3, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Applicazioni", 1, null, "/applications", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 4, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Providers", 1, null, "/providers", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 5, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Servers", 1, null, "/servers", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 10, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Diagrammi", 10, null, "/diagrams", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 12, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Progetti", 30, null, "/projects", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 13, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Users", 40, null, "/users", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 14, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Groups", 50, null, "/groups", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 15, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Roles", 60, null, "/roles", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 16, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Menu", 70, null, "/menu", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 19, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Credenziali", 80, null, "/credentials", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 20, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Comandi", 81, null, "/commands", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 21, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Test di rete", 82, null, "/reachability", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 22, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Registro esecuzioni", 83, null, "/executions", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 26, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Impostazioni", 90, null, "/settings", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedAt", "Label", "Name", "ScopeEnum", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Amministratore server", "ServerAdmin", "Environment", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 2, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Operatore server", "ServerOperator", "Environment", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 3, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Amministratore di sistema", "SystemAdmin", "System", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.InsertData(
                table: "MenuRole",
                columns: new[] { "MenuId", "RolesId" },
                values: new object[,]
                {
                    { 13, 3 },
                    { 14, 3 },
                    { 15, 3 },
                    { 16, 3 },
                    { 19, 3 },
                    { 20, 3 },
                    { 21, 3 },
                    { 22, 3 },
                    { 26, 3 }
                });

            migrationBuilder.InsertData(
                table: "Menus",
                columns: new[] { "Id", "CreatedAt", "HideBackButton", "IsActive", "IsVisible", "Name", "Order", "ParentId", "Path", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 6, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Ambiente {name}", 0, 2, "/environments/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 7, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Applicazione {name}", 0, 3, "/applications/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 8, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Server {name}", 0, 5, "/servers/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 9, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Provider {name}", 0, 4, "/providers/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 18, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Progetto {name}", 0, 12, "/projects/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_TokenHash",
                table: "ApiTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_UserId",
                table: "ApiTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationActions_ApplicationId",
                table: "ApplicationActions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationActions_CommandTemplateId",
                table: "ApplicationActions",
                column: "CommandTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationActions_Order",
                table: "ApplicationActions",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiCalls_ApplicationVersionApiId",
                table: "ApplicationApiCalls",
                column: "ApplicationVersionApiId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiCalls_EnvironmentDefinitionId",
                table: "ApplicationApiCalls",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiCalls_Username_CreatedAt",
                table: "ApplicationApiCalls",
                columns: new[] { "Username", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiKeys_ApplicationId_EnvironmentDefinitionId_Name_Location",
                table: "ApplicationApiKeys",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId", "Name", "Location" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiKeys_EnvironmentDefinitionId",
                table: "ApplicationApiKeys",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiQueryParams_ApplicationApiId",
                table: "ApplicationApiQueryParams",
                column: "ApplicationApiId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEnvironments_ApplicationId_EnvironmentDefinitionId",
                table: "ApplicationEnvironments",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEnvironments_EnvironmentDefinitionId",
                table: "ApplicationEnvironments",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationProject_ProjectsId",
                table: "ApplicationProject",
                column: "ProjectsId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationVersionApis_ApplicationVersionId",
                table: "ApplicationVersionApis",
                column: "ApplicationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationVersions_ApplicationId_VersionNumber",
                table: "ApplicationVersions",
                columns: new[] { "ApplicationId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorUsername_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "ActorUsername", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Kind_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "Kind", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvents_OccurredAt",
                table: "ClientEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvents_ServerId",
                table: "ClientEvents",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvents_Username_EventId",
                table: "ClientEvents",
                columns: new[] { "Username", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_IdempotencyKey",
                table: "CommandExecutions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_ServerId",
                table: "CommandExecutions",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_StartedAt",
                table: "CommandExecutions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommandTemplates_Name",
                table: "CommandTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DesktopDevices_UserId_Fingerprint",
                table: "DesktopDevices",
                columns: new[] { "UserId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagramEdges_DiagramId_ProxyConnectionId_TargetNodeType_TargetEntityId",
                table: "DiagramEdges",
                columns: new[] { "DiagramId", "ProxyConnectionId", "TargetNodeType", "TargetEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagramNodes_DiagramId_NodeType_EntityId",
                table: "DiagramNodes",
                columns: new[] { "DiagramId", "NodeType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagramScopeItems_DiagramId_ScopeType_EntityId",
                table: "DiagramScopeItems",
                columns: new[] { "DiagramId", "ScopeType", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_ExpiresAt_ConsumedAt",
                table: "DownloadTickets",
                columns: new[] { "ExpiresAt", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_ServerId",
                table: "DownloadTickets",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_TokenHash",
                table: "DownloadTickets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_UserId",
                table: "DownloadTickets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentDefinitions_Name",
                table: "EnvironmentDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupUser_UsersId",
                table: "GroupUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentPermissions_Action_EnvironmentDefinitionId",
                table: "McpAgentPermissions",
                columns: new[] { "Action", "EnvironmentDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentPermissions_AgentId",
                table: "McpAgentPermissions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgentPermissions_EnvironmentDefinitionId",
                table: "McpAgentPermissions",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgents_ApprovedByUserId",
                table: "McpAgents",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgents_Status",
                table: "McpAgents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_McpAgents_UserId_ClientKey",
                table: "McpAgents",
                columns: new[] { "UserId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_Action_EnvironmentDefinitionId",
                table: "McpGrants",
                columns: new[] { "Action", "EnvironmentDefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_EnvironmentDefinitionId",
                table: "McpGrants",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_GroupId",
                table: "McpGrants",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_McpGrants_UserId",
                table: "McpGrants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpPermissionRequests_AgentId_Status",
                table: "McpPermissionRequests",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_McpPermissionRequests_DecidedByUserId",
                table: "McpPermissionRequests",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpPermissionRequests_EnvironmentDefinitionId",
                table: "McpPermissionRequests",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuRole_RolesId",
                table: "MenuRole",
                column: "RolesId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_ParentId",
                table: "Menus",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEnvironmentGroupRoles_GroupId",
                table: "ProviderEnvironmentGroupRoles",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEnvironmentGroupRoles_ProviderEnvironmentId_GroupId_RoleId",
                table: "ProviderEnvironmentGroupRoles",
                columns: new[] { "ProviderEnvironmentId", "GroupId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEnvironmentGroupRoles_RoleId",
                table: "ProviderEnvironmentGroupRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEnvironments_EnvironmentDefinitionId",
                table: "ProviderEnvironments",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderEnvironments_ProviderId_EnvironmentDefinitionId",
                table: "ProviderEnvironments",
                columns: new[] { "ProviderId", "EnvironmentDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_Name",
                table: "Providers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyConnections_SourceServerId",
                table: "ProxyConnections",
                column: "SourceServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyConnections_TargetServerId",
                table: "ProxyConnections",
                column: "TargetServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_ProxyConnectionId_VirtualHost_ListenPort_Route",
                table: "ProxyRoutes",
                columns: new[] { "ProxyConnectionId", "VirtualHost", "ListenPort", "Route" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_TargetProxyConnectionId",
                table: "ProxyRoutes",
                column: "TargetProxyConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_TargetServerApplicationId",
                table: "ProxyRoutes",
                column: "TargetServerApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReachabilityTests_ServerId",
                table: "ReachabilityTests",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleUser_UsersId",
                table: "RoleUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerApplications_ApplicationVersionId",
                table: "ServerApplications",
                column: "ApplicationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerApplications_ProxyRouteId",
                table: "ServerApplications",
                column: "ProxyRouteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerApplications_ServerId",
                table: "ServerApplications",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerCredentials_Name",
                table: "ServerCredentials",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerCredentials_OwnerUserId",
                table: "ServerCredentials",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_CredentialId",
                table: "Servers",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_FileTransferCredentialId",
                table: "Servers",
                column: "FileTransferCredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_JumpServerId",
                table: "Servers",
                column: "JumpServerId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_ProviderEnvironmentId",
                table: "Servers",
                column: "ProviderEnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                table: "Sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Username",
                table: "Sessions",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_Translations_EntityType_EntityId_Field_Language",
                table: "Translations",
                columns: new[] { "EntityType", "EntityId", "Field", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TunnelServices_ServerId_Name",
                table: "TunnelServices",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_ActorUsername",
                table: "TunnelSessions",
                column: "ActorUsername");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_Outcome_LastSeenAt",
                table: "TunnelSessions",
                columns: new[] { "Outcome", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_ServerApplicationId",
                table: "TunnelSessions",
                column: "ServerApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_ServerId",
                table: "TunnelSessions",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_TunnelServiceId",
                table: "TunnelSessions",
                column: "TunnelServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_UserId_SessionKey",
                table: "TunnelSessions",
                columns: new[] { "UserId", "SessionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ProxyRoutes_ServerApplications_TargetServerApplicationId",
                table: "ProxyRoutes",
                column: "TargetServerApplicationId",
                principalTable: "ServerApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServerCredentials_Users_OwnerUserId",
                table: "ServerCredentials");

            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationVersions_Applications_ApplicationId",
                table: "ApplicationVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProviderEnvironments_EnvironmentDefinitions_EnvironmentDefinitionId",
                table: "ProviderEnvironments");

            migrationBuilder.DropForeignKey(
                name: "FK_ServerApplications_ApplicationVersions_ApplicationVersionId",
                table: "ServerApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_ProxyConnections_Servers_SourceServerId",
                table: "ProxyConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_ProxyConnections_Servers_TargetServerId",
                table: "ProxyConnections");

            migrationBuilder.DropForeignKey(
                name: "FK_ServerApplications_Servers_ServerId",
                table: "ServerApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_ProxyRoutes_ProxyConnections_ProxyConnectionId",
                table: "ProxyRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_ProxyRoutes_ProxyConnections_TargetProxyConnectionId",
                table: "ProxyRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_ProxyRoutes_ServerApplications_TargetServerApplicationId",
                table: "ProxyRoutes");

            migrationBuilder.DropTable(
                name: "ApiTokens");

            migrationBuilder.DropTable(
                name: "ApplicationActions");

            migrationBuilder.DropTable(
                name: "ApplicationApiCalls");

            migrationBuilder.DropTable(
                name: "ApplicationApiKeys");

            migrationBuilder.DropTable(
                name: "ApplicationApiQueryParams");

            migrationBuilder.DropTable(
                name: "ApplicationEnvironments");

            migrationBuilder.DropTable(
                name: "ApplicationProject");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "ClientEvents");

            migrationBuilder.DropTable(
                name: "CommandExecutions");

            migrationBuilder.DropTable(
                name: "ConnectionPolicies");

            migrationBuilder.DropTable(
                name: "DesktopDevices");

            migrationBuilder.DropTable(
                name: "DiagramEdges");

            migrationBuilder.DropTable(
                name: "DiagramNodes");

            migrationBuilder.DropTable(
                name: "DiagramScopeItems");

            migrationBuilder.DropTable(
                name: "DownloadTickets");

            migrationBuilder.DropTable(
                name: "GroupUser");

            migrationBuilder.DropTable(
                name: "McpAgentPermissions");

            migrationBuilder.DropTable(
                name: "McpGrants");

            migrationBuilder.DropTable(
                name: "McpPermissionRequests");

            migrationBuilder.DropTable(
                name: "MenuRole");

            migrationBuilder.DropTable(
                name: "ProviderEnvironmentGroupRoles");

            migrationBuilder.DropTable(
                name: "ReachabilityTests");

            migrationBuilder.DropTable(
                name: "RoleUser");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Translations");

            migrationBuilder.DropTable(
                name: "TunnelSessions");

            migrationBuilder.DropTable(
                name: "CommandTemplates");

            migrationBuilder.DropTable(
                name: "ApplicationVersionApis");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Diagrams");

            migrationBuilder.DropTable(
                name: "McpAgents");

            migrationBuilder.DropTable(
                name: "Menus");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "TunnelServices");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "EnvironmentDefinitions");

            migrationBuilder.DropTable(
                name: "ApplicationVersions");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "ProviderEnvironments");

            migrationBuilder.DropTable(
                name: "ServerCredentials");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropTable(
                name: "ProxyConnections");

            migrationBuilder.DropTable(
                name: "ServerApplications");

            migrationBuilder.DropTable(
                name: "ProxyRoutes");
        }
    }
}
