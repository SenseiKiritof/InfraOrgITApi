using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteHaproxyInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HaproxyInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerApplicationId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Transport = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ApiHost = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ApiPort = table.Column<int>(type: "INTEGER", nullable: false),
                    ApiVersion = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EncryptedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    PasswordKeyVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    StatsUsername = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EncryptedStatsPassword = table.Column<string>(type: "TEXT", nullable: true),
                    StatsPasswordKeyVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    CaPem = table.Column<string>(type: "TEXT", nullable: true),
                    AllowSelfSigned = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonitoringChoice = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    MonitoringChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MonitoringChangedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DiscoverySource = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DiscoveryStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DetectedVersion = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DetectedConfigPath = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastTestAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastTestStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    LastTestDetail = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyInstances", x => x.Id);
                    table.CheckConstraint("CK_HaproxyInstances_ApiVersion", "`ApiVersion` IN ('auto','v2','v3','stats')");
                    table.CheckConstraint("CK_HaproxyInstances_DiscoverySource", "`DiscoverySource` IN ('Auto','Manual')");
                    table.CheckConstraint("CK_HaproxyInstances_DiscoveryStatus", "`DiscoveryStatus` IN ('Found','Inactive','CredentialsRequired','Unreachable','Ready')");
                    table.CheckConstraint("CK_HaproxyInstances_MonitoringChoice", "`MonitoringChoice` IN ('Undecided','Enabled','Disabled')");
                    table.CheckConstraint("CK_HaproxyInstances_Transport", "`Transport` IN ('Direct','SshForward','DesktopRelay')");
                    table.ForeignKey(
                        name: "FK_HaproxyInstances_ServerApplications_ServerApplicationId",
                        column: x => x.ServerApplicationId,
                        principalTable: "ServerApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HaproxyInstances_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyInstances_ServerApplicationId",
                table: "HaproxyInstances",
                column: "ServerApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyInstances_ServerId",
                table: "HaproxyInstances",
                column: "ServerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaproxyInstances");
        }
    }
}
