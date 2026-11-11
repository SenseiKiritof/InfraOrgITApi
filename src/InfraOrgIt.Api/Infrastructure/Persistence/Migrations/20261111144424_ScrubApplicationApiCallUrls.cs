using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfraOrgIt.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScrubApplicationApiCallUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PT-02: rows saved before the redaction fix can contain a decrypted
            // query API key in the URL. Drop the whole query string: the key is
            // no longer recoverable and the call remains identifiable by path.
            migrationBuilder.Sql("""
                UPDATE `ApplicationApiCalls`
                SET `Url` = SUBSTRING_INDEX(`Url`, '?', 1)
                WHERE `Url` LIKE '%?%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The removed query strings cannot be restored.
        }
    }
}
