using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderDemandProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_demand_sources_organization_id_environment_id_demand_type_s~",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.AddColumn<string>(
                name: "customer_code",
                schema: "demand_planning",
                table: "demand_sources",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Customer code snapshot supplied by the upstream sales order.");

            migrationBuilder.AddColumn<string>(
                name: "source_document_id",
                schema: "demand_planning",
                table: "demand_sources",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                comment: "Stable upstream source document identifier; sales order public id for ERP demand.");

            migrationBuilder.AddColumn<string>(
                name: "source_line_reference",
                schema: "demand_planning",
                table: "demand_sources",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "Stable upstream source line reference; empty for manually managed demand.");

            migrationBuilder.AddColumn<string>(
                name: "source_status",
                schema: "demand_planning",
                table: "demand_sources",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "active",
                comment: "Explainable upstream lifecycle status: active or cancelled.");

            migrationBuilder.AddColumn<int>(
                name: "source_version",
                schema: "demand_planning",
                table: "demand_sources",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Latest accepted upstream business version; zero for manually managed demand.");

            migrationBuilder.CreateTable(
                name: "integration_event_dead_letters",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Dead-letter message id."),
                    consumer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Integration event consumer name that rejected the message."),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Rejected integration event id when present."),
                    event_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "Rejected integration event type when present."),
                    event_version = table.Column<int>(type: "integer", nullable: true, comment: "Rejected integration event envelope version when present."),
                    source_service = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Source service from the rejected event envelope when present."),
                    idempotency_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Rejected integration event idempotency key when present."),
                    event_clr_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "CLR contract type captured for replay diagnostics."),
                    event_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Serialized rejected integration event envelope and payload."),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Machine-readable reason the consumer rejected the message."),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Operator-readable rejection detail."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored."),
                    dead_lettered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the service stored the dead-letter message."),
                    replayed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the dead-letter message was marked replayed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_event_dead_letters", x => x.id);
                },
                comment: "Integration events rejected before business handling and retained for replay triage.");

            migrationBuilder.CreateTable(
                name: "processed_integration_events",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Processed integration event identifier."),
                    consumer_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "DemandPlanning integration event consumer name."),
                    event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Source event identifier retained for traceability."),
                    event_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Integration event type."),
                    event_version = table.Column<int>(type: "integer", nullable: false, comment: "Integration event contract version."),
                    source_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Service that produced the integration event."),
                    idempotency_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Deterministic idempotency key unique within the consumer."),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when DemandPlanning processed the event.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_integration_events", x => x.id);
                },
                comment: "Integration events already processed by DemandPlanning for idempotent consumption.");

            migrationBuilder.CreateTable(
                name: "sales_order_demand_projections",
                schema: "demand_planning",
                columns: table => new
                {
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment id."),
                    sales_order_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable ERP sales order public id."),
                    sales_order_no = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "ERP sales order number for traceability."),
                    customer_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Customer code snapshot."),
                    site_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning site code snapshot."),
                    order_version = table.Column<int>(type: "integer", nullable: false, comment: "Latest accepted ERP sales order business version."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Latest accepted ERP sales order lifecycle status."),
                    last_event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Latest accepted integration event identifier."),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Source event occurrence time for audit.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_order_demand_projections", x => new { x.organization_id, x.environment_id, x.sales_order_id });
                },
                comment: "Per-sales-order lifecycle watermark used to reject duplicate and out-of-order ERP events.");

            migrationBuilder.CreateIndex(
                name: "IX_demand_sources_organization_id_environment_id_demand_type_s~",
                schema: "demand_planning",
                table: "demand_sources",
                columns: new[] { "organization_id", "environment_id", "demand_type", "source_reference", "source_line_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_dead_letters_consumer_name_event_id",
                schema: "demand_planning",
                table: "integration_event_dead_letters",
                columns: new[] { "consumer_name", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_integration_event_dead_letters_consumer_name_status_dead_le~",
                schema: "demand_planning",
                table: "integration_event_dead_letters",
                columns: new[] { "consumer_name", "status", "dead_lettered_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_dp_processed_events_source_type_processed_at",
                schema: "demand_planning",
                table: "processed_integration_events",
                columns: new[] { "source_service", "event_type", "processed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_processed_integration_events_consumer_idempotency_key",
                schema: "demand_planning",
                table: "processed_integration_events",
                columns: new[] { "consumer_name", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sales_order_demand_projection_scope_order_no",
                schema: "demand_planning",
                table: "sales_order_demand_projections",
                columns: new[] { "organization_id", "environment_id", "sales_order_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_event_dead_letters",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "processed_integration_events",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "sales_order_demand_projections",
                schema: "demand_planning");

            migrationBuilder.DropIndex(
                name: "IX_demand_sources_organization_id_environment_id_demand_type_s~",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.DropColumn(
                name: "customer_code",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.DropColumn(
                name: "source_document_id",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.DropColumn(
                name: "source_line_reference",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.DropColumn(
                name: "source_status",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.DropColumn(
                name: "source_version",
                schema: "demand_planning",
                table: "demand_sources");

            migrationBuilder.CreateIndex(
                name: "IX_demand_sources_organization_id_environment_id_demand_type_s~",
                schema: "demand_planning",
                table: "demand_sources",
                columns: new[] { "organization_id", "environment_id", "demand_type", "source_reference" },
                unique: true);
        }
    }
}
