using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteHaproxyMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Menus",
                columns: new[] { "Id", "CreatedAt", "HideBackButton", "IsActive", "IsVisible", "Name", "Order", "ParentId", "Path", "UpdatedAt", "Version" },
                values: new object[] { 27, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "HAProxy", 6, null, "/haproxy", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Menus",
                keyColumn: "Id",
                keyValue: 27);
        }
    }
}
