using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HaproxyProtocolMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatsBaseUrl",
                table: "HaproxyInstances",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HaproxyProtocolMetricSamples",
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
                    H2Req = table.Column<long>(type: "bigint", nullable: false),
                    H2Sess = table.Column<long>(type: "bigint", nullable: false),
                    H3Req = table.Column<long>(type: "bigint", nullable: false),
                    H3Sess = table.Column<long>(type: "bigint", nullable: false),
                    H2OpenConnections = table.Column<long>(type: "bigint", nullable: false),
                    H2BackendOpenStreams = table.Column<long>(type: "bigint", nullable: false),
                    H2ConnErrors = table.Column<long>(type: "bigint", nullable: false),
                    H2StreamErrors = table.Column<long>(type: "bigint", nullable: false),
                    QuicHandshakeFail = table.Column<long>(type: "bigint", nullable: false),
                    QuicDroppedPkt = table.Column<long>(type: "bigint", nullable: false),
                    QuicLostPkt = table.Column<long>(type: "bigint", nullable: false),
                    QuicSentPkt = table.Column<long>(type: "bigint", nullable: false),
                    QuicRetryError = table.Column<long>(type: "bigint", nullable: false),
                    QuicTooShortDgram = table.Column<long>(type: "bigint", nullable: false),
                    QuicRxbufFull = table.Column<long>(type: "bigint", nullable: false),
                    QuicSocketFull = table.Column<long>(type: "bigint", nullable: false),
                    H3VersionFallback = table.Column<long>(type: "bigint", nullable: false),
                    H3ProtocolErrors = table.Column<long>(type: "bigint", nullable: false),
                    QuicTransportErrors = table.Column<long>(type: "bigint", nullable: false),
                    QpackEncoderErrors = table.Column<long>(type: "bigint", nullable: false),
                    QpackDecoderErrors = table.Column<long>(type: "bigint", nullable: false),
                    SslFailedHandshake = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyProtocolMetricSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaproxyProtocolMetricSamples_HaproxyInstances_HaproxyInstanc~",
                        column: x => x.HaproxyInstanceId,
                        principalTable: "HaproxyInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyProtocolMetricSamples_HaproxyInstanceId_ResolutionMin~",
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
