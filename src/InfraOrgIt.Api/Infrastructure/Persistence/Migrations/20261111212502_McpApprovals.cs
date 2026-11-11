using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class McpApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    ServerApplicationId = table.Column<int>(type: "int", nullable: true),
                    ApplicationActionId = table.Column<int>(type: "int", nullable: true),
                    ParametersJson = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedByUserId = table.Column<int>(type: "int", nullable: true),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpApprovals");
        }
    }
}
