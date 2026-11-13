using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These two views read columns that are dropped below, so they are rebuilt without them.
            migrationBuilder.Sql("DROP VIEW audit.sources; DROP VIEW audit.messages;");

            migrationBuilder.DropTable(
                name: "conversation_notebooks");

            migrationBuilder.DropTable(
                name: "conversation_tags");

            migrationBuilder.DropColumn(
                name: "license",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "source_url",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "token_count",
                table: "source_chunks");

            migrationBuilder.DropColumn(
                name: "audio_blob_key",
                table: "messages");

            migrationBuilder.Sql("""
                CREATE VIEW audit.sources AS
                SELECT id, user_id, title, file_name, content_type, size_bytes, status, failure_reason,
                       created_at_utc,
                       (SELECT count(*) FROM source_chunks c WHERE c.source_id = s.id) AS chunk_count,
                       (SELECT max(c.page_number) FROM source_chunks c WHERE c.source_id = s.id) AS last_page
                FROM sources s;

                CREATE VIEW audit.messages AS
                SELECT m.id, m.conversation_id, c.user_id, m.role,
                       CASE WHEN m.role = 'User' THEN m.content END AS content,
                       m.created_at_utc,
                       length(m.content) AS characters
                FROM messages m
                JOIN conversations c ON c.id = m.conversation_id;

                GRANT SELECT ON audit.sources, audit.messages TO talk_to_rag_auditor;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW audit.sources; DROP VIEW audit.messages;");

            migrationBuilder.AddColumn<string>(
                name: "license",
                table: "sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_url",
                table: "sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "token_count",
                table: "source_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audio_blob_key",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversation_notebooks",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notebook_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_notebooks", x => new { x.conversation_id, x.notebook_id });
                    table.ForeignKey(
                        name: "fk_conversation_notebooks_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_notebooks_notebooks_notebook_id",
                        column: x => x.notebook_id,
                        principalTable: "notebooks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_tags",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_tags", x => new { x.conversation_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_conversation_tags_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_notebooks_notebook_id",
                table: "conversation_notebooks",
                column: "notebook_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_tags_tag_id",
                table: "conversation_tags",
                column: "tag_id");

            migrationBuilder.Sql("""
                CREATE VIEW audit.sources AS
                SELECT id, user_id, title, file_name, content_type, size_bytes, status, failure_reason,
                       license, source_url, created_at_utc,
                       (SELECT count(*) FROM source_chunks c WHERE c.source_id = s.id) AS chunk_count,
                       (SELECT max(c.page_number) FROM source_chunks c WHERE c.source_id = s.id) AS last_page
                FROM sources s;

                CREATE VIEW audit.messages AS
                SELECT m.id, m.conversation_id, c.user_id, m.role,
                       CASE WHEN m.role = 'User' THEN m.content END AS content,
                       m.created_at_utc,
                       (m.audio_blob_key IS NOT NULL) AS spoken,
                       length(m.content) AS characters
                FROM messages m
                JOIN conversations c ON c.id = m.conversation_id;

                GRANT SELECT ON audit.sources, audit.messages TO talk_to_rag_auditor;
                """);
        }
    }
}
