using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceLicenseAndUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "license",
                table: "sources",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_url",
                table: "sources",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // The license and origin are metadata the user recorded, not source text, so the auditor sees them.
            migrationBuilder.Sql("""
                DROP VIEW audit.sources;

                CREATE VIEW audit.sources AS
                SELECT id, user_id, title, file_name, content_type, size_bytes, status, failure_reason,
                       license, source_url, created_at_utc,
                       (SELECT count(*) FROM source_chunks c WHERE c.source_id = s.id) AS chunk_count,
                       (SELECT max(c.page_number) FROM source_chunks c WHERE c.source_id = s.id) AS last_page
                FROM sources s;

                GRANT SELECT ON audit.sources TO talk_to_rag_auditor;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW audit.sources;");

            migrationBuilder.DropColumn(
                name: "license",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "source_url",
                table: "sources");

            migrationBuilder.Sql("""
                CREATE VIEW audit.sources AS
                SELECT id, user_id, title, file_name, content_type, size_bytes, status, failure_reason,
                       created_at_utc,
                       (SELECT count(*) FROM source_chunks c WHERE c.source_id = s.id) AS chunk_count,
                       (SELECT max(c.page_number) FROM source_chunks c WHERE c.source_id = s.id) AS last_page
                FROM sources s;

                GRANT SELECT ON audit.sources TO talk_to_rag_auditor;
                """);
        }
    }
}
