using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationEnvironmentsAndApiCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EnvironmentDefinitionId",
                table: "ApplicationApiKeys",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationApiCalls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ApplicationVersionApiId = table.Column<int>(type: "int", nullable: true),
                    EnvironmentDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Url = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    QueryJson = table.Column<string>(type: "longtext", nullable: true),
                    RequestBody = table.Column<string>(type: "longtext", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: true),
                    ContentType = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    ResponseBody = table.Column<string>(type: "longtext", nullable: true),
                    ResponseSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Truncated = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationApiCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationApiCalls_ApplicationVersionApis_ApplicationVersio~",
                        column: x => x.ApplicationVersionApiId,
                        principalTable: "ApplicationVersionApis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApplicationApiCalls_EnvironmentDefinitions_EnvironmentDefini~",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApplicationEnvironments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    EnvironmentDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Hostname = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    IgnoreSslErrors = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationEnvironments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationEnvironments_EnvironmentDefinitions_EnvironmentDe~",
                        column: x => x.EnvironmentDefinitionId,
                        principalTable: "EnvironmentDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiKeys_ApplicationId_EnvironmentDefinitionId_Nam~",
                table: "ApplicationApiKeys",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId", "Name", "Location" },
                unique: true);

            // MySQL keeps an index for the ApplicationId foreign key: create the new
            // one before dropping the previous unique index.
            migrationBuilder.DropIndex(
                name: "IX_ApplicationApiKeys_ApplicationId_Name_Location",
                table: "ApplicationApiKeys");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiKeys_EnvironmentDefinitionId",
                table: "ApplicationApiKeys",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiCalls_ApplicationVersionApiId",
                table: "ApplicationApiCalls",
                column: "ApplicationVersionApiId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiCalls_EnvironmentDefinitionId",
                table: "ApplicationApiCalls",
                column: "EnvironmentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiCalls_Username_CreatedAt",
                table: "ApplicationApiCalls",
                columns: new[] { "Username", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEnvironments_ApplicationId_EnvironmentDefinitionId",
                table: "ApplicationEnvironments",
                columns: new[] { "ApplicationId", "EnvironmentDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationEnvironments_EnvironmentDefinitionId",
                table: "ApplicationEnvironments",
                column: "EnvironmentDefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationApiKeys_EnvironmentDefinitions_EnvironmentDefinit~",
                table: "ApplicationApiKeys",
                column: "EnvironmentDefinitionId",
                principalTable: "EnvironmentDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Backfill: the old per-application hostname/SSL settings move to the
            // environments where the application is actually deployed.
            migrationBuilder.Sql("""
                INSERT INTO `ApplicationEnvironments`
                    (`ApplicationId`, `EnvironmentDefinitionId`, `Hostname`, `IgnoreSslErrors`,
                     `CreatedAt`, `UpdatedAt`, `Version`)
                SELECT DISTINCT a.`Id`, pe.`EnvironmentDefinitionId`, a.`Hostname`,
                    a.`IgnoreSslErrors`, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0
                FROM `Applications` a
                JOIN `ApplicationVersions` v ON v.`ApplicationId` = a.`Id`
                JOIN `ServerApplications` sa ON sa.`ApplicationVersionId` = v.`Id`
                JOIN `Servers` s ON s.`Id` = sa.`ServerId`
                JOIN `ProviderEnvironments` pe ON pe.`Id` = s.`ProviderEnvironmentId`
                WHERE a.`Hostname` IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Hostname",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "IgnoreSslErrors",
                table: "Applications");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationApiKeys_EnvironmentDefinitions_EnvironmentDefinit~",
                table: "ApplicationApiKeys");

            migrationBuilder.DropTable(
                name: "ApplicationApiCalls");

            migrationBuilder.DropTable(
                name: "ApplicationEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationApiKeys_ApplicationId_EnvironmentDefinitionId_Nam~",
                table: "ApplicationApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationApiKeys_EnvironmentDefinitionId",
                table: "ApplicationApiKeys");

            migrationBuilder.DropColumn(
                name: "EnvironmentDefinitionId",
                table: "ApplicationApiKeys");

            migrationBuilder.AddColumn<string>(
                name: "Hostname",
                table: "Applications",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreSslErrors",
                table: "Applications",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationApiKeys_ApplicationId_Name_Location",
                table: "ApplicationApiKeys",
                columns: new[] { "ApplicationId", "Name", "Location" },
                unique: true);
        }
    }
}
