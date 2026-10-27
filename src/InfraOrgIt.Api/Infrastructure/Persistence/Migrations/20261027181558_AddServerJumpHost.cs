using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServerJumpHost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "JumpServerId",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_JumpServerId",
                table: "Servers",
                column: "JumpServerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Servers_Servers_JumpServerId",
                table: "Servers",
                column: "JumpServerId",
                principalTable: "Servers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Servers_Servers_JumpServerId",
                table: "Servers");

            migrationBuilder.DropIndex(
                name: "IX_Servers_JumpServerId",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "JumpServerId",
                table: "Servers");
        }
    }
}
