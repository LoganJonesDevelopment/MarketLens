using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchThesisLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "research_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Keywords = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_theses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ThesisText = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_theses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    LatestEvidenceAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Summary = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_research_snapshots_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thesis_assets",
                columns: table => new
                {
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_assets", x => new { x.ThesisId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_thesis_assets_research_assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "research_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_thesis_assets_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thesis_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AssetKeywords = table.Column<string>(type: "jsonb", nullable: false),
                    ConceptKeywords = table.Column<string>(type: "jsonb", nullable: false),
                    EventTypes = table.Column<string>(type: "jsonb", nullable: false),
                    SourceNames = table.Column<string>(type: "jsonb", nullable: false),
                    SourceTiers = table.Column<string>(type: "jsonb", nullable: false),
                    ExcludeTerms = table.Column<string>(type: "jsonb", nullable: false),
                    MinArticleSimilarity = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thesis_rules_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "research_evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThesisRuleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatchReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Similarity = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: true),
                    Stance = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    ReviewerNote = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_research_evidence_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_research_evidence_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_research_evidence_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_research_evidence_thesis_rules_ThesisRuleId",
                        column: x => x.ThesisRuleId,
                        principalTable: "thesis_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_research_assets_Kind",
                table: "research_assets",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_research_assets_Name",
                table: "research_assets",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_research_assets_Symbol",
                table: "research_assets",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ArticleId",
                table: "research_evidence",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ClusterId",
                table: "research_evidence",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_MatchedAt",
                table: "research_evidence",
                column: "MatchedAt");

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

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisRuleId",
                table: "research_evidence",
                column: "ThesisRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_ReviewStatus",
                table: "research_evidence",
                columns: new[] { "ThesisId", "ReviewStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_research_evidence_ThesisId_Stance",
                table: "research_evidence",
                columns: new[] { "ThesisId", "Stance" });

            migrationBuilder.CreateIndex(
                name: "IX_research_snapshots_ThesisId_SnapshotAt",
                table: "research_snapshots",
                columns: new[] { "ThesisId", "SnapshotAt" });

            migrationBuilder.CreateIndex(
                name: "IX_research_theses_Status",
                table: "research_theses",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_research_theses_UpdatedAt",
                table: "research_theses",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_assets_AssetId",
                table: "thesis_assets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_rules_ThesisId_IsEnabled",
                table: "thesis_rules",
                columns: new[] { "ThesisId", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "research_evidence");

            migrationBuilder.DropTable(
                name: "research_snapshots");

            migrationBuilder.DropTable(
                name: "thesis_assets");

            migrationBuilder.DropTable(
                name: "thesis_rules");

            migrationBuilder.DropTable(
                name: "research_assets");

            migrationBuilder.DropTable(
                name: "research_theses");
        }
    }
}
