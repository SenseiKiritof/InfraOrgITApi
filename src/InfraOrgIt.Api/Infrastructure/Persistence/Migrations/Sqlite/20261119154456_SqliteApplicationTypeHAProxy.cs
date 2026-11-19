using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SqliteApplicationTypeHAProxy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications",
                sql: "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka','MySql','MongoDb','Redis','HAProxy')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications",
                sql: "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka','MySql','MongoDb','Redis')");
        }
    }
}
