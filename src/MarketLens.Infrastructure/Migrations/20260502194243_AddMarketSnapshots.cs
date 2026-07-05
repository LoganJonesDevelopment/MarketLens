using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QuoteTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    PreviousClose = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    OpenPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    HighPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    LowPrice = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    MovePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    BenchmarkSymbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BenchmarkMovePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    RelativeMovePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    AverageVolume = table.Column<long>(type: "bigint", nullable: true),
                    RelativeVolume = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    ReactionScore = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: false),
                    IsAfterHours = table.Column<bool>(type: "boolean", nullable: false),
                    IsStale = table.Column<bool>(type: "boolean", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_market_snapshots_events_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "events",
                        principalColumn: "ClusterId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_snapshots_CapturedAt",
                table: "market_snapshots",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_market_snapshots_ClusterId_CapturedAt",
                table: "market_snapshots",
                columns: new[] { "ClusterId", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_market_snapshots_Symbol",
                table: "market_snapshots",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_snapshots");
        }
    }
}
