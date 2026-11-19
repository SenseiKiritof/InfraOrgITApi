using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteApplicationTypesConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications");

            migrationBuilder.CreateTable(
                name: "ApplicationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationTypes", x => x.Id);
                    table.UniqueConstraint("AK_ApplicationTypes_Name", x => x.Name);
                });

            migrationBuilder.InsertData(
                table: "ApplicationTypes",
                columns: new[] { "Id", "CreatedAt", "Label", "Name", "SortOrder", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Backend", "Backend", 10, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 2, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Frontend", "Frontend", 20, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 3, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "SFTP", "Sftp", 30, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 4, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "RabbitMQ", "RabbitMq", 40, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 5, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "NGINX", "Nginx", 50, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 6, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "HAProxy", "HAProxy", 60, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 7, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Elastic", "Elastic", 70, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 8, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Kibana", "Kibana", 80, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 9, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Kafka", "Kafka", 90, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 10, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "MySQL/MariaDB", "MySql", 100, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 11, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "MongoDB", "MongoDb", 110, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 12, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Redis", "Redis", 120, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Type",
                table: "Applications",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationTypes_Name",
                table: "ApplicationTypes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_ApplicationTypes_Type",
                table: "Applications",
                column: "Type",
                principalTable: "ApplicationTypes",
                principalColumn: "Name",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_ApplicationTypes_Type",
                table: "Applications");

            migrationBuilder.DropTable(
                name: "ApplicationTypes");

            migrationBuilder.DropIndex(
                name: "IX_Applications_Type",
                table: "Applications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications",
                sql: "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka','MySql','MongoDb','Redis','HAProxy')");
        }
    }
}
