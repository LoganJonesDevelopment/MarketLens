using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "clusters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MemberCount = table.Column<int>(type: "integer", nullable: false),
                    DominantSourceTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TopSourceWeight = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    TriageEventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TriageConfidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SourceTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Headline = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Publisher = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1024)", nullable: true),
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_articles_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    ClusterId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Sentiment = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    Slots = table.Column<string>(type: "jsonb", nullable: false),
                    Importance = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    SourceWeight = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    NoveltyWeight = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    EventClassPrior = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    MagnitudeSignal = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExtractedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.ClusterId);
                    table.ForeignKey(
                        name: "FK_events_clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "clusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_articles_ClusterId",
                table: "articles",
                column: "ClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_articles_PublishedAt",
                table: "articles",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_articles_Source_SourceId",
                table: "articles",
                columns: new[] { "Source", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_articles_Symbol",
                table: "articles",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_clusters_FirstSeenAt",
                table: "clusters",
                column: "FirstSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_clusters_Symbol",
                table: "clusters",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_clusters_TriageEventType",
                table: "clusters",
                column: "TriageEventType");

            migrationBuilder.CreateIndex(
                name: "IX_events_EventType",
                table: "events",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_events_Importance",
                table: "events",
                column: "Importance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "clusters");
        }
    }
}
