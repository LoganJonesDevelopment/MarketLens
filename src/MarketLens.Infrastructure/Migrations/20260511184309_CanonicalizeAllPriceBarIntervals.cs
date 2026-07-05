using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalizeAllPriceBarIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH canonical AS (
                    SELECT
                        ctid,
                        CASE
                            WHEN "Interval" = 'd' THEN '1d'
                            WHEN "Interval" = '60m' THEN '1h'
                            WHEN "Interval" = 'mo' THEN '1mo'
                            WHEN "Interval" = 'w' THEN '1w'
                            ELSE "Interval"
                        END AS canonical_interval,
                        CASE
                            WHEN "Interval" IN ('1mo', 'mo') THEN date_trunc('month', "Timestamp")
                            WHEN "Interval" IN ('1w', 'w') THEN date_trunc('week', "Timestamp")
                            WHEN "Interval" IN ('1d', 'd') THEN date_trunc('day', "Timestamp")
                            WHEN "Interval" IN ('1h', '60m') THEN date_trunc('hour', "Timestamp")
                            WHEN "Interval" = '30m' THEN date_trunc('hour', "Timestamp")
                                + (floor(extract(minute from "Timestamp") / 30)::int * interval '30 minutes')
                            WHEN "Interval" = '15m' THEN date_trunc('hour', "Timestamp")
                                + (floor(extract(minute from "Timestamp") / 15)::int * interval '15 minutes')
                            WHEN "Interval" = '5m' THEN date_trunc('hour', "Timestamp")
                                + (floor(extract(minute from "Timestamp") / 5)::int * interval '5 minutes')
                            WHEN "Interval" = '1m' THEN date_trunc('minute', "Timestamp")
                            ELSE "Timestamp"
                        END AS canonical_ts,
                        row_number() OVER (
                            PARTITION BY
                                "Symbol",
                                CASE
                                    WHEN "Interval" = 'd' THEN '1d'
                                    WHEN "Interval" = '60m' THEN '1h'
                                    WHEN "Interval" = 'mo' THEN '1mo'
                                    WHEN "Interval" = 'w' THEN '1w'
                                    ELSE "Interval"
                                END,
                                CASE
                                    WHEN "Interval" IN ('1mo', 'mo') THEN date_trunc('month', "Timestamp")
                                    WHEN "Interval" IN ('1w', 'w') THEN date_trunc('week', "Timestamp")
                                    WHEN "Interval" IN ('1d', 'd') THEN date_trunc('day', "Timestamp")
                                    WHEN "Interval" IN ('1h', '60m') THEN date_trunc('hour', "Timestamp")
                                    WHEN "Interval" = '30m' THEN date_trunc('hour', "Timestamp")
                                        + (floor(extract(minute from "Timestamp") / 30)::int * interval '30 minutes')
                                    WHEN "Interval" = '15m' THEN date_trunc('hour', "Timestamp")
                                        + (floor(extract(minute from "Timestamp") / 15)::int * interval '15 minutes')
                                    WHEN "Interval" = '5m' THEN date_trunc('hour', "Timestamp")
                                        + (floor(extract(minute from "Timestamp") / 5)::int * interval '5 minutes')
                                    WHEN "Interval" = '1m' THEN date_trunc('minute', "Timestamp")
                                    ELSE "Timestamp"
                                END
                            ORDER BY "IngestedAt" DESC, "Timestamp" DESC
                        ) AS rn
                    FROM price_bars
                    WHERE "Interval" IN ('1mo', 'mo', '1w', 'w', '1d', 'd', '1h', '60m', '30m', '15m', '5m', '1m')
                )
                DELETE FROM price_bars p
                USING canonical c
                WHERE p.ctid = c.ctid
                  AND c.rn > 1;

                WITH canonical AS (
                    SELECT
                        ctid,
                        CASE
                            WHEN "Interval" = 'd' THEN '1d'
                            WHEN "Interval" = '60m' THEN '1h'
                            WHEN "Interval" = 'mo' THEN '1mo'
                            WHEN "Interval" = 'w' THEN '1w'
                            ELSE "Interval"
                        END AS canonical_interval,
                        CASE
                            WHEN "Interval" IN ('1mo', 'mo') THEN date_trunc('month', "Timestamp")
                            WHEN "Interval" IN ('1w', 'w') THEN date_trunc('week', "Timestamp")
                            WHEN "Interval" IN ('1d', 'd') THEN date_trunc('day', "Timestamp")
                            WHEN "Interval" IN ('1h', '60m') THEN date_trunc('hour', "Timestamp")
                            WHEN "Interval" = '30m' THEN date_trunc('hour', "Timestamp")
                                + (floor(extract(minute from "Timestamp") / 30)::int * interval '30 minutes')
                            WHEN "Interval" = '15m' THEN date_trunc('hour', "Timestamp")
                                + (floor(extract(minute from "Timestamp") / 15)::int * interval '15 minutes')
                            WHEN "Interval" = '5m' THEN date_trunc('hour', "Timestamp")
                                + (floor(extract(minute from "Timestamp") / 5)::int * interval '5 minutes')
                            WHEN "Interval" = '1m' THEN date_trunc('minute', "Timestamp")
                            ELSE "Timestamp"
                        END AS canonical_ts
                    FROM price_bars
                    WHERE "Interval" IN ('1mo', 'mo', '1w', 'w', '1d', 'd', '1h', '60m', '30m', '15m', '5m', '1m')
                )
                UPDATE price_bars p
                SET
                    "Interval" = c.canonical_interval,
                    "Timestamp" = c.canonical_ts
                FROM canonical c
                WHERE p.ctid = c.ctid
                  AND (p."Interval" <> c.canonical_interval OR p."Timestamp" <> c.canonical_ts);
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
