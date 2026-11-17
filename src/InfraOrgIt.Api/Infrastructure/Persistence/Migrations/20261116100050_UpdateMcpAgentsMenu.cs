using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMcpAgentsMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE `Menus`
                SET `Name` = 'Agenti MCP',
                    `UpdatedAt` = UTC_TIMESTAMP(6),
                    `Version` = `Version` + 1
                WHERE `Id` = 25 AND `Name` = 'Approvazioni';
                """);

            migrationBuilder.Sql("""
                INSERT INTO `Translations`
                    (`EntityType`, `EntityId`, `Field`, `Language`, `Value`,
                     `CreatedAt`, `UpdatedAt`, `Version`)
                SELECT 'Menu', 25, 'Name', 'en', 'MCP agents',
                    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0
                WHERE NOT EXISTS (
                    SELECT 1 FROM `Translations`
                    WHERE `EntityType` = 'Menu' AND `EntityId` = 25
                      AND `Field` = 'Name' AND `Language` = 'en');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM `Translations`
                WHERE `EntityType` = 'Menu' AND `EntityId` = 25
                  AND `Field` = 'Name' AND `Language` = 'en'
                  AND `Value` = 'MCP agents';
                """);

            migrationBuilder.Sql("""
                UPDATE `Menus`
                SET `Name` = 'Approvazioni',
                    `UpdatedAt` = UTC_TIMESTAMP(6),
                    `Version` = `Version` + 1
                WHERE `Id` = 25 AND `Name` = 'Agenti MCP';
                """);
        }
    }
}
