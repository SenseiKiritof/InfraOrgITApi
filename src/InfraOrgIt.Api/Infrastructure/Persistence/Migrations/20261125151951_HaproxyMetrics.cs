using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HaproxyMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HaproxyMetricSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    HaproxyInstanceId = table.Column<int>(type: "int", nullable: false),
                    ResolutionMinutes = table.Column<int>(type: "int", nullable: false),
                    Bucket = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Proxy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Server = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    CheckStatus = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Req = table.Column<long>(type: "bigint", nullable: false),
                    H1xx = table.Column<long>(type: "bigint", nullable: false),
                    H2xx = table.Column<long>(type: "bigint", nullable: false),
                    H3xx = table.Column<long>(type: "bigint", nullable: false),
                    H4xx = table.Column<long>(type: "bigint", nullable: false),
                    H5xx = table.Column<long>(type: "bigint", nullable: false),
                    HOther = table.Column<long>(type: "bigint", nullable: false),
                    Bin = table.Column<long>(type: "bigint", nullable: false),
                    Bout = table.Column<long>(type: "bigint", nullable: false),
                    Econ = table.Column<long>(type: "bigint", nullable: false),
                    Eresp = table.Column<long>(type: "bigint", nullable: false),
                    Dreq = table.Column<long>(type: "bigint", nullable: false),
                    Dresp = table.Column<long>(type: "bigint", nullable: false),
                    CliAbrt = table.Column<long>(type: "bigint", nullable: false),
                    SrvAbrt = table.Column<long>(type: "bigint", nullable: false),
                    ConnTot = table.Column<long>(type: "bigint", nullable: false),
                    Dcon = table.Column<long>(type: "bigint", nullable: false),
                    Dses = table.Column<long>(type: "bigint", nullable: false),
                    Lbtot = table.Column<long>(type: "bigint", nullable: false),
                    Wretr = table.Column<long>(type: "bigint", nullable: false),
                    Wredis = table.Column<long>(type: "bigint", nullable: false),
                    Scur = table.Column<long>(type: "bigint", nullable: false),
                    Smax = table.Column<long>(type: "bigint", nullable: false),
                    Slim = table.Column<long>(type: "bigint", nullable: false),
                    Qcur = table.Column<long>(type: "bigint", nullable: false),
                    Qmax = table.Column<long>(type: "bigint", nullable: false),
                    Rate = table.Column<long>(type: "bigint", nullable: false),
                    RateMax = table.Column<long>(type: "bigint", nullable: false),
                    ReqRate = table.Column<long>(type: "bigint", nullable: false),
                    ReqRateMax = table.Column<long>(type: "bigint", nullable: false),
                    ConnRate = table.Column<long>(type: "bigint", nullable: false),
                    ConnRateMax = table.Column<long>(type: "bigint", nullable: false),
                    Rtime = table.Column<long>(type: "bigint", nullable: false),
                    Ctime = table.Column<long>(type: "bigint", nullable: false),
                    Ttime = table.Column<long>(type: "bigint", nullable: false),
                    Qtime = table.Column<long>(type: "bigint", nullable: false),
                    Downtime = table.Column<long>(type: "bigint", nullable: false),
                    Lastsess = table.Column<long>(type: "bigint", nullable: false),
                    Weight = table.Column<long>(type: "bigint", nullable: false),
                    CheckHealth = table.Column<long>(type: "bigint", nullable: false),
                    Chkfail = table.Column<long>(type: "bigint", nullable: false),
                    Chkdown = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "HaproxyProcessMetricSamples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    HaproxyInstanceId = table.Column<int>(type: "int", nullable: false),
                    ResolutionMinutes = table.Column<int>(type: "int", nullable: false),
                    Bucket = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Uptime = table.Column<long>(type: "bigint", nullable: false),
                    CurrConns = table.Column<long>(type: "bigint", nullable: false),
                    CumConns = table.Column<long>(type: "bigint", nullable: false),
                    CurrSslConns = table.Column<long>(type: "bigint", nullable: false),
                    CumSslConns = table.Column<long>(type: "bigint", nullable: false),
                    MaxConn = table.Column<long>(type: "bigint", nullable: false),
                    SessRate = table.Column<long>(type: "bigint", nullable: false),
                    ConnRate = table.Column<long>(type: "bigint", nullable: false),
                    Tasks = table.Column<long>(type: "bigint", nullable: false),
                    RunQueue = table.Column<long>(type: "bigint", nullable: false),
                    IdlePct = table.Column<long>(type: "bigint", nullable: false),
                    MaxMemoryMb = table.Column<long>(type: "bigint", nullable: false),
                    Threads = table.Column<long>(type: "bigint", nullable: false),
                    ActivePeers = table.Column<long>(type: "bigint", nullable: false),
                    ConnectedPeers = table.Column<long>(type: "bigint", nullable: false),
                    FailedResolutions = table.Column<long>(type: "bigint", nullable: false),
                    DroppedLogs = table.Column<long>(type: "bigint", nullable: false),
                    BytesOutRate = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyProcessMetricSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyProcessMetricSamples_HaproxyInstances_HaproxyInstance~",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyMetricSamples_HaproxyInstanceId_Bucket",
                table: "HaproxyMetricSamples",
                columns: new[] { "HaproxyInstanceId", "Bucket" });

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyMetricSamples_HaproxyInstanceId_ResolutionMinutes_Buc~",
                table: "HaproxyMetricSamples",
                columns: new[] { "HaproxyInstanceId", "ResolutionMinutes", "Bucket", "Kind", "Proxy", "Server" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyProcessMetricSamples_HaproxyInstanceId_ResolutionMinu~",
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
