using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpInputChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mrp_input_changes",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Unique change fact id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization owning the input."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment owning the input."),
                    input_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "MRP input category, such as demand or forecast."),
                    source_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable source document or input identity."),
                    source_line_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable source line identity, empty for a whole-input source."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time at which the source input changed."),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Source change operation: Created, Updated, or Deleted."),
                    previous_start_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "First date in the input interval before the change, if present."),
                    previous_end_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "Last date in the input interval before the change, if present."),
                    previously_eligible = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the previous source state qualified as an MRP input."),
                    current_start_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "First date in the input interval after the change, if present."),
                    current_end_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "Last date in the input interval after the change, if present."),
                    currently_eligible = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the current source state qualifies as an MRP input.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_input_changes", x => x.id);
                },
                comment: "Durable before-and-after facts for DemandPlanning MRP input changes.");

            migrationBuilder.CreateIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_current_en~",
                schema: "demand_planning",
                table: "mrp_input_changes",
                columns: new[] { "organization_id", "environment_id", "current_end_date", "current_start_date" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_input_type~",
                schema: "demand_planning",
                table: "mrp_input_changes",
                columns: new[] { "organization_id", "environment_id", "input_type", "source_reference", "source_line_reference", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_occurred_a~",
                schema: "demand_planning",
                table: "mrp_input_changes",
                columns: new[] { "organization_id", "environment_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_mrp_input_changes_organization_id_environment_id_previous_e~",
                schema: "demand_planning",
                table: "mrp_input_changes",
                columns: new[] { "organization_id", "environment_id", "previous_end_date", "previous_start_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mrp_input_changes",
                schema: "demand_planning");
        }
    }
}
