using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteServerOsUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OsEolDate",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsName",
                table: "Servers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OsUpdateCheckedAt",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsUpdateDetail",
                table: "Servers",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OsUpdateLtsOnly",
                table: "Servers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsUpdateStatus",
                table: "Servers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsUpdateTarget",
                table: "Servers",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OsUpdateTargetLts",
                table: "Servers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsVersion",
                table: "Servers",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServerUpdatePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LtsOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerUpdatePolicies", x => x.Id);
                });

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
