using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceCursorAndFetchCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "local_fetch_cache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CacheKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseText = table.Column<string>(type: "text", nullable: true),
                    ETag = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LastModified = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ErrorText = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_local_fetch_cache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "source_cursor_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CursorJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastItemTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastItemId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    NextEligibleRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_cursor_states", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_local_fetch_cache_CacheKey",
                table: "local_fetch_cache",
                column: "CacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_local_fetch_cache_ExpiresAt",
                table: "local_fetch_cache",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_local_fetch_cache_Source_ExpiresAt",
                table: "local_fetch_cache",
                columns: new[] { "Source", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_source_cursor_states_NextEligibleRunAt",
                table: "source_cursor_states",
                column: "NextEligibleRunAt");

            migrationBuilder.CreateIndex(
                name: "IX_source_cursor_states_SourceName_NextEligibleRunAt",
                table: "source_cursor_states",
                columns: new[] { "SourceName", "NextEligibleRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_source_cursor_states_SourceName_SourceKey",
                table: "source_cursor_states",
                columns: new[] { "SourceName", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "local_fetch_cache");

            migrationBuilder.DropTable(
                name: "source_cursor_states");
        }
    }
}
