using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThesisTradingInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "thesis_catalysts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CatalystDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Metal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CatalystType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_catalysts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thesis_catalysts_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thesis_kill_criteria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scenario = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    MonitoringKeywords = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ThreatLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ContradictingEvidenceCount = table.Column<int>(type: "integer", nullable: false),
                    LastTriggeredReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastEscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_kill_criteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thesis_kill_criteria_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "thesis_positions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThesisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Metal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetAllocationPct = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    DeployedPct = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    EntryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ScaleInTriggerPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ScaleInNotes = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thesis_positions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thesis_positions_research_theses_ThesisId",
                        column: x => x.ThesisId,
                        principalTable: "research_theses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_thesis_catalysts_CatalystDate",
                table: "thesis_catalysts",
                column: "CatalystDate");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_catalysts_ThesisId_CatalystDate",
                table: "thesis_catalysts",
                columns: new[] { "ThesisId", "CatalystDate" });

            migrationBuilder.CreateIndex(
                name: "IX_thesis_kill_criteria_ThesisId",
                table: "thesis_kill_criteria",
                column: "ThesisId");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_kill_criteria_ThreatLevel",
                table: "thesis_kill_criteria",
                column: "ThreatLevel");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_positions_Metal",
                table: "thesis_positions",
                column: "Metal");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_positions_Symbol",
                table: "thesis_positions",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_thesis_positions_ThesisId",
                table: "thesis_positions",
                column: "ThesisId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thesis_catalysts");

            migrationBuilder.DropTable(
                name: "thesis_kill_criteria");

            migrationBuilder.DropTable(
                name: "thesis_positions");
        }
    }
}
