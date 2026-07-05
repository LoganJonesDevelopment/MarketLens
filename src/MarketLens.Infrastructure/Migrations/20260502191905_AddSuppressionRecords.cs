using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSuppressionRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suppressions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true),
                    Headline = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Publisher = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SuppressedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppressions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_suppressions_Reason",
                table: "suppressions",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_suppressions_Source_SourceId_Stage_Reason",
                table: "suppressions",
                columns: new[] { "Source", "SourceId", "Stage", "Reason" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppressions_Stage",
                table: "suppressions",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_suppressions_SuppressedAt",
                table: "suppressions",
                column: "SuppressedAt");

            migrationBuilder.CreateIndex(
                name: "IX_suppressions_Symbol",
                table: "suppressions",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "suppressions");
        }
    }
}
