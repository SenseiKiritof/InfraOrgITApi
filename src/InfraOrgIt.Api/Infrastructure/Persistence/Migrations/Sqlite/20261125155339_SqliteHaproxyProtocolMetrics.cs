using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteHaproxyProtocolMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatsBaseUrl",
                table: "HaproxyInstances",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HaproxyProtocolMetricSamples",
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
                    H2Req = table.Column<long>(type: "INTEGER", nullable: false),
                    H2Sess = table.Column<long>(type: "INTEGER", nullable: false),
                    H3Req = table.Column<long>(type: "INTEGER", nullable: false),
                    H3Sess = table.Column<long>(type: "INTEGER", nullable: false),
                    H2OpenConnections = table.Column<long>(type: "INTEGER", nullable: false),
                    H2BackendOpenStreams = table.Column<long>(type: "INTEGER", nullable: false),
                    H2ConnErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    H2StreamErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicHandshakeFail = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicDroppedPkt = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicLostPkt = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicSentPkt = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicRetryError = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicTooShortDgram = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicRxbufFull = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicSocketFull = table.Column<long>(type: "INTEGER", nullable: false),
                    H3VersionFallback = table.Column<long>(type: "INTEGER", nullable: false),
                    H3ProtocolErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    QuicTransportErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    QpackEncoderErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    QpackDecoderErrors = table.Column<long>(type: "INTEGER", nullable: false),
                    SslFailedHandshake = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyProtocolMetricSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyProtocolMetricSamples_HaproxyInstances_HaproxyInstanceId",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyProtocolMetricSamples_HaproxyInstanceId_ResolutionMinutes_Bucket_Kind_Proxy_Server",
                table: "HaproxyProtocolMetricSamples",
                columns: new[] { "HaproxyInstanceId", "ResolutionMinutes", "Bucket", "Kind", "Proxy", "Server" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaproxyProtocolMetricSamples");

            migrationBuilder.DropColumn(
                name: "StatsBaseUrl",
                table: "HaproxyInstances");
        }
    }
}
