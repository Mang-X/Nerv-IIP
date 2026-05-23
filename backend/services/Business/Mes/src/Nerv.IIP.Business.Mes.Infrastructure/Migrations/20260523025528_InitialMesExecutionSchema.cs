using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMesExecutionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mes");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "mes",
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
                schema: "mes",
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
                schema: "mes",
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
                name: "device_asset_work_center_mappings",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Device asset work center mapping aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Organization tenant id; null means the mapping is global."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Environment id; null means the mapping is global."),
                    device_asset_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Maintenance device asset public id."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData work center public id used by MES scheduling.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_asset_work_center_mappings", x => x.id);
                },
                comment: "MES local mapping from Maintenance device asset public ids to MasterData work center public ids.");

            migrationBuilder.CreateTable(
                name: "schedule_results",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Schedule result aggregate id."),
                    schedule_version = table.Column<int>(type: "integer", nullable: false, comment: "Monotonic schedule version preserving current MES behavior."),
                    trigger = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Business trigger that caused the schedule run."),
                    scheduled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time requested for the schedule run."),
                    assignments_json = table.Column<string>(type: "text", nullable: false, comment: "JSON schedule assignments produced by MES scheduler; producer is MES, consumers are MES/WMS/read APIs, compatibility is append-only fields."),
                    affected_work_order_ids_json = table.Column<string>(type: "text", nullable: false, comment: "JSON affected work order id list produced by MES scheduler; producer is MES, consumers are MES/WMS/read APIs, compatibility is append-only fields.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_results", x => x.id);
                },
                comment: "MES schedule result facts produced by the deterministic rule scheduler.");

            migrationBuilder.CreateTable(
                name: "work_center_unavailabilities",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work center unavailability aggregate id."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData work center public id unavailable for scheduling."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Organization tenant id; null means the scheduling constraint is global."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Environment id; null means the scheduling constraint is global."),
                    from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC start of the unavailable window."),
                    to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC end of the unavailable window; null means still unavailable."),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Business reason for the scheduling constraint."),
                    device_asset_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Maintenance device asset public id that produced the unavailable window, when applicable.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_center_unavailabilities", x => x.id);
                },
                comment: "MES scheduling constraint facts for unavailable work centers from maintenance or manual inputs.");

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the work order execution context."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business work order id unique within organization and environment."),
                    sku_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU public id for the item being produced."),
                    production_version_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "ProductEngineering production version public id; MES does not duplicate engineering facts."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Planned production quantity."),
                    priority = table.Column<int>(type: "integer", nullable: false, comment: "Scheduling priority; rush work orders use a high priority value."),
                    due_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC due time used by the deterministic rule scheduler."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "MES work order lifecycle status."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the MES work order fact was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_orders", x => x.id);
                    table.UniqueConstraint("ak_work_orders_scope_work_order", x => new { x.organization_id, x.environment_id, x.work_order_id });
                },
                comment: "MES durable work orders created from business demand and ProductEngineering production version references.");

            migrationBuilder.CreateTable(
                name: "finished_goods_receipt_requests",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Finished goods receipt request aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the receipt request."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES business work order id that produced finished goods."),
                    sku_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU public id to receive."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Finished goods quantity requested for receipt."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code for the receipt quantity."),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when MES requested finished goods receipt.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finished_goods_receipt_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipt_requests_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "MES finished goods receipt request facts exposed for WMS or inventory movement boundaries.");

            migrationBuilder.CreateTable(
                name: "operation_tasks",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Operation task aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the operation execution context."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES business work order id this operation belongs to."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business operation task id unique within organization and environment."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Operation lifecycle status used by the scheduler."),
                    operation_sequence = table.Column<int>(type: "integer", nullable: false, comment: "Routing operation sequence used for deterministic scheduling order."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Primary MasterData work center public id."),
                    alternative_work_center_ids = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Pipe-delimited alternate work center public ids copied from routing snapshot."),
                    earliest_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Earliest UTC start time allowed for this operation."),
                    duration_ticks = table.Column<long>(type: "bigint", nullable: false, comment: "Operation duration stored as .NET ticks for deterministic scheduler reconstruction."),
                    existing_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Existing UTC start time for in-progress operation preservation."),
                    existing_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Existing UTC end time for in-progress operation preservation."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the operation task fact was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_tasks", x => x.id);
                    table.UniqueConstraint("ak_operation_tasks_scope_task", x => new { x.organization_id, x.environment_id, x.operation_task_id });
                    table.ForeignKey(
                        name: "fk_operation_tasks_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "MES operation task facts created from routing step snapshots for scheduling and execution tracking.");

            migrationBuilder.CreateTable(
                name: "production_reports",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Production report aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the production report."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES business work order id reported against."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES operation task id reported against."),
                    good_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Good quantity reported for the operation."),
                    scrap_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Scrap quantity reported for the operation."),
                    completes_operation = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether this report marks the operation as completed."),
                    reported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when production was reported.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_production_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_production_reports_operation_tasks",
                        columns: x => new { x.organization_id, x.environment_id, x.operation_task_id },
                        principalSchema: "mes",
                        principalTable: "operation_tasks",
                        principalColumns: new[] { "organization_id", "environment_id", "operation_task_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_production_reports_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "MES production report facts recording good and scrap quantities for operation execution.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "mes",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "mes",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "mes",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "mes",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "ix_asset_wc_mapping_scope_asset",
                schema: "mes",
                table: "device_asset_work_center_mappings",
                columns: new[] { "organization_id", "environment_id", "device_asset_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_receipt_requests_scope_work_order_fk",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_requests_scope_work_order_sku_time",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "sku_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_requests_work_order_id",
                schema: "mes",
                table: "finished_goods_receipt_requests",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_operation_tasks_scope_work_order_fk",
                schema: "mes",
                table: "operation_tasks",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_operation_tasks_scope_work_order_seq",
                schema: "mes",
                table: "operation_tasks",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "operation_sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_operation_tasks_work_order_id",
                schema: "mes",
                table: "operation_tasks",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_reports_operation_task_id",
                schema: "mes",
                table: "production_reports",
                column: "operation_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_production_reports_scope_operation_fk",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "operation_task_id" });

            migrationBuilder.CreateIndex(
                name: "ix_production_reports_scope_operation_time",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "operation_task_id", "reported_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_production_reports_scope_work_order_fk",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "work_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_production_reports_work_order_id",
                schema: "mes",
                table: "production_reports",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_schedule_results_trigger_time",
                schema: "mes",
                table: "schedule_results",
                columns: new[] { "trigger", "scheduled_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_results_version",
                schema: "mes",
                table: "schedule_results",
                column: "schedule_version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_wc_unavailability_scope_asset_open",
                schema: "mes",
                table: "work_center_unavailabilities",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_wc_unavailability_scope_center_window",
                schema: "mes",
                table: "work_center_unavailabilities",
                columns: new[] { "organization_id", "environment_id", "work_center_id", "from_utc", "to_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_scope_sku_due",
                schema: "mes",
                table: "work_orders",
                columns: new[] { "organization_id", "environment_id", "sku_id", "due_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_work_order_id",
                schema: "mes",
                table: "work_orders",
                column: "work_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "device_asset_work_center_mappings",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "finished_goods_receipt_requests",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "production_reports",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "schedule_results",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "work_center_unavailabilities",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "operation_tasks",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "mes");
        }
    }
}
