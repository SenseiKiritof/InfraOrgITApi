using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: true),
                    TargetHost = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    TargetPort = table.Column<int>(type: "int", nullable: true),
                    LocalPort = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    DetailJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvents_OccurredAt",
                table: "ClientEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvents_ServerId",
                table: "ClientEvents",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvents_Username_EventId",
                table: "ClientEvents",
                columns: new[] { "Username", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientEvents");
        }
    }
}
