using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStanceClassificationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClassifiedAt",
                table: "research_evidence",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StanceConfidence",
                table: "research_evidence",
                type: "numeric(6,5)",
                precision: 6,
                scale: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StanceModel",
                table: "research_evidence",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StancePromptVersion",
                table: "research_evidence",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StanceRationale",
                table: "research_evidence",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassifiedAt",
                table: "research_evidence");

            migrationBuilder.DropColumn(
                name: "StanceConfidence",
                table: "research_evidence");

            migrationBuilder.DropColumn(
                name: "StanceModel",
                table: "research_evidence");

            migrationBuilder.DropColumn(
                name: "StancePromptVersion",
                table: "research_evidence");

            migrationBuilder.DropColumn(
                name: "StanceRationale",
                table: "research_evidence");
        }
    }
}
