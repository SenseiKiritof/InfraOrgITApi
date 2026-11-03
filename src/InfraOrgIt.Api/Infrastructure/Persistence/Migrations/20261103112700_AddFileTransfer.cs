using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileTransferCredentialId",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FileTransferPort",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileTransferProtocol",
                table: "Servers",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_FileTransferCredentialId",
                table: "Servers",
                column: "FileTransferCredentialId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Servers_FileTransferProtocol",
                table: "Servers",
                sql: "`FileTransferProtocol` IS NULL OR `FileTransferProtocol` IN ('Sftp','Ftp')");

            migrationBuilder.AddForeignKey(
                name: "FK_Servers_ServerCredentials_FileTransferCredentialId",
                table: "Servers",
                column: "FileTransferCredentialId",
                principalTable: "ServerCredentials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Servers_ServerCredentials_FileTransferCredentialId",
                table: "Servers");

            migrationBuilder.DropIndex(
                name: "IX_Servers_FileTransferCredentialId",
                table: "Servers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Servers_FileTransferProtocol",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "FileTransferCredentialId",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "FileTransferPort",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "FileTransferProtocol",
                table: "Servers");
        }
    }
}
