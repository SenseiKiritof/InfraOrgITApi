using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HaproxyClusters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClusterId",
                table: "HaproxyInstances",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HaproxyClusters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ProviderEnvironmentId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Mode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Vip = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaproxyClusters", x => x.Id);
                    table.CheckConstraint("CK_HaproxyClusters_Mode", "`Mode` IN ('active-active','active-passive')");
                    table.ForeignKey(
                        name: "FK_HaproxyClusters_ProviderEnvironments_ProviderEnvironmentId",
                        column: x => x.ProviderEnvironmentId,
                        principalTable: "ProviderEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyInstances_ClusterId",
                table: "HaproxyInstances",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_HaproxyClusters_ProviderEnvironmentId_Name",
                table: "HaproxyClusters",
                columns: new[] { "ProviderEnvironmentId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HaproxyInstances_HaproxyClusters_ClusterId",
                table: "HaproxyInstances",
                column: "ClusterId",
                principalTable: "HaproxyClusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HaproxyInstances_HaproxyClusters_ClusterId",
                table: "HaproxyInstances");

            migrationBuilder.DropTable(
                name: "HaproxyClusters");

            migrationBuilder.DropIndex(
                name: "IX_HaproxyInstances_ClusterId",
                table: "HaproxyInstances");

            migrationBuilder.DropColumn(
                name: "ClusterId",
                table: "HaproxyInstances");
        }
    }
}
