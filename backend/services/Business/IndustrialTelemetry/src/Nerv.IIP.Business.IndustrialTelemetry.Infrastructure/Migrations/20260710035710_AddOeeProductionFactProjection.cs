using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOeeProductionFactProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oee_production_facts",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "OEE production fact aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    source_report_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES production report number used as the idempotent projection key."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work center snapshot for the reported operation."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MES assigned device asset used to scope OEE."),
                    good_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Reported accepted output quantity; reversals are negative."),
                    scrap_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Reported scrap output quantity; reversals are negative."),
                    rework_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Reported rework output quantity; reversals are negative."),
                    uom_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Output quantity unit copied from the MES operation snapshot."),
                    theoretical_rate_per_hour = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true, comment: "Expected output per productive hour from the MES operation planning snapshot."),
                    reported_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC instant assigned to the production report.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oee_production_facts", x => x.id);
                },
                comment: "MES production-report facts projected for explainable IndustrialTelemetry OEE calculations.");

            migrationBuilder.CreateIndex(
                name: "IX_oee_production_facts_organization_id_environment_id_device_~",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "reported_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_oee_production_facts_organization_id_environment_id_source_~",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                columns: new[] { "organization_id", "environment_id", "source_report_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oee_production_facts",
                schema: "industrial_telemetry");
        }
    }
}
