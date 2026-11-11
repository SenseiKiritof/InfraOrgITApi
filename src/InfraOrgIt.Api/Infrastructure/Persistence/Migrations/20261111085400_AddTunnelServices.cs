using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTunnelServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TargetPort",
                table: "TunnelSessions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "LocalPort",
                table: "TunnelSessions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "TunnelSessions",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<int>(
                name: "RemotePort",
                table: "TunnelSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TunnelServiceId",
                table: "TunnelSessions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TunnelServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ServerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Mode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    TargetHost = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, defaultValue: "127.0.0.1"),
                    TargetPort = table.Column<int>(type: "int", nullable: true),
                    LocalPort = table.Column<int>(type: "int", nullable: true),
                    RemotePort = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, defaultValue: "Tcp"),
                    OpenPath = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    AutoStart = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TunnelServices", x => x.Id);
                    table.CheckConstraint("CK_TunnelServices_Kind", "`Kind` IN ('Tcp','Http','Https')");
                    table.CheckConstraint("CK_TunnelServices_Mode", "`Mode` IN ('Local','Dynamic','Remote')");
                    table.ForeignKey(
                        name: "FK_TunnelServices_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_ServerApplicationId",
                table: "TunnelSessions",
                column: "ServerApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelSessions_TunnelServiceId",
                table: "TunnelSessions",
                column: "TunnelServiceId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TunnelSessions_Mode",
                table: "TunnelSessions",
                sql: "`Mode` IN ('Local','Dynamic','Remote')");

            migrationBuilder.CreateIndex(
                name: "IX_TunnelServices_ServerId_Name",
                table: "TunnelServices",
                columns: new[] { "ServerId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TunnelSessions_TunnelServices_TunnelServiceId",
                table: "TunnelSessions",
                column: "TunnelServiceId",
                principalTable: "TunnelServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TunnelSessions_TunnelServices_TunnelServiceId",
                table: "TunnelSessions");

            migrationBuilder.DropTable(
                name: "TunnelServices");

            migrationBuilder.DropIndex(
                name: "IX_TunnelSessions_ServerApplicationId",
                table: "TunnelSessions");

            migrationBuilder.DropIndex(
                name: "IX_TunnelSessions_TunnelServiceId",
                table: "TunnelSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TunnelSessions_Mode",
                table: "TunnelSessions");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "TunnelSessions");

            migrationBuilder.DropColumn(
                name: "RemotePort",
                table: "TunnelSessions");

            migrationBuilder.DropColumn(
                name: "TunnelServiceId",
                table: "TunnelSessions");

            migrationBuilder.AlterColumn<int>(
                name: "TargetPort",
                table: "TunnelSessions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LocalPort",
                table: "TunnelSessions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
