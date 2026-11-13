using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConceptGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "concepts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    normalized_name = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: false),
                    proposed_by_model = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_concepts", x => x.id);
                    table.ForeignKey(
                        name: "fk_concepts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "concept_passages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    page_label = table.Column<string>(type: "text", nullable: true),
                    section = table.Column<string>(type: "text", nullable: true),
                    snippet = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_concept_passages", x => x.id);
                    table.ForeignKey(
                        name: "fk_concept_passages_concepts_concept_id",
                        column: x => x.concept_id,
                        principalTable: "concepts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_concept_passages_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "concept_prerequisites",
                columns: table => new
                {
                    concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prerequisite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_concept_prerequisites", x => new { x.concept_id, x.prerequisite_id });
                    table.CheckConstraint("ck_concept_prerequisites_not_self", "concept_id <> prerequisite_id");
                    table.ForeignKey(
                        name: "fk_concept_prerequisites_concepts_concept_id",
                        column: x => x.concept_id,
                        principalTable: "concepts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_concept_prerequisites_concepts_prerequisite_id",
                        column: x => x.prerequisite_id,
                        principalTable: "concepts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_concept_passages_concept_id",
                table: "concept_passages",
                column: "concept_id");

            migrationBuilder.CreateIndex(
                name: "ix_concept_passages_source_id",
                table: "concept_passages",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_concept_prerequisites_prerequisite_id",
                table: "concept_prerequisites",
                column: "prerequisite_id");

            migrationBuilder.CreateIndex(
                name: "ix_concepts_user_id_normalized_name",
                table: "concepts",
                columns: new[] { "user_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "concept_passages");

            migrationBuilder.DropTable(
                name: "concept_prerequisites");

            migrationBuilder.DropTable(
                name: "concepts");
        }
    }
}
