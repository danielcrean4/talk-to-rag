using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KeepCitationsAcrossReingest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW audit.citations;");

            migrationBuilder.DropForeignKey(
                name: "fk_message_citations_source_chunks_source_chunk_id",
                table: "message_citations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_message_citations",
                table: "message_citations");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_chunk_id",
                table: "message_citations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<int>(
                name: "chunk_index",
                table: "message_citations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "content",
                table: "message_citations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "page_label",
                table: "message_citations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "page_number",
                table: "message_citations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "section",
                table: "message_citations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_id",
                table: "message_citations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("""
                UPDATE message_citations mc
                SET source_id = ch.source_id,
                    page_number = ch.page_number,
                    page_label = ch.page_label,
                    section = ch.section,
                    chunk_index = ch.chunk_index,
                    content = ch.content
                FROM source_chunks ch
                WHERE ch.id = mc.source_chunk_id;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "pk_message_citations",
                table: "message_citations",
                columns: new[] { "message_id", "number" });

            migrationBuilder.CreateIndex(
                name: "ix_message_citations_source_id",
                table: "message_citations",
                column: "source_id");

            migrationBuilder.AddForeignKey(
                name: "fk_message_citations_source_chunks_source_chunk_id",
                table: "message_citations",
                column: "source_chunk_id",
                principalTable: "source_chunks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_message_citations_sources_source_id",
                table: "message_citations",
                column: "source_id",
                principalTable: "sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                CREATE VIEW audit.citations AS
                SELECT mc.message_id, mc.number AS rank, mc.source_id, s.title AS source_title,
                       mc.page_number, mc.page_label, mc.section, mc.chunk_index
                FROM message_citations mc
                JOIN sources s ON s.id = mc.source_id;

                GRANT SELECT ON audit.citations TO talk_to_rag_auditor;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Citations whose chunk was replaced cannot point at a chunk again.
            migrationBuilder.Sql("""
                DROP VIEW audit.citations;
                DELETE FROM message_citations WHERE source_chunk_id IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_message_citations_source_chunks_source_chunk_id",
                table: "message_citations");

            migrationBuilder.DropForeignKey(
                name: "fk_message_citations_sources_source_id",
                table: "message_citations");

            migrationBuilder.DropPrimaryKey(
                name: "pk_message_citations",
                table: "message_citations");

            migrationBuilder.DropIndex(
                name: "ix_message_citations_source_id",
                table: "message_citations");

            migrationBuilder.DropColumn(
                name: "chunk_index",
                table: "message_citations");

            migrationBuilder.DropColumn(
                name: "content",
                table: "message_citations");

            migrationBuilder.DropColumn(
                name: "page_label",
                table: "message_citations");

            migrationBuilder.DropColumn(
                name: "page_number",
                table: "message_citations");

            migrationBuilder.DropColumn(
                name: "section",
                table: "message_citations");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "message_citations");

            migrationBuilder.AlterColumn<Guid>(
                name: "source_chunk_id",
                table: "message_citations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "pk_message_citations",
                table: "message_citations",
                columns: new[] { "message_id", "source_chunk_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_message_citations_source_chunks_source_chunk_id",
                table: "message_citations",
                column: "source_chunk_id",
                principalTable: "source_chunks",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                CREATE VIEW audit.citations AS
                SELECT mc.message_id, mc.number AS rank, ch.source_id, s.title AS source_title,
                       ch.page_number, ch.page_label, ch.section, ch.chunk_index
                FROM message_citations mc
                JOIN source_chunks ch ON ch.id = mc.source_chunk_id
                JOIN sources s ON s.id = ch.source_id;

                GRANT SELECT ON audit.citations TO talk_to_rag_auditor;
                """);
        }
    }
}
