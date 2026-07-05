using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThesisPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "research_theses",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanGeneratedAt",
                table: "research_theses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanModel",
                table: "research_theses",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanPromptVersion",
                table: "research_theses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "PlanGeneratedAt",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "PlanModel",
                table: "research_theses");

            migrationBuilder.DropColumn(
                name: "PlanPromptVersion",
                table: "research_theses");
        }
    }
}
