using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WithholdModelTextFromAuditViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Text a model wrote from passages can quote them, so these views show its length only.
            // The text itself, with quotes replaced, is in audit.ai_calls, audit.model_calls and audit.answers.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW audit.messages AS
                SELECT m.id, m.conversation_id, c.user_id, m.role,
                       CASE WHEN m.role = 'User' THEN m.content END AS content,
                       m.created_at_utc,
                       (m.audio_blob_key IS NOT NULL) AS spoken,
                       length(m.content) AS characters
                FROM messages m
                JOIN conversations c ON c.id = m.conversation_id;

                CREATE OR REPLACE VIEW audit.questions AS
                SELECT id, user_id, concept_id,
                       CASE WHEN generated_by_model IS NULL THEN prompt END AS prompt,
                       CASE WHEN generated_by_model IS NULL THEN answer END AS answer,
                       generated_by_model, source_id, page_number, page_label, section, created_at_utc,
                       length(prompt) AS prompt_characters, length(answer) AS answer_characters
                FROM questions;

                CREATE VIEW audit.answers AS
                SELECT id AS ai_call_id, created_at_utc, user_id, operation, duration_ms,
                       request_redacted->>'question' AS question,
                       jsonb_array_length(request_redacted->'passages') AS passages,
                       response_redacted->>'answer' AS answer,
                       coalesce(response_redacted->'cited', response_redacted->'events'->-1->'cited') AS cited
                FROM ai_calls
                WHERE operation LIKE '/v1/rag/answer%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP VIEW audit.answers;
                DROP VIEW audit.questions;
                DROP VIEW audit.messages;

                CREATE VIEW audit.messages AS
                SELECT m.id, m.conversation_id, c.user_id, m.role, m.content, m.created_at_utc,
                       (m.audio_blob_key IS NOT NULL) AS spoken
                FROM messages m
                JOIN conversations c ON c.id = m.conversation_id;

                CREATE VIEW audit.questions AS
                SELECT id, user_id, concept_id, prompt, answer, generated_by_model, source_id, page_number, page_label,
                       section, created_at_utc
                FROM questions;
                """);
        }
    }
}
