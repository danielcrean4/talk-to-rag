using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TalkToRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionsAndAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "text", nullable: false),
                    generated_by_model = table.Column<bool>(type: "boolean", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    page_label = table.Column<string>(type: "text", nullable: true),
                    section = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_questions_concepts_concept_id",
                        column: x => x.concept_id,
                        principalTable: "concepts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_questions_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_questions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concept_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    platform = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<double>(type: "double precision", nullable: false),
                    answered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                    table.CheckConstraint("ck_attempts_score_range", "score >= 0 AND score <= 1");
                    table.ForeignKey(
                        name: "fk_attempts_concepts_concept_id",
                        column: x => x.concept_id,
                        principalTable: "concepts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempts_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_attempts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempts_concept_id",
                table: "attempts",
                column: "concept_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_question_id",
                table: "attempts",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_user_id_concept_id_answered_at_utc",
                table: "attempts",
                columns: new[] { "user_id", "concept_id", "answered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_questions_concept_id",
                table: "questions",
                column: "concept_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_source_id",
                table: "questions",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_user_id",
                table: "questions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempts");

            migrationBuilder.DropTable(
                name: "questions");
        }
    }
}
