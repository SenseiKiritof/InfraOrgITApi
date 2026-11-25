using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteHaproxyMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HaproxyMetricSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HaproxyInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolutionMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Bucket = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Proxy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Server = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CheckStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Req = table.Column<long>(type: "INTEGER", nullable: false),
                    H1xx = table.Column<long>(type: "INTEGER", nullable: false),
                    H2xx = table.Column<long>(type: "INTEGER", nullable: false),
                    H3xx = table.Column<long>(type: "INTEGER", nullable: false),
                    H4xx = table.Column<long>(type: "INTEGER", nullable: false),
                    H5xx = table.Column<long>(type: "INTEGER", nullable: false),
                    HOther = table.Column<long>(type: "INTEGER", nullable: false),
                    Bin = table.Column<long>(type: "INTEGER", nullable: false),
                    Bout = table.Column<long>(type: "INTEGER", nullable: false),
                    Econ = table.Column<long>(type: "INTEGER", nullable: false),
                    Eresp = table.Column<long>(type: "INTEGER", nullable: false),
                    Dreq = table.Column<long>(type: "INTEGER", nullable: false),
                    Dresp = table.Column<long>(type: "INTEGER", nullable: false),
                    CliAbrt = table.Column<long>(type: "INTEGER", nullable: false),
                    SrvAbrt = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnTot = table.Column<long>(type: "INTEGER", nullable: false),
                    Dcon = table.Column<long>(type: "INTEGER", nullable: false),
                    Dses = table.Column<long>(type: "INTEGER", nullable: false),
                    Lbtot = table.Column<long>(type: "INTEGER", nullable: false),
                    Wretr = table.Column<long>(type: "INTEGER", nullable: false),
                    Wredis = table.Column<long>(type: "INTEGER", nullable: false),
                    Scur = table.Column<long>(type: "INTEGER", nullable: false),
                    Smax = table.Column<long>(type: "INTEGER", nullable: false),
                    Slim = table.Column<long>(type: "INTEGER", nullable: false),
                    Qcur = table.Column<long>(type: "INTEGER", nullable: false),
                    Qmax = table.Column<long>(type: "INTEGER", nullable: false),
                    Rate = table.Column<long>(type: "INTEGER", nullable: false),
                    RateMax = table.Column<long>(type: "INTEGER", nullable: false),
                    ReqRate = table.Column<long>(type: "INTEGER", nullable: false),
                    ReqRateMax = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnRate = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnRateMax = table.Column<long>(type: "INTEGER", nullable: false),
                    Rtime = table.Column<long>(type: "INTEGER", nullable: false),
                    Ctime = table.Column<long>(type: "INTEGER", nullable: false),
                    Ttime = table.Column<long>(type: "INTEGER", nullable: false),
                    Qtime = table.Column<long>(type: "INTEGER", nullable: false),
                    Downtime = table.Column<long>(type: "INTEGER", nullable: false),
                    Lastsess = table.Column<long>(type: "INTEGER", nullable: false),
                    Weight = table.Column<long>(type: "INTEGER", nullable: false),
                    CheckHealth = table.Column<long>(type: "INTEGER", nullable: false),
                    Chkfail = table.Column<long>(type: "INTEGER", nullable: false),
                    Chkdown = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyMetricSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyMetricSamples_HaproxyInstances_HaproxyInstanceId",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HaproxyProcessMetricSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HaproxyInstanceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolutionMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    Bucket = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Uptime = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrConns = table.Column<long>(type: "INTEGER", nullable: false),
                    CumConns = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrSslConns = table.Column<long>(type: "INTEGER", nullable: false),
                    CumSslConns = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxConn = table.Column<long>(type: "INTEGER", nullable: false),
                    SessRate = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnRate = table.Column<long>(type: "INTEGER", nullable: false),
                    Tasks = table.Column<long>(type: "INTEGER", nullable: false),
                    RunQueue = table.Column<long>(type: "INTEGER", nullable: false),
                    IdlePct = table.Column<long>(type: "INTEGER", nullable: false),
                    MaxMemoryMb = table.Column<long>(type: "INTEGER", nullable: false),
                    Threads = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivePeers = table.Column<long>(type: "INTEGER", nullable: false),
                    ConnectedPeers = table.Column<long>(type: "INTEGER", nullable: false),
                    FailedResolutions = table.Column<long>(type: "INTEGER", nullable: false),
                    DroppedLogs = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesOutRate = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyProcessMetricSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyProcessMetricSamples_HaproxyInstances_HaproxyInstanceId",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyMetricSamples_HaproxyInstanceId_Bucket",
                table: "HaproxyMetricSamples",
                columns: new[] { "HaproxyInstanceId", "Bucket" });

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyMetricSamples_HaproxyInstanceId_ResolutionMinutes_Bucket_Kind_Proxy_Server",
                table: "HaproxyMetricSamples",
                columns: new[] { "HaproxyInstanceId", "ResolutionMinutes", "Bucket", "Kind", "Proxy", "Server" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyProcessMetricSamples_HaproxyInstanceId_ResolutionMinutes_Bucket",
                table: "HaproxyProcessMetricSamples",
                columns: new[] { "HaproxyInstanceId", "ResolutionMinutes", "Bucket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaproxyMetricSamples");

            migrationBuilder.DropTable(
                name: "HaproxyProcessMetricSamples");
        }
    }
}
