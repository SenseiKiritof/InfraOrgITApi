using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoteManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommandExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    ServerName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Script = table.Column<string>(type: "longtext", nullable: false),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    Output = table.Column<string>(type: "longtext", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandExecutions", x => x.Id);
                    table.CheckConstraint("CK_CommandExecutions_Kind", "`Kind` IN ('Action','Reachability','Console')");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommandTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Script = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandTemplates", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReachabilityTests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ServerId = table.Column<int>(type: "int", nullable: true),
                    Hostname = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReachabilityTests", x => x.Id);
                    table.CheckConstraint("CK_ReachabilityTests_Port", "`Port` BETWEEN 1 AND 65535");
                    table.ForeignKey(
                        name: "FK_ReachabilityTests_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ServerCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AuthType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "longtext", nullable: false),
                    EncryptedPassphrase = table.Column<string>(type: "longtext", nullable: true),
                    KeyVersion = table.Column<int>(type: "int", nullable: false),
                    NeedsReauth = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerCredentials", x => x.Id);
                    table.CheckConstraint("CK_ServerCredentials_AuthType", "`AuthType` IN ('Password','PrivateKey')");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApplicationActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CommandTemplateId = table.Column<int>(type: "int", nullable: true),
                    CustomScript = table.Column<string>(type: "longtext", nullable: true),
                    ParameterValuesJson = table.Column<string>(type: "json", nullable: false),
                    RequiresConfirmation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationActions_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationActions_CommandTemplates_CommandTemplateId",
                        column: x => x.CommandTemplateId,
                        principalTable: "CommandTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_CredentialId",
                table: "Servers",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationActions_ApplicationId",
                table: "ApplicationActions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationActions_CommandTemplateId",
                table: "ApplicationActions",
                column: "CommandTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationActions_Order",
                table: "ApplicationActions",
                column: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_IdempotencyKey",
                table: "CommandExecutions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_ServerId",
                table: "CommandExecutions",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_CommandExecutions_StartedAt",
                table: "CommandExecutions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CommandTemplates_Name",
                table: "CommandTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReachabilityTests_ServerId",
                table: "ReachabilityTests",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerCredentials_Name",
                table: "ServerCredentials",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Servers_ServerCredentials_CredentialId",
                table: "Servers",
                column: "CredentialId",
                principalTable: "ServerCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Servers_ServerCredentials_CredentialId",
                table: "Servers");

            migrationBuilder.DropTable(
                name: "ApplicationActions");

            migrationBuilder.DropTable(
                name: "CommandExecutions");

            migrationBuilder.DropTable(
                name: "ReachabilityTests");

            migrationBuilder.DropTable(
                name: "ServerCredentials");

            migrationBuilder.DropTable(
                name: "CommandTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Servers_CredentialId",
                table: "Servers");
        }
    }
}
