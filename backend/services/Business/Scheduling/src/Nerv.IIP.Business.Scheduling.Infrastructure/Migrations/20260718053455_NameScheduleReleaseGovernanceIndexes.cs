using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NameScheduleReleaseGovernanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_schedule_plans_organization_id_environment_id_release_revis~",
                schema: "scheduling",
                table: "schedule_plans",
                newName: "ux_schedule_plans_scope_release_revision");

            migrationBuilder.RenameIndex(
                name: "IX_schedule_plans_organization_id_environment_id",
                schema: "scheduling",
                table: "schedule_plans",
                newName: "ux_schedule_plans_scope_active_release");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ux_schedule_plans_scope_release_revision",
                schema: "scheduling",
                table: "schedule_plans",
                newName: "IX_schedule_plans_organization_id_environment_id_release_revis~");

            migrationBuilder.RenameIndex(
                name: "ux_schedule_plans_scope_active_release",
                schema: "scheduling",
                table: "schedule_plans",
                newName: "IX_schedule_plans_organization_id_environment_id");
        }
    }
}
