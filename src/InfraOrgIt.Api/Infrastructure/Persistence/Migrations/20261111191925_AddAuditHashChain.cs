using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditHashChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Servers_FileTransferProtocol",
                table: "Servers");

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "AuditEvents",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevHash",
                table: "AuditEvents",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Servers_FileTransferProtocol",
                table: "Servers",
                sql: "`FileTransferProtocol` IS NULL OR `FileTransferProtocol` IN ('Sftp','Ftp','Ftps')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_Hash",
                table: "AuditEvents",
                sql: "`Hash` IS NULL OR CHAR_LENGTH(`Hash`) = 64");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Servers_FileTransferProtocol",
                table: "Servers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_Hash",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "PrevHash",
                table: "AuditEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Servers_FileTransferProtocol",
                table: "Servers",
                sql: "`FileTransferProtocol` IS NULL OR `FileTransferProtocol` IN ('Sftp','Ftp')");
        }
    }
}
