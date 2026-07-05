using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations;

[Migration("20260502183000_AddArticleEmbeddingHnswIndex")]
public partial class AddArticleEmbeddingHnswIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_articles_Embedding_Hnsw"
            ON articles
            USING hnsw ("Embedding" vector_cosine_ops)
            WHERE "Embedding" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_articles_Embedding_Hnsw";""");
    }
}
