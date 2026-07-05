using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdeaMemos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idea_memos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WindowDays = table.Column<int>(type: "integer", nullable: false),
                    EvidenceHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: false),
                    MemoJson = table.Column<string>(type: "jsonb", nullable: true),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idea_memos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_idea_memos_Status_RequestedAt",
                table: "idea_memos",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_idea_memos_Symbol_WindowDays_EvidenceHash",
                table: "idea_memos",
                columns: new[] { "Symbol", "WindowDays", "EvidenceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_idea_memos_Symbol_WindowDays_UpdatedAt",
                table: "idea_memos",
                columns: new[] { "Symbol", "WindowDays", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idea_memos");
        }
    }
}
