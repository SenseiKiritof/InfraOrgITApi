using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class McpAgentPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpApprovals");

            migrationBuilder.CreateTable(
                name: "McpAgents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClientKey = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "McpAgentPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "int", nullable: true),
                    AgentId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpAgentPermissions", x => x.Id);
                    table.CheckConstraint("CK_McpAgentPermissions_Action", "`Action` IN ('reachability.run','actions.run','*')");
                    table.ForeignKey(
                        name: "FK_McpAgentPermissions_EnvironmentDefinitions_EnvironmentDefini~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "McpPermissionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    AgentId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpPermissionRequests", x => x.Id);
                    table.CheckConstraint("CK_McpPermissionRequests_Status", "`Status` IN ('Pending','Approved','Denied')");
                    table.ForeignKey(
                        name: "FK_McpPermissionRequests_EnvironmentDefinitions_EnvironmentDefi~",
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpAgentPermissions");

            migrationBuilder.DropTable(
                name: "McpPermissionRequests");

            migrationBuilder.DropTable(
                name: "McpAgents");

            migrationBuilder.CreateTable(
                name: "McpApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ApplicationActionId = table.Column<int>(type: "int", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
                    ServerApplicationId = table.Column<int>(type: "int", nullable: true),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ParametersJson = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpApprovals", x => x.Id);
                    table.CheckConstraint("CK_McpApprovals_Status", "`Status` IN ('Pending','Approved','Denied','Used','Expired')");
                    table.ForeignKey(
                        name: "FK_McpApprovals_ApplicationActions_ApplicationActionId",
                        column: x => x.ApplicationActionId,
                        principalTable: "ApplicationActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpApprovals_ServerApplications_ServerApplicationId",
                        column: x => x.ServerApplicationId,
                        principalTable: "ServerApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpApprovals_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_McpApprovals_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_McpApprovals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_McpApprovals_ApplicationActionId",
                table: "McpApprovals",
                column: "ApplicationActionId");

            migrationBuilder.CreateIndex(
                name: "IX_McpApprovals_DecidedByUserId",
                table: "McpApprovals",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_McpApprovals_ServerApplicationId",
                table: "McpApprovals",
                column: "ServerApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_McpApprovals_ServerId",
                table: "McpApprovals",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_McpApprovals_Status_ExpiresAt",
                table: "McpApprovals",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpApprovals_UserId_Status_ExpiresAt",
                table: "McpApprovals",
                columns: new[] { "UserId", "Status", "ExpiresAt" });
        }
    }
}
