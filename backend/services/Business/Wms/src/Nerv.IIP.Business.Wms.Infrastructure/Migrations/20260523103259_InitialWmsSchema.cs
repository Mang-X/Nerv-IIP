using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWmsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "wms");

            migrationBuilder.CreateTable(
                name: "CAPLock",
                schema: "wms",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPLock", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CAPPublishedMessage",
                schema: "wms",
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
                    table.PrimaryKey("PK_CAPPublishedMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CAPReceivedMessage",
                schema: "wms",
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
                    table.PrimaryKey("PK_CAPReceivedMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "count_executions",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Count execution id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    count_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Count execution number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Counted warehouse location."),
                    expected_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Expected count quantity provided by upstream boundary."),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Actual counted quantity."),
                    variance_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Counted quantity minus expected quantity."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Count execution status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC completion time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_count_executions", x => x.id);
                },
                comment: "WMS count execution and variance output facts.");

            migrationBuilder.CreateTable(
                name: "inbound_orders",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inbound order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    inbound_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WMS inbound order number."),
                    source_document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Producer document type."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Producer document id."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inbound execution status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC completion time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_orders", x => x.id);
                },
                comment: "WMS inbound execution order header and source document reference.");

            migrationBuilder.CreateTable(
                name: "inventory_movement_requests",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inventory movement request id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    movement_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Inventory movement type requested by WMS."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "WMS source document id."),
                    source_document_line_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "WMS source document line id."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Producer-stable idempotency key."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory public stock location code."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot number."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status dimension."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Owner type dimension."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional owner id."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Movement quantity requested from Inventory."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Posting status for the Inventory request."),
                    inventory_movement_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Public Inventory movement id returned after posting."),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Inventory posting failure code."),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Inventory posting failure message."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC posted time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movement_requests", x => x.id);
                },
                comment: "WMS-owned metadata for Inventory movement posting requests.");

            migrationBuilder.CreateTable(
                name: "outbound_orders",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Outbound order aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    outbound_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WMS outbound order number."),
                    source_document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Producer document type."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Producer document id."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Outbound execution status."),
                    pack_review_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Pack review reference."),
                    pack_review_passed = table.Column<bool>(type: "boolean", nullable: true, comment: "Pack review pass flag."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC completion time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_orders", x => x.id);
                },
                comment: "WMS outbound execution order header and pack review facts.");

            migrationBuilder.CreateTable(
                name: "warehouse_tasks",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse task id."),
                    task_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Task type: putaway or picking."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    task_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Warehouse task number."),
                    source_order_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WMS source order number."),
                    source_order_line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WMS source order line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    from_location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Task source location."),
                    to_location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Task target location."),
                    planned_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Planned execution quantity."),
                    executed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Executed task quantity."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Warehouse task status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC completion time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_tasks", x => x.id);
                },
                comment: "WMS putaway and picking warehouse tasks.");

            migrationBuilder.CreateTable(
                name: "wcs_tasks",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "WCS task id."),
                    warehouse_task_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "WMS warehouse task id."),
                    adapter_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WCS adapter type."),
                    external_task_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "External WCS task id."),
                    payload_json = table.Column<string>(type: "text", nullable: false, comment: "Outbound adapter payload JSON."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "WCS task status."),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, comment: "Dispatch attempt count."),
                    completion_payload_json = table.Column<string>(type: "text", nullable: true, comment: "Completion callback payload JSON."),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "WCS failure diagnostic code."),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "WCS failure diagnostic message."),
                    dispatched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC dispatch time."),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC completion time."),
                    failed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC failure time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wcs_tasks", x => x.id);
                },
                comment: "WCS adapter task mapping, lifecycle and diagnostics.");

            migrationBuilder.CreateTable(
                name: "inbound_order_lines",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Inbound order line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    received_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Inbound received quantity."),
                    staging_location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Staging location for received stock."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot number."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status dimension."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Owner type dimension."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional owner id."),
                    inbound_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning inbound order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbound_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_inbound_order_lines_inbound_orders_inbound_order_id",
                        column: x => x.inbound_order_id,
                        principalSchema: "wms",
                        principalTable: "inbound_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "WMS inbound order execution lines.");

            migrationBuilder.CreateTable(
                name: "outbound_order_lines",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Outbound order line id."),
                    line_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source line number."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code."),
                    requested_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Outbound requested quantity."),
                    pick_location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Pick location for outbound stock."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot number."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status dimension."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Owner type dimension."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional owner id."),
                    outbound_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning outbound order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_order_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbound_order_lines_outbound_orders_outbound_order_id",
                        column: x => x.outbound_order_id,
                        principalSchema: "wms",
                        principalTable: "outbound_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "WMS outbound order execution lines.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "wms",
                table: "CAPPublishedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "wms",
                table: "CAPPublishedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "wms",
                table: "CAPReceivedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "wms",
                table: "CAPReceivedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_inbound_order_lines_inbound_order_id",
                schema: "wms",
                table: "inbound_order_lines",
                column: "inbound_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_inbound_orders_organization_id_environment_id_inbound_order~",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "inbound_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movement_requests_organization_id_environment_id_~",
                schema: "wms",
                table: "inventory_movement_requests",
                columns: new[] { "organization_id", "environment_id", "source_document_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbound_order_lines_outbound_order_id",
                schema: "wms",
                table: "outbound_order_lines",
                column: "outbound_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_orders_organization_id_environment_id_outbound_ord~",
                schema: "wms",
                table: "outbound_orders",
                columns: new[] { "organization_id", "environment_id", "outbound_order_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_warehouse_tasks_organization_id_environment_id_task_no",
                schema: "wms",
                table: "warehouse_tasks",
                columns: new[] { "organization_id", "environment_id", "task_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_external_task_id",
                schema: "wms",
                table: "wcs_tasks",
                column: "external_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_warehouse_task_id_adapter_type",
                schema: "wms",
                table: "wcs_tasks",
                columns: new[] { "warehouse_task_id", "adapter_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CAPLock",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "CAPPublishedMessage",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "CAPReceivedMessage",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "count_executions",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "inbound_order_lines",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "inventory_movement_requests",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "outbound_order_lines",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "warehouse_tasks",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "wcs_tasks",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "inbound_orders",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "outbound_orders",
                schema: "wms");
        }
    }
}
