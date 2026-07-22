using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderUrgencyModelV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_urgency_business_priorities",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Business-priority row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Scheduling order/work-order id."),
                    business_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable upstream business reference used across ERP, planning, MES, and scheduling."),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Current P0-P3 business priority."),
                    set_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Authenticated actor reference that set the priority."),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Human-readable reason for the priority."),
                    set_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the priority was set."),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Optional UTC expiry for the manual priority."),
                    revision = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic audit revision and optimistic concurrency token.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_business_priorities", x => x.id);
                },
                comment: "Current audited business-priority input for the unified order urgency model.");

            migrationBuilder.CreateTable(
                name: "order_urgency_business_priority_changes",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Priority-change row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Scheduling order/work-order id."),
                    business_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable upstream business reference used across ERP, planning, MES, and scheduling."),
                    revision = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic priority revision."),
                    previous_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true, comment: "Priority before the change; null for the initial setting."),
                    new_level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Priority after the change."),
                    changed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Authenticated actor reference."),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Human-readable change reason."),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC change timestamp."),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Optional UTC expiry for the new priority.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_business_priority_changes", x => x.id);
                },
                comment: "Append-only business-priority audit history for the unified order urgency model.");

            migrationBuilder.CreateTable(
                name: "order_urgency_snapshots",
                schema: "scheduling",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Urgency snapshot row id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Business environment id."),
                    order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Scheduling order/work-order id."),
                    business_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable upstream business reference used across ERP, planning, MES, and scheduling."),
                    level = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Unified urgency level."),
                    model_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Versioned deterministic calculation model."),
                    input_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Fingerprint of authoritative source facts."),
                    business_priority_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Priority revision used by this calculation."),
                    calculation_bucket_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Deterministic UTC time bucket used for idempotent recalculation."),
                    calculated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC model calculation timestamp."),
                    result_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Explainable contributions, reason codes, and source timestamps.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_urgency_snapshots", x => x.id);
                },
                comment: "Immutable explainable urgency calculation snapshots and input audit evidence.");

            migrationBuilder.CreateIndex(
                name: "IX_order_urgency_business_priorities_organization_id_environm~1",
                schema: "scheduling",
                table: "order_urgency_business_priorities",
                columns: new[] { "organization_id", "environment_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_urgency_business_priorities_organization_id_environme~",
                schema: "scheduling",
                table: "order_urgency_business_priorities",
                columns: new[] { "organization_id", "environment_id", "business_reference" });

            migrationBuilder.CreateIndex(
                name: "IX_order_urgency_business_priority_changes_organization_id_env~",
                schema: "scheduling",
                table: "order_urgency_business_priority_changes",
                columns: new[] { "organization_id", "environment_id", "order_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_urgency_snapshots_organization_id_environment_id_busi~",
                schema: "scheduling",
                table: "order_urgency_snapshots",
                columns: new[] { "organization_id", "environment_id", "business_reference", "calculated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_order_urgency_snapshots_organization_id_environment_id_ord~1",
                schema: "scheduling",
                table: "order_urgency_snapshots",
                columns: new[] { "organization_id", "environment_id", "order_id", "model_version", "input_fingerprint", "business_priority_revision", "calculation_bucket_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_urgency_snapshots_organization_id_environment_id_orde~",
                schema: "scheduling",
                table: "order_urgency_snapshots",
                columns: new[] { "organization_id", "environment_id", "order_id", "calculated_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_urgency_business_priorities",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "order_urgency_business_priority_changes",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "order_urgency_snapshots",
                schema: "scheduling");
        }
    }
}
