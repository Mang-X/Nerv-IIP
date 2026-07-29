using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialRequirementSnapshotOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "material_requirement_snapshot_evaluated_at_utc",
                schema: "mes",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when MES last proved the material requirement snapshot outcome.");

            migrationBuilder.AddColumn<string>(
                name: "material_requirement_snapshot_production_version_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Production version id whose material requirement snapshot outcome was proved; it must match the current work order version.");

            migrationBuilder.AddColumn<string>(
                name: "material_requirement_snapshot_status",
                schema: "mes",
                table: "work_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                comment: "Latest durable material requirement snapshot outcome: captured or no-requirements; null means readiness is not proven.");

            // Existing material requirement rows cannot be attributed to the work order's
            // current production version with certainty. Keep all three proof columns null
            // so upgraded work orders fail closed until a governed snapshot is captured.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "material_requirement_snapshot_evaluated_at_utc",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "material_requirement_snapshot_production_version_id",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "material_requirement_snapshot_status",
                schema: "mes",
                table: "work_orders");
        }
    }
}
