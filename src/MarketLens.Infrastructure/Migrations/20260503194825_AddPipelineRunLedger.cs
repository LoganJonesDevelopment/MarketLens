using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineRunLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScopeKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InputCount = table.Column<int>(type: "integer", nullable: false),
                    OutputCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_materializations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PartitionKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    MaterializedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordCount = table.Column<int>(type: "integer", nullable: false),
                    DataVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_materializations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_materializations_pipeline_runs_RunId",
                        column: x => x.RunId,
                        principalTable: "pipeline_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_materializations_AssetKey_PartitionKey_Materialize~",
                table: "pipeline_materializations",
                columns: new[] { "AssetKey", "PartitionKey", "MaterializedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_materializations_MaterializedAt",
                table: "pipeline_materializations",
                column: "MaterializedAt");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_materializations_RunId_AssetKey",
                table: "pipeline_materializations",
                columns: new[] { "RunId", "AssetKey" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_ScopeType_ScopeKey",
                table: "pipeline_runs",
                columns: new[] { "ScopeType", "ScopeKey" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_Stage_StartedAt",
                table: "pipeline_runs",
                columns: new[] { "Stage", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_Stage_Status_StartedAt",
                table: "pipeline_runs",
                columns: new[] { "Stage", "Status", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_StartedAt",
                table: "pipeline_runs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_Status",
                table: "pipeline_runs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_materializations");

            migrationBuilder.DropTable(
                name: "pipeline_runs");
        }
    }
}
