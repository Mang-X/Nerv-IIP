using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityInspectionFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_sourc~",
                schema: "quality",
                table: "nonconformance_reports",
                newName: "IX_nonconformance_reports_organization_id_environment_id_sour~1");

            migrationBuilder.AddColumn<Guid>(
                name: "source_inspection_record_id",
                schema: "quality",
                table: "nonconformance_reports",
                type: "uuid",
                nullable: true,
                comment: "Optional Quality inspection record id that opened this NCR.");

            migrationBuilder.CreateTable(
                name: "inspection_plans",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inspection plan aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the plan."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the plan applies."),
                    plan_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Human-readable inspection plan code."),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inspection category: receiving, operation, final, maintenance or customer-return."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional MasterData SKU code applicability reference."),
                    partner_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional supplier or customer public reference id."),
                    work_center_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional work center public reference id."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional device asset public reference id."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional source document type covered by the plan."),
                    version = table.Column<int>(type: "integer", nullable: false, comment: "Plan version number."),
                    supersedes_plan_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Previous inspection plan version id superseded by this version."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inspection plan lifecycle status."),
                    activated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the plan was activated."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the plan was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the plan was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_plans", x => x.id);
                },
                comment: "Quality inspection plan version and applicability facts.");

            migrationBuilder.CreateTable(
                name: "inspection_records",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inspection record aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the record."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the inspection was recorded."),
                    inspection_plan_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Optional inspection plan version id used for this record."),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inspection source type: receiving, operation, final, maintenance or customer-return."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source service or document family that requested the inspection."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source document or operation public id."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "SKU code inspected as a Quality reference."),
                    inspected_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity inspected."),
                    batch_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional batch number reference."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number reference."),
                    result = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inspection result: passed, rejected or conditional-release."),
                    disposition_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Disposition reason preserved for rejected or conditional-release inspections."),
                    disposition_attachment_file_ids = table.Column<List<string>>(type: "text[]", nullable: false, comment: "File Storage attachment ids supporting the disposition."),
                    nonconformance_report_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional NCR id opened from this failed inspection."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the inspection was recorded."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the inspection record was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_records", x => x.id);
                },
                comment: "Quality inspection execution records and final result facts.");

            migrationBuilder.CreateTable(
                name: "inspection_plan_characteristics",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inspection plan characteristic id."),
                    inspection_plan_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning inspection plan id."),
                    characteristic_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable characteristic code within the plan."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Characteristic display name."),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inspection method or measurement procedure."),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality severity classification."),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this characteristic is required for plan execution."),
                    sampling_rule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Sampling rule or sample size expression.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_plan_characteristics", x => x.id);
                    table.ForeignKey(
                        name: "FK_inspection_plan_characteristics_inspection_plans_inspection~",
                        column: x => x.inspection_plan_id,
                        principalSchema: "quality",
                        principalTable: "inspection_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Quality inspection plan characteristics and sampling rules.");

            migrationBuilder.CreateTable(
                name: "inspection_result_lines",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inspection result line id."),
                    inspection_record_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning inspection record id."),
                    characteristic_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Measured or checked characteristic code."),
                    observed_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Observed measurement value or check result."),
                    unit_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Optional unit of measure code for measured values."),
                    result = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Line result: passed, failed or conditional-release."),
                    defect_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Defect or waiver reason for failed or conditional-release lines."),
                    defect_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Quantity represented by this defect line."),
                    attachment_file_ids = table.Column<List<string>>(type: "text[]", nullable: false, comment: "File Storage attachment ids for this result line.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inspection_result_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_inspection_result_lines_inspection_records_inspection_recor~",
                        column: x => x.inspection_record_id,
                        principalSchema: "quality",
                        principalTable: "inspection_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Quality inspection result line measurements and defect facts.");

            migrationBuilder.CreateIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_sourc~",
                schema: "quality",
                table: "nonconformance_reports",
                columns: new[] { "organization_id", "environment_id", "source_inspection_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_plan_characteristics_inspection_plan_id_characte~",
                schema: "quality",
                table: "inspection_plan_characteristics",
                columns: new[] { "inspection_plan_id", "characteristic_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspection_plans_organization_id_environment_id_category_st~",
                schema: "quality",
                table: "inspection_plans",
                columns: new[] { "organization_id", "environment_id", "category", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_plans_organization_id_environment_id_plan_code",
                schema: "quality",
                table: "inspection_plans",
                columns: new[] { "organization_id", "environment_id", "plan_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inspection_records_organization_id_environment_id_source_se~",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_records_organization_id_environment_id_source_ty~",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_type", "result" });

            migrationBuilder.CreateIndex(
                name: "IX_inspection_result_lines_inspection_record_id_characteristic~",
                schema: "quality",
                table: "inspection_result_lines",
                columns: new[] { "inspection_record_id", "characteristic_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inspection_plan_characteristics",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "inspection_result_lines",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "inspection_plans",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "inspection_records",
                schema: "quality");

            migrationBuilder.DropIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_sourc~",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.DropColumn(
                name: "source_inspection_record_id",
                schema: "quality",
                table: "nonconformance_reports");

            migrationBuilder.RenameIndex(
                name: "IX_nonconformance_reports_organization_id_environment_id_sour~1",
                schema: "quality",
                table: "nonconformance_reports",
                newName: "IX_nonconformance_reports_organization_id_environment_id_sourc~");
        }
    }
}
