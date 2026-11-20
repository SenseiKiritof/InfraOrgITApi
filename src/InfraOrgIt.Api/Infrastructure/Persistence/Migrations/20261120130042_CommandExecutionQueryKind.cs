using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommandExecutionQueryKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CommandExecutions_Kind",
                table: "CommandExecutions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommandExecutions_Kind",
                table: "CommandExecutions",
                sql: "`Kind` IN ('Action','Reachability','Console','Query')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CommandExecutions_Kind",
                table: "CommandExecutions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CommandExecutions_Kind",
                table: "CommandExecutions",
                sql: "`Kind` IN ('Action','Reachability','Console')");
        }
    }
}
