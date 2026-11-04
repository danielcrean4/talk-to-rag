using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceHashAndChunkMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "file_hash",
                table: "sources",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

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

            migrationBuilder.AddColumn<string>(
                name: "embedding_model",
                table: "source_chunks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "token_count",
                table: "source_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sources_user_id_file_hash",
                table: "sources",
                columns: new[] { "user_id", "file_hash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sources_user_id_file_hash",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "file_hash",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "license",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "source_url",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "embedding_model",
                table: "source_chunks");

            migrationBuilder.DropColumn(
                name: "token_count",
                table: "source_chunks");
        }
    }
}
