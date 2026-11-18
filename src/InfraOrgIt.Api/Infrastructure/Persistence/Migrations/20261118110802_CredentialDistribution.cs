using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CredentialDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents");

            migrationBuilder.AddColumn<bool>(
                name: "AllowOffline",
                table: "ServerCredentials",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "ServerCredentials",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DesktopDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "longtext", nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DesktopDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DesktopDevices_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ServerCredentials_OwnerUserId",
                table: "ServerCredentials",
                column: "OwnerUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents",
                sql: "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError','ClientCheck','CredentialDownload','FileTransfer','AdminChange')");

            migrationBuilder.CreateIndex(
                name: "IX_DesktopDevices_UserId_Fingerprint",
                table: "DesktopDevices",
                columns: new[] { "UserId", "Fingerprint" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServerCredentials_Users_OwnerUserId",
                table: "ServerCredentials",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServerCredentials_Users_OwnerUserId",
                table: "ServerCredentials");

            migrationBuilder.DropTable(
                name: "DesktopDevices");

            migrationBuilder.DropIndex(
                name: "IX_ServerCredentials_OwnerUserId",
                table: "ServerCredentials");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "AllowOffline",
                table: "ServerCredentials");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "ServerCredentials");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents",
                sql: "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError','ClientCheck','FileTransfer','AdminChange')");
        }
    }
}
