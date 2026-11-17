using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Transport",
                table: "TunnelSessions",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Client");

            migrationBuilder.CreateTable(
                name: "ConnectionPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "Auto"),
                    UpdatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionPolicies", x => x.Id);
                    table.CheckConstraint("CK_ConnectionPolicies_Mode", "`Mode` IN ('Server','Client','Auto')");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "ConnectionPolicies",
                columns: new[] { "Id", "CreatedAt", "Mode", "UpdatedAt", "UpdatedBy", "Version" },
                values: new object[] { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Auto", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "", 0 });

            migrationBuilder.InsertData(
                table: "Menus",
                columns: new[] { "Id", "CreatedAt", "HideBackButton", "IsActive", "IsVisible", "Name", "Order", "ParentId", "Path", "UpdatedAt", "Version" },
                values: new object[] { 26, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Impostazioni", 90, null, "/settings", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 });

            migrationBuilder.InsertData(
                table: "MenuRole",
                columns: new[] { "MenuId", "RolesId" },
                values: new object[] { 26, 3 });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TunnelSessions_Transport",
                table: "TunnelSessions",
                sql: "`Transport` IN ('Client','Server')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TunnelSessions_Transport",
                table: "TunnelSessions");

            migrationBuilder.DeleteData(
                table: "MenuRole",
                keyColumns: new[] { "MenuId", "RolesId" },
                keyValues: new object[] { 26, 3 });

            migrationBuilder.DeleteData(
                table: "Menus",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DropColumn(
                name: "Transport",
                table: "TunnelSessions");
        }
    }
}
