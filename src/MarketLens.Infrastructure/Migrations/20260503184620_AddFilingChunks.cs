using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChunkMatchedAt",
                table: "research_theses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ArticleChunkId",
                table: "research_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "article_chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Section = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_article_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_article_chunks_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ArticleChunkId",
                table: "research_evidence",
                column: "ArticleChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_ArticleChunkId",
                table: "research_evidence",
                columns: new[] { "ThesisId", "ArticleChunkId" },
                unique: true,
                filter: "\"ArticleChunkId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_article_chunks_ArticleId",
                table: "article_chunks",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_article_chunks_ArticleId_ChunkIndex",
                table: "article_chunks",
                columns: new[] { "ArticleId", "ChunkIndex" });

            migrationBuilder.AddForeignKey(
                name: "FK_research_evidence_article_chunks_ArticleChunkId",
                table: "research_evidence",
                column: "ArticleChunkId",
                principalTable: "article_chunks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_article_chunks_Embedding_Hnsw"
                ON article_chunks
                USING hnsw ("Embedding" vector_cosine_ops)
                WHERE "Embedding" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_research_evidence_article_chunks_ArticleChunkId",
                table: "research_evidence");

            migrationBuilder.DropTable(
                name: "article_chunks");

            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ArticleChunkId",
                table: "research_evidence");

            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ThesisId_ArticleChunkId",
                table: "research_evidence");

            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_article_chunks_Embedding_Hnsw";""");

            migrationBuilder.DropColumn(
                name: "LastChunkMatchedAt",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "ArticleChunkId",
                table: "research_evidence");
        }
    }
}
