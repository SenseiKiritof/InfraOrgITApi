using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTunnelSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TunnelSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SessionKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ActorUsername = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ClientId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    ServerApplicationId = table.Column<int>(type: "int", nullable: true),
                    LocalPort = table.Column<int>(type: "int", nullable: false),
                    TargetHost = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    TargetPort = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Outcome = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelSessions", x => x.Id);
                    table.CheckConstraint("CK_TunnelSessions_Outcome", "`Outcome` IN ('Active','Stopped','Error','Stale')");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_ActorUsername",
                table: "TunnelSessions",
                column: "ActorUsername");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_Outcome_LastSeenAt",
                table: "TunnelSessions",
                columns: new[] { "Outcome", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_ServerId",
                table: "TunnelSessions",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_UserId_SessionKey",
                table: "TunnelSessions",
                columns: new[] { "UserId", "SessionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TunnelSessions");
        }
    }
}
