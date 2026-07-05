using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChartDataFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "economic_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsTimeSpecific = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_economic_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_economic_events_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "price_bars",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Interval = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Open = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    High = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Close = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Volume = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_bars", x => new { x.Symbol, x.Interval, x.Timestamp });
                });

            migrationBuilder.CreateIndex(
                name: "IX_economic_events_ClusterId",
                table: "economic_events",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_economic_events_EventType",
                table: "economic_events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_economic_events_ScheduledAt",
                table: "economic_events",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_economic_events_Source_SourceId",
                table: "economic_events",
                columns: new[] { "Source", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_economic_events_Symbol_ScheduledAt",
                table: "economic_events",
                columns: new[] { "Symbol", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_price_bars_Symbol_Interval_Timestamp",
                table: "price_bars",
                columns: new[] { "Symbol", "Interval", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "economic_events");

            migrationBuilder.DropTable(
                name: "price_bars");
        }
    }
}
