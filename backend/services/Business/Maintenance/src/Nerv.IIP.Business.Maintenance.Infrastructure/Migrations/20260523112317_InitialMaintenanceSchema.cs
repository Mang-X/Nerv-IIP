using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMaintenanceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maintenance");

            migrationBuilder.CreateTable(
                name: "CAPLock",
                schema: "maintenance",
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
                schema: "maintenance",
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
                schema: "maintenance",
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
                name: "downtime_reasons",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Downtime reason id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Downtime reason code."),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Downtime reason description.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_downtime_reasons", x => x.id);
                },
                comment: "Maintenance downtime reason reference facts owned by Maintenance.");

            migrationBuilder.CreateTable(
                name: "maintenance_inspections",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Maintenance inspection id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    maintenance_plan_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Referenced maintenance plan id, if applicable."),
                    maintenance_work_order_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Referenced maintenance work order id, if applicable."),
                    inspector = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Inspector actor."),
                    result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Inspection result."),
                    inspected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC inspection time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_inspections", x => x.id);
                },
                comment: "Maintenance inspection facts linked to a plan or work order.");

            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Maintenance plan id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MasterData device asset public id or code reference."),
                    plan_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Maintenance plan code."),
                    interval = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Explicit maintenance interval expression, for example ISO-8601 P7D."),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false, comment: "Plan start date."),
                    owner = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Plan owner or team."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_plans", x => x.id);
                },
                comment: "Preventive maintenance plan schedule facts.");

            migrationBuilder.CreateTable(
                name: "maintenance_work_orders",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Maintenance work order id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MasterData device asset public id or code reference."),
                    priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Maintenance priority."),
                    source_alarm_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "IndustrialTelemetry alarm id that opened this work order, when applicable."),
                    opened_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Actor or source that opened the work order."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Maintenance work order lifecycle status."),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when work order was opened."),
                    asset_unavailable = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether Maintenance marked the device unavailable."),
                    asset_unavailable_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Reason published with maintenance.AssetUnavailable."),
                    asset_unavailable_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC start time of asset unavailability."),
                    completion_result = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Maintenance completion result."),
                    downtime_reason_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Downtime reason attribution code."),
                    downtime_minutes = table.Column<int>(type: "integer", nullable: true, comment: "Attributed downtime minutes."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC completion time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_work_orders", x => x.id);
                },
                comment: "Maintenance work orders, alarm references, asset availability and completion facts.");

            migrationBuilder.CreateTable(
                name: "maintenance_work_order_spare_part_lines",
                schema: "maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Spare part line id."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Referenced spare part SKU code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Required spare part quantity."),
                    uom_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Optional spare part unit of measure code."),
                    maintenance_work_order_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning maintenance work order id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_work_order_spare_part_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_work_order_spare_part_lines_maintenance_work_or~",
                        column: x => x.maintenance_work_order_id,
                        principalSchema: "maintenance",
                        principalTable: "maintenance_work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Spare part demand lines recorded by Maintenance; not inventory balances.");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "maintenance",
                table: "CAPPublishedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "maintenance",
                table: "CAPPublishedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "maintenance",
                table: "CAPReceivedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "maintenance",
                table: "CAPReceivedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_downtime_reasons_organization_id_environment_id_reason_code",
                schema: "maintenance",
                table: "downtime_reasons",
                columns: new[] { "organization_id", "environment_id", "reason_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_plans_organization_id_environment_id_plan_code",
                schema: "maintenance",
                table: "maintenance_plans",
                columns: new[] { "organization_id", "environment_id", "plan_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_work_order_spare_part_lines_maintenance_work_or~",
                schema: "maintenance",
                table: "maintenance_work_order_spare_part_lines",
                column: "maintenance_work_order_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_work_orders_organization_id_environment_id_sour~",
                schema: "maintenance",
                table: "maintenance_work_orders",
                columns: new[] { "organization_id", "environment_id", "source_alarm_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CAPLock",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "CAPPublishedMessage",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "CAPReceivedMessage",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "downtime_reasons",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_inspections",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_plans",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_work_order_spare_part_lines",
                schema: "maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_work_orders",
                schema: "maintenance");
        }
    }
}
