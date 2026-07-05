using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInsiderTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "insider_transactions",
                columns: table => new
                {
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    IssuerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IssuerSymbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OwnerCik = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OwnerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsDirector = table.Column<bool>(type: "boolean", nullable: false),
                    IsOfficer = table.Column<bool>(type: "boolean", nullable: false),
                    IsTenPercentOwner = table.Column<bool>(type: "boolean", nullable: false),
                    IsOther = table.Column<bool>(type: "boolean", nullable: false),
                    OfficerTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SecurityTitle = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransactionCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    AcquiredDisposedCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    PricePerShare = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    SharesOwnedFollowing = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: true),
                    DirectOrIndirectOwnership = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    IsOpenMarketTrade = table.Column<bool>(type: "boolean", nullable: false),
                    IsDerivative = table.Column<bool>(type: "boolean", nullable: false),
                    ParsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_insider_transactions", x => new { x.ArticleId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_insider_transactions_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_insider_transactions_IssuerSymbol",
                table: "insider_transactions",
                column: "IssuerSymbol");

            migrationBuilder.CreateIndex(
                name: "IX_insider_transactions_IssuerSymbol_IsOpenMarketTrade_Transac~",
                table: "insider_transactions",
                columns: new[] { "IssuerSymbol", "IsOpenMarketTrade", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_insider_transactions_IssuerSymbol_TransactionDate",
                table: "insider_transactions",
                columns: new[] { "IssuerSymbol", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_insider_transactions_OwnerName",
                table: "insider_transactions",
                column: "OwnerName");

            migrationBuilder.CreateIndex(
                name: "IX_insider_transactions_TransactionDate",
                table: "insider_transactions",
                column: "TransactionDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "insider_transactions");
        }
    }
}
