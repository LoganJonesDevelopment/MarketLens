using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscripts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transcripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CallType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CallDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AudioUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DurationSeconds = table.Column<float>(type: "real", nullable: true),
                    SegmentCount = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transcripts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transcripts_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "transcript_segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TranscriptId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentIndex = table.Column<int>(type: "integer", nullable: false),
                    StartSeconds = table.Column<float>(type: "real", nullable: false),
                    EndSeconds = table.Column<float>(type: "real", nullable: false),
                    Speaker = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1024)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transcript_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_transcript_segments_transcripts_TranscriptId",
                        column: x => x.TranscriptId,
                        principalTable: "transcripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_transcript_segments_TranscriptId_SegmentIndex",
                table: "transcript_segments",
                columns: new[] { "TranscriptId", "SegmentIndex" });

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_transcript_segments_Embedding_Hnsw"
                ON transcript_segments
                USING hnsw ("Embedding" vector_cosine_ops)
                WHERE "Embedding" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_transcripts_ArticleId",
                table: "transcripts",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_transcripts_CallDate",
                table: "transcripts",
                column: "CallDate");

            migrationBuilder.CreateIndex(
                name: "IX_transcripts_Status",
                table: "transcripts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_transcripts_Symbol",
                table: "transcripts",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_transcript_segments_Embedding_Hnsw";""");

            migrationBuilder.DropTable(
                name: "transcript_segments");

            migrationBuilder.DropTable(
                name: "transcripts");
        }
    }
}
