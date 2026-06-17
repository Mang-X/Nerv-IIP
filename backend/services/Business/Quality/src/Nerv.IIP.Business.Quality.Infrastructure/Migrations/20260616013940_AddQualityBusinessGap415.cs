using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityBusinessGap415 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "measured_value",
                schema: "quality",
                table: "inspection_result_lines",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Numeric measured value for variable characteristics.");

            migrationBuilder.AddColumn<string>(
                name: "location_code",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional stock release location code for Inventory quality-status transfer.");

            migrationBuilder.AddColumn<string>(
                name: "owner_id",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional stock owner reference id for Inventory quality-status transfer.");

            migrationBuilder.AddColumn<string>(
                name: "owner_type",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Optional stock owner type for Inventory quality-status transfer.");

            migrationBuilder.AddColumn<string>(
                name: "site_code",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional stock release site code for Inventory quality-status transfer.");

            migrationBuilder.AddColumn<string>(
                name: "source_quality_status",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Optional source Inventory quality status to transfer from after inspection.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Optional stock release UOM code for Inventory quality-status transfer.");

            migrationBuilder.AddColumn<string>(
                name: "characteristic_type",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "attribute",
                comment: "Characteristic type: variable or attribute.");

            migrationBuilder.AddColumn<decimal>(
                name: "lower_spec_limit",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Lower specification limit for variable inspection characteristics.");

            migrationBuilder.AddColumn<decimal>(
                name: "nominal_value",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Nominal target value for variable inspection characteristics.");

            migrationBuilder.AddColumn<int>(
                name: "sampling_acceptance_number",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "integer",
                nullable: true,
                comment: "Maximum defect count that accepts the lot.");

            migrationBuilder.AddColumn<string>(
                name: "sampling_aql",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Acceptable quality limit value used for attribute sampling.");

            migrationBuilder.AddColumn<string>(
                name: "sampling_inspection_level",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "AQL sampling inspection level such as general-ii.");

            migrationBuilder.AddColumn<int>(
                name: "sampling_rejection_number",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "integer",
                nullable: true,
                comment: "Minimum defect count that rejects the lot.");

            migrationBuilder.AddColumn<int>(
                name: "sampling_sample_size",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "integer",
                nullable: true,
                comment: "Required sample size resolved from the sampling plan.");

            migrationBuilder.AddColumn<string>(
                name: "unit_code",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Unit of measure code for measured characteristic values.");

            migrationBuilder.AddColumn<decimal>(
                name: "upper_spec_limit",
                schema: "quality",
                table: "inspection_plan_characteristics",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Upper specification limit for variable inspection characteristics.");

            migrationBuilder.CreateTable(
                name: "corrective_actions",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "CAPA aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the CAPA."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the CAPA is managed."),
                    capa_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Human-readable CAPA code."),
                    source_ncr_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional source NCR id that triggered this CAPA."),
                    root_cause = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Root cause analysis summary such as 5Why or fishbone result."),
                    containment_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Immediate containment action taken to control nonconforming output."),
                    owner_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "CAPA owner user public id."),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC due time for CAPA completion."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "CAPA lifecycle status."),
                    effectiveness_verified_by_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "User id that verified CAPA effectiveness."),
                    effectiveness_result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Effectiveness verification result."),
                    effectiveness_verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when effectiveness was verified."),
                    closed_by_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "User id that closed the CAPA."),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when CAPA was closed."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when CAPA was opened."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when CAPA was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corrective_actions", x => x.id);
                },
                comment: "Quality CAPA corrective and preventive action lifecycle facts.");

            migrationBuilder.CreateTable(
                name: "ncr_mrb_reviews",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MRB review entry id."),
                    nonconformance_report_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning NCR id."),
                    reviewer_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Reviewer user or committee member public id."),
                    decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MRB reviewer decision such as approved or rejected."),
                    comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional MRB reviewer comment."),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when this MRB review was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ncr_mrb_reviews", x => x.id);
                    table.ForeignKey(
                        name: "FK_ncr_mrb_reviews_nonconformance_reports_nonconformance_repor~",
                        column: x => x.nonconformance_report_id,
                        principalSchema: "quality",
                        principalTable: "nonconformance_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Quality NCR material review board decisions captured before disposition execution.");

            migrationBuilder.CreateTable(
                name: "corrective_action_items",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "CAPA action item id."),
                    corrective_action_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning CAPA id."),
                    action_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "CAPA action type: containment, corrective or preventive."),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "CAPA action description."),
                    owner_user_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Action owner user public id."),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC due time for this CAPA action."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "CAPA action item status."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when this action item was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_corrective_action_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_corrective_action_items_corrective_actions_corrective_actio~",
                        column: x => x.corrective_action_id,
                        principalSchema: "quality",
                        principalTable: "corrective_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Quality CAPA containment, corrective and preventive action items.");

            migrationBuilder.CreateIndex(
                name: "IX_corrective_action_items_corrective_action_id_action_type",
                schema: "quality",
                table: "corrective_action_items",
                columns: new[] { "corrective_action_id", "action_type" });

            migrationBuilder.CreateIndex(
                name: "IX_corrective_actions_organization_id_environment_id_capa_code",
                schema: "quality",
                table: "corrective_actions",
                columns: new[] { "organization_id", "environment_id", "capa_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_corrective_actions_organization_id_environment_id_source_nc~",
                schema: "quality",
                table: "corrective_actions",
                columns: new[] { "organization_id", "environment_id", "source_ncr_id" });

            migrationBuilder.CreateIndex(
                name: "IX_corrective_actions_organization_id_environment_id_status",
                schema: "quality",
                table: "corrective_actions",
                columns: new[] { "organization_id", "environment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_ncr_mrb_reviews_nonconformance_report_id_reviewer_id",
                schema: "quality",
                table: "ncr_mrb_reviews",
                columns: new[] { "nonconformance_report_id", "reviewer_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corrective_action_items",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "ncr_mrb_reviews",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "corrective_actions",
                schema: "quality");

            migrationBuilder.DropColumn(
                name: "measured_value",
                schema: "quality",
                table: "inspection_result_lines");

            migrationBuilder.DropColumn(
                name: "location_code",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "owner_id",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "owner_type",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "site_code",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "source_quality_status",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "characteristic_type",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "lower_spec_limit",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "nominal_value",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "sampling_acceptance_number",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "sampling_aql",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "sampling_inspection_level",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "sampling_rejection_number",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "sampling_sample_size",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "unit_code",
                schema: "quality",
                table: "inspection_plan_characteristics");

            migrationBuilder.DropColumn(
                name: "upper_spec_limit",
                schema: "quality",
                table: "inspection_plan_characteristics");
        }
    }
}
