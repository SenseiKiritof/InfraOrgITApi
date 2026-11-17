using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SudoCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptedSudoSecret",
                table: "ServerCredentials",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SudoKeyVersion",
                table: "ServerCredentials",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresSudo",
                table: "CommandTemplates",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresSudo",
                table: "CommandExecutions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresSudo",
                table: "ApplicationActions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EncryptedSudoSecret",
                table: "ServerCredentials");

            migrationBuilder.DropColumn(
                name: "SudoKeyVersion",
                table: "ServerCredentials");

            migrationBuilder.DropColumn(
                name: "RequiresSudo",
                table: "CommandTemplates");

            migrationBuilder.DropColumn(
                name: "RequiresSudo",
                table: "CommandExecutions");

            migrationBuilder.DropColumn(
                name: "RequiresSudo",
                table: "ApplicationActions");
        }
    }
}
