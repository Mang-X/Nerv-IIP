using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleWorkingDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_working_drafts",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Working draft row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    plan_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false, comment: "Persisted baseline plan version id."),
                    user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Authenticated planner id forwarded by an internal caller."),
                    state_json = table.Column<string>(type: "jsonb", nullable: false, comment: "SchedulingWorkingDraftStateContract v1 editing state; producer and consumer are Scheduling API and Business Console. Baseline and undo history are excluded."),
                    saved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp of the most recent draft save.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_working_drafts", x => x.id);
                },
                comment: "User-owned scheduling editing state, separate from authoritative plan assignments.");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_working_drafts_organization_id_environment_id_user~",
                schema: "scheduling",
                table: "schedule_working_drafts",
                columns: new[] { "organization_id", "environment_id", "user_id", "plan_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_working_drafts",
                schema: "scheduling");
        }
    }
}
