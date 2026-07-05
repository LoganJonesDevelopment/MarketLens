using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThesisPositionIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PositionIntent",
                table: "research_theses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<string>(
                name: "PositionThesis",
                table: "research_theses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PositionUpdatedAt",
                table: "research_theses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_research_theses_PositionIntent",
                table: "research_theses",
                column: "PositionIntent");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_research_theses_PositionIntent",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "PositionIntent",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "PositionThesis",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "PositionUpdatedAt",
                table: "research_theses");
        }
    }
}
