using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InstrumentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Exchange = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Last = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    PreviousClose = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Change = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    ChangePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    AsOf = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_quotes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_quotes_AsOf",
                table: "market_quotes",
                column: "AsOf");

            migrationBuilder.CreateIndex(
                name: "IX_market_quotes_Provider_Symbol",
                table: "market_quotes",
                columns: new[] { "Provider", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_quotes");
        }
    }
}
