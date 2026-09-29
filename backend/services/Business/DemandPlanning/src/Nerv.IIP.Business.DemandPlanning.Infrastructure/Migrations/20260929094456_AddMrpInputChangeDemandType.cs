using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpInputChangeDemandType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_input_type~",
                schema: "demand_planning",
                table: "mrp_input_changes");

            migrationBuilder.AddColumn<string>(
                name: "demand_type",
                schema: "demand_planning",
                table: "mrp_input_changes",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Demand source type; null for forecast, MPS, or legacy facts with unknown type.");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_input_type~",
                schema: "demand_planning",
                table: "mrp_input_changes",
                columns: new[] { "organization_id", "environment_id", "input_type", "demand_type", "source_reference", "source_line_reference", "occurred_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_input_type~",
                schema: "demand_planning",
                table: "mrp_input_changes");

            migrationBuilder.DropColumn(
                name: "demand_type",
                schema: "demand_planning",
                table: "mrp_input_changes");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_input_type~",
                schema: "demand_planning",
                table: "mrp_input_changes",
                columns: new[] { "organization_id", "environment_id", "input_type", "source_reference", "source_line_reference", "occurred_at_utc" });
        }
    }
}
