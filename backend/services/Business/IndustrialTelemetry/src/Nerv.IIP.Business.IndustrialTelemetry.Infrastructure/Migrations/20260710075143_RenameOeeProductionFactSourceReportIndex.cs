using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameOeeProductionFactSourceReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_oee_production_facts_organization_id_environment_id_source_~",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                newName: "ux_oee_production_facts_scope_source_report_no");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ux_oee_production_facts_scope_source_report_no",
                schema: "industrial_telemetry",
                table: "oee_production_facts",
                newName: "IX_oee_production_facts_organization_id_environment_id_source_~");
        }
    }
}
