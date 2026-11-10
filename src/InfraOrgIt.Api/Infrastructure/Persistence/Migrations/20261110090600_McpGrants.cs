using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class McpGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "McpGrants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpGrants");
        }
    }
}
