using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialIssueSupplementarySemantics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_supplementary",
                schema: "mes",
                table: "material_issue_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this material issue request supplements an earlier request.");

            migrationBuilder.AddColumn<string>(
                name: "original_material_issue_request_no",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Business number of the original material issue request in the same organization, environment, work order and material scope.");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_material_issue_requests_scope_request_work_order_material",
                schema: "mes",
                table: "material_issue_requests",
                columns: new[] { "organization_id", "environment_id", "request_no", "work_order_id", "material_id" });

            migrationBuilder.CreateIndex(
                name: "ix_material_issue_requests_scope_original_request",
                schema: "mes",
                table: "material_issue_requests",
                columns: new[] { "organization_id", "environment_id", "original_material_issue_request_no", "work_order_id", "material_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_issue_requests_not_self_referential",
                schema: "mes",
                table: "material_issue_requests",
                sql: "original_material_issue_request_no IS NULL OR original_material_issue_request_no <> request_no");

            migrationBuilder.AddCheckConstraint(
                name: "ck_material_issue_requests_supplementary_source",
                schema: "mes",
                table: "material_issue_requests",
                sql: "(is_supplementary = TRUE AND original_material_issue_request_no IS NOT NULL) OR (is_supplementary = FALSE AND original_material_issue_request_no IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_material_issue_requests_original_request",
                schema: "mes",
                table: "material_issue_requests",
                columns: new[] { "organization_id", "environment_id", "original_material_issue_request_no", "work_order_id", "material_id" },
                principalSchema: "mes",
                principalTable: "material_issue_requests",
                principalColumns: new[] { "organization_id", "environment_id", "request_no", "work_order_id", "material_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_material_issue_requests_original_request",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_material_issue_requests_scope_request_work_order_material",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropIndex(
                name: "ix_material_issue_requests_scope_original_request",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_issue_requests_not_self_referential",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_material_issue_requests_supplementary_source",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "is_supplementary",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "original_material_issue_request_no",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
