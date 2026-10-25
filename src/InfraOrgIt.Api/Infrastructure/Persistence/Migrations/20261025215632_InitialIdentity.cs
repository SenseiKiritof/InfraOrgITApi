using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Menus",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    Path = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HideBackButton = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menus", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menus_Menus_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ScopeEnum = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Surname = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MenuRole",
                columns: table => new
                {
                    MenuId = table.Column<int>(type: "int", nullable: false),
                    RolesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuRole", x => new { x.MenuId, x.RolesId });
                    table.ForeignKey(
                        name: "FK_MenuRole_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuRole_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GroupUser",
                columns: table => new
                {
                    GroupsId = table.Column<int>(type: "int", nullable: false),
                    UsersId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupUser", x => new { x.GroupsId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_GroupUser_Groups_GroupsId",
                        column: x => x.GroupsId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupUser_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RoleUser",
                columns: table => new
                {
                    RolesId = table.Column<int>(type: "int", nullable: false),
                    UsersId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleUser", x => new { x.RolesId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_RoleUser_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoleUser_Users_UsersId",
                        column: x => x.UsersId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Menus",
                columns: new[] { "Id", "CreatedAt", "HideBackButton", "IsActive", "IsVisible", "Name", "Order", "ParentId", "Path", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Home", 0, null, "/", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 2, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Ambienti", 1, null, "/environments", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 3, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Applicazioni", 1, null, "/applications", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 4, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Providers", 1, null, "/providers", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 5, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Servers", 1, null, "/servers", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 10, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Diagrammi", 10, null, "/diagrams", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 12, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Progetti", 30, null, "/projects", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 13, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Users", 40, null, "/users", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 14, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Groups", 50, null, "/groups", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 15, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Roles", 60, null, "/roles", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 16, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Menu", 70, null, "/menu", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 19, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Credenziali", 80, null, "/credentials", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 20, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Comandi", 81, null, "/commands", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 21, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Test di rete", 82, null, "/reachability", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 22, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), true, true, true, "Registro esecuzioni", 83, null, "/executions", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedAt", "Label", "Name", "ScopeEnum", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Amministratore server", "ServerAdmin", "Environment", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 2, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Operatore server", "ServerOperator", "Environment", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 3, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), "Amministratore di sistema", "SystemAdmin", "System", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.InsertData(
                table: "MenuRole",
                columns: new[] { "MenuId", "RolesId" },
                values: new object[,]
                {
                    { 13, 3 },
                    { 14, 3 },
                    { 15, 3 },
                    { 16, 3 },
                    { 19, 3 },
                    { 20, 3 },
                    { 21, 3 },
                    { 22, 3 }
                });

            migrationBuilder.InsertData(
                table: "Menus",
                columns: new[] { "Id", "CreatedAt", "HideBackButton", "IsActive", "IsVisible", "Name", "Order", "ParentId", "Path", "UpdatedAt", "Version" },
                values: new object[,]
                {
                    { 6, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Ambiente {name}", 0, 2, "/environments/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 7, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Applicazione {name}", 0, 3, "/applications/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 8, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Server {name}", 0, 5, "/servers/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 9, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Provider {name}", 0, 4, "/providers/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 },
                    { 18, new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), false, true, false, "Progetto {name}", 0, 12, "/projects/{id}", new DateTime(2026, 10, 25, 0, 0, 0, 0, DateTimeKind.Utc), 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupUser_UsersId",
                table: "GroupUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuRole_RolesId",
                table: "MenuRole",
                column: "RolesId");

            migrationBuilder.CreateIndex(
                name: "IX_Menus_ParentId",
                table: "Menus",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleUser_UsersId",
                table: "RoleUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupUser");

            migrationBuilder.DropTable(
                name: "MenuRole");

            migrationBuilder.DropTable(
                name: "RoleUser");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "Menus");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
