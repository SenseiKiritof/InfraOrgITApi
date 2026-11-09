using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class McpFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    OccurredAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorUsername = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    Action = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    TargetId = table.Column<int>(type: "int", nullable: true),
                    ServerId = table.Column<int>(type: "int", nullable: true),
                    TargetHost = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    TargetPort = table.Column<int>(type: "int", nullable: true),
                    LocalPort = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DetailJson = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.CheckConstraint("CK_AuditEvents_Kind", "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError')");
                    table.CheckConstraint("CK_AuditEvents_Outcome", "`Outcome` IN ('Ok','Error','Denied')");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_TokenHash",
                table: "ApiTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokens_UserId",
                table: "ApiTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorUsername_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "ActorUsername", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Kind_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "Kind", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OccurredAt",
                table: "AuditEvents",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiTokens");

            migrationBuilder.DropTable(
                name: "AuditEvents");
        }
    }
}
