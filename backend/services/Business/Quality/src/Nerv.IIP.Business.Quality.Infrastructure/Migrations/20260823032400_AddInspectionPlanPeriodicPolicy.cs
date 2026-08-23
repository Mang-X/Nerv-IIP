using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionPlanPeriodicPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "assigned_inspector_user_id",
                schema: "quality",
                table: "inspection_plans",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional inspector user target copied to generated periodic inspection tasks.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_team_id",
                schema: "quality",
                table: "inspection_plans",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional team target copied to generated periodic inspection tasks.");

            migrationBuilder.AddColumn<decimal>(
                name: "quantity_interval",
                schema: "quality",
                table: "inspection_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Optional positive produced quantity interval in the SKU base unit of measure for periodic operation inspection task generation.");

            migrationBuilder.AddColumn<decimal>(
                name: "time_interval_hours",
                schema: "quality",
                table: "inspection_plans",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Optional positive hour interval for periodic operation inspection task generation.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_plans_periodic_assignment_requires_interval",
                schema: "quality",
                table: "inspection_plans",
                sql: "(assigned_inspector_user_id IS NULL AND assigned_team_id IS NULL) OR time_interval_hours IS NOT NULL OR quantity_interval IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_plans_periodic_assignment_target",
                schema: "quality",
                table: "inspection_plans",
                sql: "assigned_inspector_user_id IS NULL OR assigned_team_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_plans_periodic_policy_applicability",
                schema: "quality",
                table: "inspection_plans",
                sql: "(time_interval_hours IS NULL AND quantity_interval IS NULL AND assigned_inspector_user_id IS NULL AND assigned_team_id IS NULL) OR (sku_code IS NOT NULL AND work_center_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_plans_periodic_policy_operation_only",
                schema: "quality",
                table: "inspection_plans",
                sql: "category = 'operation' OR (time_interval_hours IS NULL AND quantity_interval IS NULL AND assigned_inspector_user_id IS NULL AND assigned_team_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_plans_quantity_interval_positive",
                schema: "quality",
                table: "inspection_plans",
                sql: "quantity_interval IS NULL OR quantity_interval > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_plans_time_interval_positive",
                schema: "quality",
                table: "inspection_plans",
                sql: "time_interval_hours IS NULL OR time_interval_hours > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_plans_periodic_assignment_requires_interval",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_plans_periodic_assignment_target",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_plans_periodic_policy_applicability",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_plans_periodic_policy_operation_only",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_plans_quantity_interval_positive",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_plans_time_interval_positive",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropColumn(
                name: "assigned_inspector_user_id",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropColumn(
                name: "assigned_team_id",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropColumn(
                name: "quantity_interval",
                schema: "quality",
                table: "inspection_plans");

            migrationBuilder.DropColumn(
                name: "time_interval_hours",
                schema: "quality",
                table: "inspection_plans");
        }
    }
}
