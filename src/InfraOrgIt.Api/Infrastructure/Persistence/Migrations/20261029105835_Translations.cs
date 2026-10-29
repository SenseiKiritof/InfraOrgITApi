using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Translations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Translations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EntityType = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Field = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Value = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Translations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Translations_EntityType_EntityId_Field_Language",
                table: "Translations",
                columns: new[] { "EntityType", "EntityId", "Field", "Language" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO `Translations`
                    (`EntityType`, `EntityId`, `Field`, `Language`, `Value`,
                     `CreatedAt`, `UpdatedAt`, `Version`)
                SELECT 'Menu', `Id`, 'Name', 'en',
                    CASE `Name`
                        WHEN 'Home' THEN 'Home'
                        WHEN 'Ambienti' THEN 'Environments'
                        WHEN 'Applicazioni' THEN 'Applications'
                        WHEN 'Providers' THEN 'Providers'
                        WHEN 'Servers' THEN 'Servers'
                        WHEN 'Ambiente {name}' THEN 'Environment {name}'
                        WHEN 'Applicazione {name}' THEN 'Application {name}'
                        WHEN 'Server {name}' THEN 'Server {name}'
                        WHEN 'Provider {name}' THEN 'Provider {name}'
                        WHEN 'Diagrammi' THEN 'Diagrams'
                        WHEN 'Progetti' THEN 'Projects'
                        WHEN 'Users' THEN 'Users'
                        WHEN 'Groups' THEN 'Groups'
                        WHEN 'Roles' THEN 'Roles'
                        WHEN 'Menu' THEN 'Menu'
                        WHEN 'Progetto {name}' THEN 'Project {name}'
                        WHEN 'Credenziali' THEN 'Credentials'
                        WHEN 'Comandi' THEN 'Commands'
                        WHEN 'Test di rete' THEN 'Network tests'
                        WHEN 'Registro esecuzioni' THEN 'Execution log'
                        WHEN 'Registro admin' THEN 'Admin log'
                        ELSE `Name`
                    END,
                    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0
                FROM `Menus`;
                """);

            migrationBuilder.Sql("""
                INSERT INTO `Translations`
                    (`EntityType`, `EntityId`, `Field`, `Language`, `Value`,
                     `CreatedAt`, `UpdatedAt`, `Version`)
                SELECT 'Role', `Id`, 'Label', 'en',
                    CASE `Label`
                        WHEN 'Amministratore server' THEN 'Server administrator'
                        WHEN 'Operatore server' THEN 'Server operator'
                        WHEN 'Amministratore di sistema' THEN 'System administrator'
                        ELSE `Label`
                    END,
                    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 0
                FROM `Roles`;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Translations");
        }
    }
}
