using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialDemandPlanningSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "demand_planning");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "demand_planning",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_locks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cap_published_messages",
                schema: "demand_planning",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_published_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_received_messages",
                schema: "demand_planning",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_received_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "demand_sources",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Demand source aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id that owns the demand."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment id, such as dev, test, or production planning space."),
                    demand_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Demand type: forecast, sales-order, safety-stock, or manual."),
                    source_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "External or manual source reference unique in the planning scope."),
                    sku_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Demanded finished-good SKU code snapshot."),
                    uom_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Demand quantity unit of measure snapshot."),
                    site_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Demand site code snapshot."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive demand quantity."),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Demand due date bucket."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the demand source was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the demand source was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_demand_sources", x => x.id);
                },
                comment: "DemandPlanning owned demand source facts for MPS and MRP input.");

            migrationBuilder.CreateTable(
                name: "master_production_schedules",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Master production schedule aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id that owns the MPS bucket."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment id."),
                    sku_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Scheduled SKU code snapshot."),
                    uom_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Scheduled quantity unit of measure snapshot."),
                    site_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Scheduled site code snapshot."),
                    bucket_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Daily MPS bucket date."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive scheduled quantity.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_production_schedules", x => x.id);
                },
                comment: "DemandPlanning owned daily master production schedule buckets.");

            migrationBuilder.CreateTable(
                name: "mrp_runs",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MRP run aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id that owns the MRP run."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment id."),
                    horizon_start = table.Column<DateOnly>(type: "date", nullable: false, comment: "MRP calculation horizon start date."),
                    horizon_end = table.Column<DateOnly>(type: "date", nullable: false, comment: "MRP calculation horizon end date."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "MRP run status."),
                    production_engineering_snapshot_source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Source adapter used for released version and MBOM snapshots."),
                    inventory_snapshot_source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Source adapter used for inventory availability snapshots."),
                    demand_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of demand source snapshots included in the run."),
                    availability_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of availability snapshots included in the run."),
                    suggestion_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of planning suggestions created by the run."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the MRP run was created."),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when calculation started."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when calculation completed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_runs", x => x.id);
                },
                comment: "DemandPlanning MRP calculation run headers and input snapshot metadata.");

            migrationBuilder.CreateTable(
                name: "planning_suggestions",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Planning suggestion aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id that owns the suggestion."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment id."),
                    mrp_run_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning MRP run id; no cross-service foreign key."),
                    suggestion_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Suggestion type such as planned-purchase or planned-work-order."),
                    sku_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Suggested SKU code snapshot."),
                    uom_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Suggested quantity unit of measure snapshot."),
                    site_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Suggested site code snapshot."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Positive suggested quantity."),
                    required_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Required date for downstream procurement or production."),
                    reason_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "MRP reason code explaining why the suggestion exists."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Suggestion lifecycle status."),
                    accepted_downstream_service = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Downstream service that accepted the suggestion."),
                    accepted_downstream_document_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Downstream document type that accepted the suggestion."),
                    accepted_downstream_document_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "Downstream document id that accepted the suggestion."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the suggestion was created."),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC timestamp when the suggestion was accepted.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_planning_suggestions", x => x.id);
                },
                comment: "DemandPlanning generated planned purchase and planned work-order suggestions.");

            migrationBuilder.CreateTable(
                name: "mrp_pegging_links",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MRP pegging link entity id."),
                    planning_suggestion_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning planning suggestion id."),
                    pegging_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Pegging type, such as demand or bom-component."),
                    demand_source_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Demand source reference that caused the suggestion."),
                    parent_sku_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Parent SKU code in the MRP explanation."),
                    component_sku_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Component SKU code in the MRP explanation when relevant."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Pegged quantity attributable to the demand."),
                    production_version_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "ProductEngineering production version snapshot reference."),
                    manufacturing_bom_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "ProductEngineering manufacturing BOM snapshot reference."),
                    routing_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true, comment: "ProductEngineering routing snapshot reference.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mrp_pegging_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_mrp_pegging_links_planning_suggestions_planning_suggestion_~",
                        column: x => x.planning_suggestion_id,
                        principalSchema: "demand_planning",
                        principalTable: "planning_suggestions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "DemandPlanning MRP pegging links from suggestions back to demand and input snapshots.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "demand_planning",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "demand_planning",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "demand_planning",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "demand_planning",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_demand_sources_organization_id_environment_id_demand_type_s~",
                schema: "demand_planning",
                table: "demand_sources",
                columns: new[] { "organization_id", "environment_id", "demand_type", "source_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_master_production_schedules_organization_id_environment_id_~",
                schema: "demand_planning",
                table: "master_production_schedules",
                columns: new[] { "organization_id", "environment_id", "sku_code", "site_code", "bucket_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mrp_pegging_links_planning_suggestion_id",
                schema: "demand_planning",
                table: "mrp_pegging_links",
                column: "planning_suggestion_id");

            migrationBuilder.CreateIndex(
                name: "IX_planning_suggestions_organization_id_environment_id_mrp_run~",
                schema: "demand_planning",
                table: "planning_suggestions",
                columns: new[] { "organization_id", "environment_id", "mrp_run_id", "suggestion_type", "sku_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "demand_sources",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "master_production_schedules",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "mrp_pegging_links",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "mrp_runs",
                schema: "demand_planning");

            migrationBuilder.DropTable(
                name: "planning_suggestions",
                schema: "demand_planning");
        }
    }
}
