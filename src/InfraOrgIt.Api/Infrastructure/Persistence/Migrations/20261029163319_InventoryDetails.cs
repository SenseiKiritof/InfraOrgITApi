using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications");

            migrationBuilder.AddColumn<int>(
                name: "CpuCores",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiskGb",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HaNode",
                table: "Servers",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagedBy",
                table: "Servers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MemoryGb",
                table: "Servers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "Servers",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicIpAddress",
                table: "Servers",
                type: "varchar(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComposeDirectory",
                table: "ServerApplications",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContainerImage",
                table: "ServerApplications",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContainerName",
                table: "ServerApplications",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HaRole",
                table: "ServerApplications",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HostPort",
                table: "ServerApplications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Runtime",
                table: "ServerApplications",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ListenPort",
                table: "ProxyRoutes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VirtualHost",
                table: "ProxyRoutes",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServerApplications_HaRole",
                table: "ServerApplications",
                sql: "`HaRole` IN ('Primary','Secondary')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServerApplications_Runtime",
                table: "ServerApplications",
                sql: "`Runtime` IN ('Container','Native')");

            // The foreign key on ProxyConnectionId uses this index: create its
            // replacement (which also starts with ProxyConnectionId) before
            // dropping the old one, otherwise MySQL refuses the gap.
            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_ProxyConnectionId_VirtualHost_ListenPort_Route",
                table: "ProxyRoutes",
                columns: new[] { "ProxyConnectionId", "VirtualHost", "ListenPort", "Route" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_ProxyRoutes_ProxyConnectionId_Route",
                table: "ProxyRoutes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications",
                sql: "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka','MySql','MongoDb','Redis')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ServerApplications_HaRole",
                table: "ServerApplications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServerApplications_Runtime",
                table: "ServerApplications");

            // Same reason as Up: keep an index for the foreign key at all times.
            migrationBuilder.CreateIndex(
                name: "IX_ProxyRoutes_ProxyConnectionId_Route",
                table: "ProxyRoutes",
                columns: new[] { "ProxyConnectionId", "Route" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_ProxyRoutes_ProxyConnectionId_VirtualHost_ListenPort_Route",
                table: "ProxyRoutes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "CpuCores",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "DiskGb",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "HaNode",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "ManagedBy",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "MemoryGb",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "PublicIpAddress",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "ComposeDirectory",
                table: "ServerApplications");

            migrationBuilder.DropColumn(
                name: "ContainerImage",
                table: "ServerApplications");

            migrationBuilder.DropColumn(
                name: "ContainerName",
                table: "ServerApplications");

            migrationBuilder.DropColumn(
                name: "HaRole",
                table: "ServerApplications");

            migrationBuilder.DropColumn(
                name: "HostPort",
                table: "ServerApplications");

            migrationBuilder.DropColumn(
                name: "Runtime",
                table: "ServerApplications");

            migrationBuilder.DropColumn(
                name: "ListenPort",
                table: "ProxyRoutes");

            migrationBuilder.DropColumn(
                name: "VirtualHost",
                table: "ProxyRoutes");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Applications_Type",
                table: "Applications",
                sql: "`Type` IN ('Backend','Frontend','Sftp','RabbitMq','Nginx','Elastic','Kibana','Kafka')");
        }
    }
}
