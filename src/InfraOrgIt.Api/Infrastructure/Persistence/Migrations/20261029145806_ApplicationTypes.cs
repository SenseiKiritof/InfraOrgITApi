using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApplicationTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Applications",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Backend");

            // Existing frontends stay frontends; everything else becomes Backend.
            migrationBuilder.Sql(
                "UPDATE `Applications` SET `Type` = 'Frontend' WHERE `IsFrontend` = 1;");

            migrationBuilder.DropColumn(
                name: "IsFrontend",
                table: "Applications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications",
                sql: "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Applications");

            migrationBuilder.AddColumn<bool>(
                name: "IsFrontend",
                table: "Applications",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }
    }
}
