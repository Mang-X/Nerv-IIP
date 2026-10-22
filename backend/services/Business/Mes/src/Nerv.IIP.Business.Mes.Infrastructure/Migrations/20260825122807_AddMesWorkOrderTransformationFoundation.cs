using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesWorkOrderTransformationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "mes",
                table: "work_orders",
                type: "bigint",
                nullable: false,
                defaultValue: 1L,
                comment: "Optimistic concurrency token advanced for every work-order lifecycle or execution mutation.");

            migrationBuilder.CreateTable(
                name: "work_order_transformations",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work-order transformation aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the MES transformation."),
                    transformation_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Transformation type: Split or Merge."),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Transformation audit status; Applied is committed in the same transaction as work-order changes."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Client supplied idempotency identity scoped by organization and environment."),
                    request_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Canonical request payload fingerprint used to reject a different replay under the same idempotency key."),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Authenticated actor recorded for the transformation audit."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Audited business reason for the split or merge."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the transformation was applied.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_transformations", x => x.id);
                    table.CheckConstraint("ck_work_order_transformations_status", "status = 'Applied'");
                    table.CheckConstraint("ck_work_order_transformations_type", "transformation_type IN ('Split', 'Merge')");
                },
                comment: "MES immutable split or merge audit facts and their scoped idempotency identity.");

            migrationBuilder.CreateTable(
                name: "work_order_transformation_lines",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work-order transformation lineage edge id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id copied onto the lineage edge."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id copied onto the lineage edge."),
                    lineage_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Lineage relation type: Split or Merge."),
                    source_work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source or parent MES work-order business id."),
                    target_work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Target or child MES work-order business id."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Quantity represented by this source-to-target lineage edge."),
                    source_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Full source work-order planned quantity captured at transformation time."),
                    target_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Full target work-order planned quantity captured at transformation time."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "UOM shared by the source and target work orders."),
                    source_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Source work-order status captured before the transformation."),
                    target_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Target work-order status captured at the transformation boundary."),
                    source_version = table.Column<long>(type: "bigint", nullable: false, comment: "Expected source work-order version used for optimistic concurrency."),
                    target_version = table.Column<long>(type: "bigint", nullable: false, comment: "Target work-order version captured for lineage audit."),
                    work_order_transformation_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning transformation audit aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_transformation_lines", x => x.id);
                    table.CheckConstraint("ck_work_order_transformation_lines_distinct_work_orders", "source_work_order_id <> target_work_order_id");
                    table.CheckConstraint("ck_work_order_transformation_lines_lineage_type", "lineage_type IN ('Split', 'Merge')");
                    table.CheckConstraint("ck_work_order_transformation_lines_positive_quantity", "quantity > 0");
                    table.CheckConstraint("ck_work_order_transformation_lines_positive_snapshot_quantities", "source_quantity > 0 AND target_quantity > 0");
                    table.CheckConstraint("ck_work_order_transformation_lines_positive_versions", "source_version > 0 AND target_version > 0");
                    table.CheckConstraint("ck_work_order_transformation_lines_uom_present", "trim(uom_code) <> ''");
                    table.ForeignKey(
                        name: "fk_work_order_transformation_lines_source_work_order",
                        columns: x => new { x.organization_id, x.environment_id, x.source_work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_transformation_lines_target_work_order",
                        columns: x => new { x.organization_id, x.environment_id, x.target_work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_transformation_lines_transformations",
                        column: x => x.work_order_transformation_id,
                        principalSchema: "mes",
                        principalTable: "work_order_transformations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "MES immutable source-to-target lineage edges for a split or merge audit.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_work_orders_version_positive",
                schema: "mes",
                table: "work_orders",
                sql: "version > 0");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_transformation_lines_scope_source",
                schema: "mes",
                table: "work_order_transformation_lines",
                columns: new[] { "organization_id", "environment_id", "source_work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_transformation_lines_scope_target",
                schema: "mes",
                table: "work_order_transformation_lines",
                columns: new[] { "organization_id", "environment_id", "target_work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_order_transformation_lines_transformation",
                schema: "mes",
                table: "work_order_transformation_lines",
                column: "work_order_transformation_id");

            migrationBuilder.CreateIndex(
                name: "ux_work_order_transformation_lines_scope_edge",
                schema: "mes",
                table: "work_order_transformation_lines",
                columns: new[] { "organization_id", "environment_id", "source_work_order_id", "target_work_order_id", "lineage_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_transformations_scope_type_occurred",
                schema: "mes",
                table: "work_order_transformations",
                columns: new[] { "organization_id", "environment_id", "transformation_type", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_work_order_transformations_scope_idempotency",
                schema: "mes",
                table: "work_order_transformations",
                columns: new[] { "organization_id", "environment_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_transformation_lines",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "work_order_transformations",
                schema: "mes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_work_orders_version_positive",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "mes",
                table: "work_orders");
        }
    }
}
