using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HaproxyInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HaproxyInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    ServerApplicationId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Transport = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ApiHost = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    ApiPort = table.Column<int>(type: "int", nullable: false),
                    ApiVersion = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EncryptedPassword = table.Column<string>(type: "longtext", nullable: true),
                    PasswordKeyVersion = table.Column<int>(type: "int", nullable: true),
                    StatsUsername = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    EncryptedStatsPassword = table.Column<string>(type: "longtext", nullable: true),
                    StatsPasswordKeyVersion = table.Column<int>(type: "int", nullable: true),
                    CaPem = table.Column<string>(type: "longtext", nullable: true),
                    AllowSelfSigned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MonitoringChoice = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    MonitoringChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MonitoringChangedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    DiscoverySource = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    DiscoveryStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DetectedVersion = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    DetectedConfigPath = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastTestAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastTestStatus = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    LastTestDetail = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

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
