using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTickets_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTickets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_ExpiresAt_ConsumedAt",
                table: "DownloadTickets",
                columns: new[] { "ExpiresAt", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_ServerId",
                table: "DownloadTickets",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_TokenHash",
                table: "DownloadTickets",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTickets_UserId",
                table: "DownloadTickets",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadTickets");
        }
    }
}
