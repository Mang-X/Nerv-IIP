using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesScheduleReleaseWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_release_watermarks",
                schema: "mes",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the scheduling scope."),
                    revoked_plan_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Plan id owning the highest revoked release revision consumed in this scope."),
                    revoked_release_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Highest revoked monotonic Scheduling release revision consumed in this scope."),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC occurrence time of the highest consumed revocation.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_release_watermarks", x => new { x.organization_id, x.environment_id });
                },
                comment: "Highest revoked Scheduling release revision consumed by MES for each business scope.");

            migrationBuilder.CreateIndex(
                name: "ix_operation_tasks_scope_schedule_plan",
                schema: "mes",
                table: "operation_tasks",
                columns: new[] { "organization_id", "environment_id", "schedule_plan_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_release_watermarks",
                schema: "mes");

            migrationBuilder.DropIndex(
                name: "ix_operation_tasks_scope_schedule_plan",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
