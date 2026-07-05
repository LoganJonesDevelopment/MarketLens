using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOriginalStanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalStance",
                table: "research_evidence",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalStanceConfidence",
                table: "research_evidence",
                type: "numeric(6,5)",
                precision: 6,
                scale: 5,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalStance",
                table: "research_evidence");

            migrationBuilder.DropColumn(
                name: "OriginalStanceConfidence",
                table: "research_evidence");
        }
    }
}
