using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EvidenceFanOutPerRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ThesisId_ArticleId",
                table: "research_evidence");

            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ThesisId_ClusterId",
                table: "research_evidence");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_ThesisRuleId_ArticleId",
                table: "research_evidence",
                columns: new[] { "ThesisId", "ThesisRuleId", "ArticleId" },
                unique: true,
                filter: "\"ArticleId\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_ThesisRuleId_ClusterId",
                table: "research_evidence",
                columns: new[] { "ThesisId", "ThesisRuleId", "ClusterId" },
                unique: true,
                filter: "\"ClusterId\" IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ThesisId_ThesisRuleId_ArticleId",
                table: "research_evidence");

            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ThesisId_ThesisRuleId_ClusterId",
                table: "research_evidence");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_ArticleId",
                table: "research_evidence",
                columns: new[] { "ThesisId", "ArticleId" },
                unique: true,
                filter: "\"ArticleId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_ClusterId",
                table: "research_evidence",
                columns: new[] { "ThesisId", "ClusterId" },
                unique: true,
                filter: "\"ClusterId\" IS NOT NULL");
        }
    }
}
