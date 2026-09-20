using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAssetRetirementDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "retired_current_file_by_decision_id",
                schema: "barcode",
                table: "label_templates",
                type: "uuid",
                nullable: true,
                comment: "Retirement decision that preserves this historical pointer while prohibiting reuse.");

            migrationBuilder.CreateTable(
                name: "template_asset_retirement_decisions",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Retirement decision id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the decision."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id that owns the decision."),
                    label_template_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "BarcodeLabel template that owns the FileStorage asset."),
                    template_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Frozen FileStorage owner id for the template asset."),
                    template_file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "FileStorage file id permanently fenced from new BarcodeLabel use."),
                    template_asset_sha256 = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false, comment: "Frozen canonical SHA-256 asset digest."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Caller supplied idempotency key for decision creation."),
                    requester_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Authenticated final-user subject captured for audit."),
                    permission = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Permission proven when the decision was created."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Final-user supplied retirement reason."),
                    correlation_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Safe upstream audit correlation id."),
                    reference_result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Frozen BarcodeLabel reference evaluation result."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Retirement decision execution status."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the decision was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the decision was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_asset_retirement_decisions", x => x.id);
                },
                comment: "BarcodeLabel-owned decisions that permanently fence template assets from new use.");

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirement_decisions_label_template_id",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                column: "label_template_id");

            migrationBuilder.CreateIndex(
                name: "UX_template_asset_retirement_decisions_file",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                columns: new[] { "organization_id", "environment_id", "template_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_template_asset_retirement_decisions_idempotency",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                columns: new[] { "organization_id", "environment_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_asset_retirement_decisions",
                schema: "barcode");

            migrationBuilder.DropColumn(
                name: "retired_current_file_by_decision_id",
                schema: "barcode",
                table: "label_templates");
        }
    }
}
