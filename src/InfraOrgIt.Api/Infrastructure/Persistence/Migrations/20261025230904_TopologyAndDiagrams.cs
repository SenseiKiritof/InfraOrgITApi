using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TopologyAndDiagrams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Diagrams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagrams", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProxyConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SourceServerId = table.Column<int>(type: "int", nullable: false),
                    TargetServerId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DiagramEdges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DiagramId = table.Column<int>(type: "int", nullable: false),
                    ProxyConnectionId = table.Column<int>(type: "int", nullable: false),
                    TargetNodeType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    TargetEntityId = table.Column<int>(type: "int", nullable: false),
                    SourceHandle = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    TargetHandle = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    BendX = table.Column<double>(type: "double", nullable: true),
                    BendY = table.Column<double>(type: "double", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DiagramNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DiagramId = table.Column<int>(type: "int", nullable: false),
                    NodeType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<double>(type: "double", nullable: false),
                    Y = table.Column<double>(type: "double", nullable: false),
                    Width = table.Column<double>(type: "double", nullable: true),
                    Height = table.Column<double>(type: "double", nullable: true),
                    Collapsed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DiagramScopeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DiagramId = table.Column<int>(type: "int", nullable: false),
                    ScopeType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProxyRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ProxyConnectionId = table.Column<int>(type: "int", nullable: false),
                    Route = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    MappedTo = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    TargetServerApplicationId = table.Column<int>(type: "int", nullable: true),
                    TargetProxyConnectionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                    table.ForeignKey(
                        name: "FK_ProxyRoutes_ServerApplications_TargetServerApplicationId",
                        column: x => x.TargetServerApplicationId,
                        principalTable: "ServerApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ServerApplications_ProxyRouteId",
                table: "ServerApplications",
                column: "ProxyRouteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagramEdges_DiagramId_ProxyConnectionId_TargetNodeType_Targ~",
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
                name: "IX_ProxyConnections_SourceServerId",
                table: "ProxyConnections",
                column: "SourceServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyConnections_TargetServerId",
                table: "ProxyConnections",
                column: "TargetServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_ProxyConnectionId_Route",
                table: "ProxyRoutes",
                columns: new[] { "ProxyConnectionId", "Route" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_TargetProxyConnectionId",
                table: "ProxyRoutes",
                column: "TargetProxyConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_TargetServerApplicationId",
                table: "ProxyRoutes",
                column: "TargetServerApplicationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServerApplications_ProxyRoutes_ProxyRouteId",
                table: "ServerApplications",
                column: "ProxyRouteId",
                principalTable: "ProxyRoutes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServerApplications_ProxyRoutes_ProxyRouteId",
                table: "ServerApplications");

            migrationBuilder.DropTable(
                name: "DiagramEdges");

            migrationBuilder.DropTable(
                name: "DiagramNodes");

            migrationBuilder.DropTable(
                name: "DiagramScopeItems");

            migrationBuilder.DropTable(
                name: "ProxyRoutes");

            migrationBuilder.DropTable(
                name: "Diagrams");

            migrationBuilder.DropTable(
                name: "ProxyConnections");

            migrationBuilder.DropIndex(
                name: "IX_ServerApplications_ProxyRouteId",
                table: "ServerApplications");
        }
    }
}
