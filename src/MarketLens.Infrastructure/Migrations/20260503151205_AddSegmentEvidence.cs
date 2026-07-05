using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSegmentMatchedAt",
                table: "research_theses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TranscriptSegmentId",
                table: "research_evidence",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_TranscriptSegmentId",
                table: "research_evidence",
                columns: new[] { "ThesisId", "TranscriptSegmentId" },
                unique: true,
                filter: "\"TranscriptSegmentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_TranscriptSegmentId",
                table: "research_evidence",
                column: "TranscriptSegmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_research_evidence_transcript_segments_TranscriptSegmentId",
                table: "research_evidence",
                column: "TranscriptSegmentId",
                principalTable: "transcript_segments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_research_evidence_transcript_segments_TranscriptSegmentId",
                table: "research_evidence");

            migrationBuilder.DropIndex(
                name: "IX_research_evidence_ThesisId_TranscriptSegmentId",
                table: "research_evidence");

            migrationBuilder.DropIndex(
                name: "IX_research_evidence_TranscriptSegmentId",
                table: "research_evidence");

            migrationBuilder.DropColumn(
                name: "LastSegmentMatchedAt",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "TranscriptSegmentId",
                table: "research_evidence");
        }
    }
}
