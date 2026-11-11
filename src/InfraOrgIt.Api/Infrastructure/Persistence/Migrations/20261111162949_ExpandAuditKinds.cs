using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandAuditKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents",
                sql: "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError','FileTransfer','AdminChange')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditEvents_Kind",
                table: "AuditEvents",
                sql: "`Kind` IN ('McpToolCall','TunnelStart','TunnelStop','TunnelError')");
        }
    }
}
