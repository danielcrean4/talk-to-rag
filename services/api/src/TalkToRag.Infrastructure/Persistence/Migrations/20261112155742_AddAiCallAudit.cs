using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCallAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_calls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_trace_id = table.Column<string>(type: "text", nullable: true),
                    operation = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    request = table.Column<string>(type: "jsonb", nullable: false),
                    response = table.Column<string>(type: "jsonb", nullable: true),
                    request_redacted = table.Column<string>(type: "jsonb", nullable: false),
                    response_redacted = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_calls", x => x.id);
                    table.ForeignKey(
                        name: "fk_ai_calls_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_calls_created_at_utc",
                table: "ai_calls",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_ai_calls_operation_created_at_utc",
                table: "ai_calls",
                columns: new[] { "operation", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_calls_user_id",
                table: "ai_calls",
                column: "user_id");

            // What an outside auditor may read: the audit schema only, never the tables.
            // Source text is left out of every view; passage text inside prompts is already
            // replaced by placeholders in the *_redacted columns.
            migrationBuilder.Sql("""
                CREATE SCHEMA IF NOT EXISTS audit;

                CREATE VIEW audit.ai_calls AS
                SELECT id, created_at_utc, user_id, request_trace_id, operation, status_code, duration_ms,
                       request_redacted AS request, response_redacted AS response, error
                FROM ai_calls;

                CREATE VIEW audit.model_calls AS
                SELECT c.id AS ai_call_id, c.created_at_utc, c.user_id, c.operation,
                       t.call->>'kind' AS kind,
                       t.call->>'model' AS model,
                       t.call->>'served_model' AS served_model,
                       (t.call->>'duration_ms')::int AS duration_ms,
                       t.call->'messages' AS messages,
                       t.call->>'completion' AS completion,
                       t.call->>'error' AS error
                FROM ai_calls c
                CROSS JOIN LATERAL jsonb_array_elements(
                    CASE WHEN jsonb_typeof(c.response_redacted->'trace') = 'array'
                         THEN c.response_redacted->'trace' ELSE '[]'::jsonb END) AS t(call);

                CREATE VIEW audit.sources AS
                SELECT s.id, s.user_id, s.title, s.file_name, s.content_type, s.size_bytes, s.status, s.failure_reason,
                       s.license, s.source_url, s.created_at_utc,
                       (SELECT count(*) FROM source_chunks c WHERE c.source_id = s.id) AS chunk_count,
                       (SELECT max(c.page_number) FROM source_chunks c WHERE c.source_id = s.id) AS last_page
                FROM sources s;

                CREATE VIEW audit.chunks AS
                SELECT id, source_id, page_number, page_label, section, chunk_index, embedding_model,
                       length(content) AS characters
                FROM source_chunks;

                CREATE VIEW audit.messages AS
                SELECT m.id, m.conversation_id, c.user_id, m.role, m.content, m.created_at_utc,
                       (m.audio_blob_key IS NOT NULL) AS spoken
                FROM messages m
                JOIN conversations c ON c.id = m.conversation_id;

                CREATE VIEW audit.citations AS
                SELECT mc.message_id, mc.number AS rank, ch.source_id, s.title AS source_title,
                       ch.page_number, ch.page_label, ch.section, ch.chunk_index
                FROM message_citations mc
                JOIN source_chunks ch ON ch.id = mc.source_chunk_id
                JOIN sources s ON s.id = ch.source_id;

                CREATE VIEW audit.concepts AS
                SELECT id, user_id, name, summary, state, proposed_by_model, created_at_utc FROM concepts;

                CREATE VIEW audit.concept_prerequisites AS
                SELECT concept_id, prerequisite_id, state, created_at_utc FROM concept_prerequisites;

                CREATE VIEW audit.concept_passages AS
                SELECT id, concept_id, source_id, page_number, page_label, section, created_at_utc FROM concept_passages;

                CREATE VIEW audit.questions AS
                SELECT id, user_id, concept_id, prompt, answer, generated_by_model, source_id, page_number, page_label,
                       section, created_at_utc
                FROM questions;

                CREATE VIEW audit.attempts AS
                SELECT id, user_id, concept_id, question_id, platform, score, answered_at_utc FROM attempts;

                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'talk_to_rag_auditor') THEN
                        -- A development password, like the one in compose.yaml; the database listens on 127.0.0.1 only.
                        CREATE ROLE talk_to_rag_auditor LOGIN PASSWORD 'auditor-local-only';
                    END IF;
                END
                $$;

                ALTER ROLE talk_to_rag_auditor SET default_transaction_read_only = on;
                ALTER ROLE talk_to_rag_auditor SET statement_timeout = '10s';
                GRANT USAGE ON SCHEMA audit TO talk_to_rag_auditor;
                GRANT SELECT ON ALL TABLES IN SCHEMA audit TO talk_to_rag_auditor;
                ALTER DEFAULT PRIVILEGES IN SCHEMA audit GRANT SELECT ON TABLES TO talk_to_rag_auditor;

                -- The read-only setting above can be switched off by the session, so the grants are
                -- what holds: without this, any role may still create temporary tables.
                DO $$
                BEGIN
                    EXECUTE format('REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC', current_database());
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA audit REVOKE SELECT ON TABLES FROM talk_to_rag_auditor;
                DO $$
                BEGIN
                    EXECUTE format('GRANT TEMPORARY ON DATABASE %I TO PUBLIC', current_database());
                END
                $$;
                DROP SCHEMA IF EXISTS audit CASCADE;
                DROP ROLE IF EXISTS talk_to_rag_auditor;
                """);

            migrationBuilder.DropTable(
                name: "ai_calls");
        }
    }
}
