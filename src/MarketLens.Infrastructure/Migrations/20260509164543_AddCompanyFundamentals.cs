using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyFundamentals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_fundamentals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Exchange = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Industry = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    WebUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IpoDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MarketCapitalizationMillion = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    ShareOutstandingMillion = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    EnterpriseValueMillion = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    PeTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ForwardPe = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    PegTtm = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    PsTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    EvRevenueTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    EvEbitdaTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    PriceToBook = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    PriceToFreeCashFlowTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    RevenueGrowthTtmYoy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    EpsGrowthTtmYoy = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    GrossMarginTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    OperatingMarginTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    NetMarginTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    RoeTtm = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    DebtToEquityQuarterly = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Beta = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Week52High = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Week52Low = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Week52PriceReturnDaily = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    RawProfileJson = table.Column<string>(type: "jsonb", nullable: false),
                    RawMetricJson = table.Column<string>(type: "jsonb", nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_fundamentals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_fundamentals_Provider_Symbol",
                table: "company_fundamentals",
                columns: new[] { "Provider", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_fundamentals_Status",
                table: "company_fundamentals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_company_fundamentals_Symbol_IngestedAt",
                table: "company_fundamentals",
                columns: new[] { "Symbol", "IngestedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_fundamentals");
        }
    }
}
