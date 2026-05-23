using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "inventory",
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
                schema: "inventory",
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
                schema: "inventory",
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
                name: "stock_count_adjustments",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stock count adjustment aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the count adjustment."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the count adjustment was confirmed."),
                    count_task_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business count task code that produced the adjustment."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Idempotency key supplied when confirming the count variance."),
                    movement_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Stock movement id generated for the count variance."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code for counted quantity."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory stock location code."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot or batch number dimension."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number dimension."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status carried by adjusted stock."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stock ownership type such as company, customer or supplier."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional public owner reference id."),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Confirmed physical counted quantity."),
                    variance_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Confirmed variance quantity against ledger on-hand."),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the count adjustment was confirmed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_count_adjustments", x => x.id);
                },
                comment: "Inventory stock count adjustment facts generated from confirmed count variances.");

            migrationBuilder.CreateTable(
                name: "stock_count_tasks",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stock count task aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the count task."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the count task was created."),
                    count_task_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business count task code."),
                    ledger_organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization id of the ledger snapshot."),
                    ledger_environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id of the ledger snapshot."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code for counted quantity."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory stock location code."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot or batch number dimension."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number dimension."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status carried by counted stock."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stock ownership type such as company, customer or supplier."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional public owner reference id."),
                    expected_ledger_version = table.Column<long>(type: "bigint", nullable: false, comment: "Ledger version captured when the count task was created."),
                    counted_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Confirmed physical counted quantity."),
                    variance_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Confirmed variance quantity against ledger on-hand."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Stock count task lifecycle status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the count task was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the count task was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_count_tasks", x => x.id);
                },
                comment: "Inventory stock count tasks, expected ledger version snapshots and confirmed variances.");

            migrationBuilder.CreateTable(
                name: "stock_ledgers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stock ledger aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the ledger balance."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the balance is valid."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code for the quantity."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory stock location code."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot or batch number dimension."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number dimension."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status carried by stock facts."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stock ownership type such as company, customer or supplier."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional public owner reference id."),
                    on_hand_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Current on-hand stock quantity."),
                    reserved_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Current reserved quantity; always zero in Inventory MVP."),
                    ledger_version = table.Column<long>(type: "bigint", nullable: false, comment: "Monotonic ledger version incremented when movements are applied."),
                    row_version = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version for concurrent stock balance updates."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the ledger was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_ledgers", x => x.id);
                },
                comment: "Inventory current stock ledger balances by SKU, UOM, site, location, lot, serial, quality and owner dimensions.");

            migrationBuilder.CreateTable(
                name: "stock_locations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stock location aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the location."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the location is valid."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business stock location code."),
                    location_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Location type such as warehouse, zone, bin or logical."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code associated with this location."),
                    parent_location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional parent stock location code."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Location lifecycle status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the location was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the location was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_locations", x => x.id);
                },
                comment: "Inventory stock locations such as warehouse, zone, bin or logical stock area.");

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Stock movement aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the movement."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the movement was posted."),
                    movement_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Movement type: inbound, outbound, transfer, adjustment or count-adjustment."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source service that requested the movement."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source document id supplied by the producer."),
                    source_document_line_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional source document line id supplied by the producer."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Idempotency key unique within organization, environment, source service and source document."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData SKU code."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "MasterData unit of measure code for the quantity."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code."),
                    location_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Inventory stock location code."),
                    lot_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional lot or batch number dimension."),
                    serial_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional serial number dimension."),
                    quality_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quality status carried by stock facts."),
                    owner_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stock ownership type such as company, customer or supplier."),
                    owner_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional public owner reference id."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Signed movement quantity."),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the movement was posted.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.id);
                },
                comment: "Append-only Inventory stock movement facts with source document and idempotency key.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "inventory",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "inventory",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "inventory",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "inventory",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_adjustments_organization_id_environment_id_coun~",
                schema: "inventory",
                table: "stock_count_adjustments",
                columns: new[] { "organization_id", "environment_id", "count_task_code", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_adjustments_organization_id_environment_id_sku_~",
                schema: "inventory",
                table: "stock_count_adjustments",
                columns: new[] { "organization_id", "environment_id", "sku_code", "site_code", "location_code", "confirmed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_tasks_organization_id_environment_id_count_task~",
                schema: "inventory",
                table: "stock_count_tasks",
                columns: new[] { "organization_id", "environment_id", "count_task_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_tasks_organization_id_environment_id_status_sit~",
                schema: "inventory",
                table: "stock_count_tasks",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "location_code" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_organization_id_environment_id_sku_code_uom_c~",
                schema: "inventory",
                table: "stock_ledgers",
                columns: new[] { "organization_id", "environment_id", "sku_code", "uom_code", "site_code", "location_code", "lot_no", "serial_no", "quality_status", "owner_type", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_locations_organization_id_environment_id_location_code",
                schema: "inventory",
                table: "stock_locations",
                columns: new[] { "organization_id", "environment_id", "location_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_locations_organization_id_environment_id_site_code_st~",
                schema: "inventory",
                table: "stock_locations",
                columns: new[] { "organization_id", "environment_id", "site_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_organization_id_environment_id_sku_code_sit~",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "organization_id", "environment_id", "sku_code", "site_code", "location_code", "posted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_organization_id_environment_id_source_servi~",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "organization_id", "environment_id", "source_service", "source_document_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_count_adjustments",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_count_tasks",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_ledgers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_locations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");
        }
    }
}
