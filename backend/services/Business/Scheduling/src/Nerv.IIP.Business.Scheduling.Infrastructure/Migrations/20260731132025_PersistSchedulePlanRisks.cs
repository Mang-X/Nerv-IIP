using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistSchedulePlanRisks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "equipment_risks_json",
                schema: "scheduling",
                table: "schedule_plans",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]",
                comment: "Immutable JSON snapshot of equipment data risks emitted with the generated plan.");

            migrationBuilder.AddColumn<string>(
                name: "material_risks_json",
                schema: "scheduling",
                table: "schedule_plans",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]",
                comment: "Immutable JSON snapshot of material risks and nested shortages emitted with the generated plan.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "equipment_risks_json",
                schema: "scheduling",
                table: "schedule_plans");

            migrationBuilder.DropColumn(
                name: "material_risks_json",
                schema: "scheduling",
                table: "schedule_plans");
        }
    }
}
