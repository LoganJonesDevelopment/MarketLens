using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceBarFetchStateAndCanonicalDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH ranked_daily AS (
                    SELECT
                        ctid,
                        row_number() OVER (
                            PARTITION BY
                                "Symbol",
                                CASE WHEN "Interval" = 'd' THEN '1d' ELSE "Interval" END,
                                date_trunc('day', "Timestamp")
                            ORDER BY "IngestedAt" DESC, "Timestamp" DESC
                        ) AS rn
                    FROM price_bars
                    WHERE "Interval" IN ('1d', 'd')
                )
                DELETE FROM price_bars p
                USING ranked_daily r
                WHERE p.ctid = r.ctid
                  AND r.rn > 1;

                UPDATE price_bars
                SET
                    "Interval" = '1d',
                    "Timestamp" = date_trunc('day', "Timestamp")
                WHERE "Interval" IN ('1d', 'd');
                """);

            migrationBuilder.CreateTable(
                name: "price_bar_fetch_states",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Interval = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderSymbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EarliestFetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LatestFetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmptyResultCount = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveFailureCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_bar_fetch_states", x => new { x.Symbol, x.Interval, x.Provider });
                });

            migrationBuilder.CreateIndex(
                name: "IX_price_bar_fetch_states_Interval_NextAttemptAt",
                table: "price_bar_fetch_states",
                columns: new[] { "Interval", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_price_bar_fetch_states_NextAttemptAt",
                table: "price_bar_fetch_states",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_price_bar_fetch_states_Status_NextAttemptAt",
                table: "price_bar_fetch_states",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_bar_fetch_states");
        }
    }
}
