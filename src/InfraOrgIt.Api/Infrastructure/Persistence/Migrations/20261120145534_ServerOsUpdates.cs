using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServerOsUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OsEolDate",
                table: "Servers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsName",
                table: "Servers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OsUpdateCheckedAt",
                table: "Servers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsUpdateDetail",
                table: "Servers",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OsUpdateLtsOnly",
                table: "Servers",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsUpdateStatus",
                table: "Servers",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsUpdateTarget",
                table: "Servers",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OsUpdateTargetLts",
                table: "Servers",
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsVersion",
                table: "Servers",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServerUpdatePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LtsOnly = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerUpdatePolicies", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "ServerUpdatePolicies",
                columns: new[] { "Id", "CreatedAt", "Enabled", "LtsOnly", "UpdatedAt", "UpdatedBy", "Version" },
                values: new object[] { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "", 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerUpdatePolicies");

            migrationBuilder.DropColumn(
                name: "OsEolDate",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsName",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsUpdateCheckedAt",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsUpdateDetail",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsUpdateLtsOnly",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsUpdateStatus",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsUpdateTarget",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsUpdateTargetLts",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OsVersion",
                table: "Servers");
        }
    }
}
