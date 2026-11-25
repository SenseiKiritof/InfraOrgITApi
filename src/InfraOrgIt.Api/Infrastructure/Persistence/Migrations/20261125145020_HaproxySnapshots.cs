using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HaproxySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfigurationVersion",
                table: "HaproxyInstances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectiveApiVersion",
                table: "HaproxyInstances",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshAt",
                table: "HaproxyInstances",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefreshDetail",
                table: "HaproxyInstances",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefreshStatus",
                table: "HaproxyInstances",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HaproxyCertificateSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    HaproxyInstanceId = table.Column<int>(type: "int", nullable: false),
                    StorageName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    Domains = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    Issuer = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    NotBefore = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NotAfter = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyCertificateSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyCertificateSnapshots_HaproxyInstances_HaproxyInstance~",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HaproxyRouteSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    HaproxyInstanceId = table.Column<int>(type: "int", nullable: false),
                    Frontend = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Bind = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    Backend = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RawCondition = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    ConfigurationVersion = table.Column<int>(type: "int", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyRouteSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyRouteSnapshots_HaproxyInstances_HaproxyInstanceId",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HaproxyServerSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    HaproxyInstanceId = table.Column<int>(type: "int", nullable: false),
                    Backend = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Server = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    CheckStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Weight = table.Column<int>(type: "int", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyServerSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyServerSnapshots_HaproxyInstances_HaproxyInstanceId",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyCertificateSnapshots_HaproxyInstanceId_StorageName",
                table: "HaproxyCertificateSnapshots",
                columns: new[] { "HaproxyInstanceId", "StorageName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyRouteSnapshots_HaproxyInstanceId_Frontend_Host_Path_B~",
                table: "HaproxyRouteSnapshots",
                columns: new[] { "HaproxyInstanceId", "Frontend", "Host", "Path", "Backend" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyServerSnapshots_HaproxyInstanceId_Backend_Server",
                table: "HaproxyServerSnapshots",
                columns: new[] { "HaproxyInstanceId", "Backend", "Server" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaproxyCertificateSnapshots");

            migrationBuilder.DropTable(
                name: "HaproxyRouteSnapshots");

            migrationBuilder.DropTable(
                name: "HaproxyServerSnapshots");

            migrationBuilder.DropColumn(
                name: "ConfigurationVersion",
                table: "HaproxyInstances");

            migrationBuilder.DropColumn(
                name: "EffectiveApiVersion",
                table: "HaproxyInstances");

            migrationBuilder.DropColumn(
                name: "LastRefreshAt",
                table: "HaproxyInstances");

            migrationBuilder.DropColumn(
                name: "LastRefreshDetail",
                table: "HaproxyInstances");

            migrationBuilder.DropColumn(
                name: "LastRefreshStatus",
                table: "HaproxyInstances");
        }
    }
}
