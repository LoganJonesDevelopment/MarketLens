using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MarketLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineWorkQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipeline_work_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NaturalKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    CurrentAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_work_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_work_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_work_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_work_attempts_pipeline_work_items_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "pipeline_work_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_attempts_Status_LeaseExpiresAt",
                table: "pipeline_work_attempts",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_attempts_WorkerId",
                table: "pipeline_work_attempts",
                column: "WorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_attempts_WorkItemId_AttemptNumber",
                table: "pipeline_work_attempts",
                columns: new[] { "WorkItemId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_items_CurrentAttemptId",
                table: "pipeline_work_items",
                column: "CurrentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_items_LeaseExpiresAt",
                table: "pipeline_work_items",
                column: "LeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_items_WorkType_NaturalKey",
                table: "pipeline_work_items",
                columns: new[] { "WorkType", "NaturalKey" },
                unique: true,
                filter: "\"Status\" IN ('queued', 'running')");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_work_items_WorkType_Status_AvailableAt_Priority",
                table: "pipeline_work_items",
                columns: new[] { "WorkType", "Status", "AvailableAt", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_work_attempts");

            migrationBuilder.DropTable(
                name: "pipeline_work_items");
        }
    }
}
