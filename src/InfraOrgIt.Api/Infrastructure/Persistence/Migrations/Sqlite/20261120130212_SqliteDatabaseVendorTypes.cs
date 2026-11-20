using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteDatabaseVendorTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "ApplicationTypes",
                columns: new[] { "Id", "CreatedAt", "Label", "Name", "SortOrder", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 13, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "PostgreSQL", "PostgreSql", 130, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 14, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "SQL Server", "SqlServer", 140, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 15, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Oracle", "Oracle", 150, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ApplicationTypes",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "ApplicationTypes",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "ApplicationTypes",
                keyColumn: "Id",
                keyValue: 15);
        }
    }
}
