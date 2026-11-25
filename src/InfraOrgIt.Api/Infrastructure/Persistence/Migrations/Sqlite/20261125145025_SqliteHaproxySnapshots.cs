using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteHaproxySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfigurationVersion",
                table: "HaproxyInstances",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EffectiveApiVersion",
                table: "HaproxyInstances",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRefreshAt",
                table: "HaproxyInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefreshDetail",
                table: "HaproxyInstances",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRefreshStatus",
                table: "HaproxyInstances",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HaproxyCertificateSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HaproxyInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    StorageName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Domains = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Issuer = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NotBefore = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NotAfter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyCertificateSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyCertificateSnapshots_HaproxyInstances_HaproxyInstanceId",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HaproxyRouteSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HaproxyInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Frontend = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Bind = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Backend = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    RawCondition = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ConfigurationVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "HaproxyServerSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HaproxyInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Backend = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Server = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CheckStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Weight = table.Column<int>(type: "INTEGER", nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyCertificateSnapshots_HaproxyInstanceId_StorageName",
                table: "HaproxyCertificateSnapshots",
                columns: new[] { "HaproxyInstanceId", "StorageName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyRouteSnapshots_HaproxyInstanceId_Frontend_Host_Path_Backend",
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
