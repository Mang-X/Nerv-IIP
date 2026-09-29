using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingOverrideSourcePlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_plan_id",
                schema: "scheduling",
                table: "schedule_operation_overrides",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Source APS plan public id for the current manual override; null for historical or MES facts.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_plan_id",
                schema: "scheduling",
                table: "schedule_operation_overrides");
        }
    }
}
