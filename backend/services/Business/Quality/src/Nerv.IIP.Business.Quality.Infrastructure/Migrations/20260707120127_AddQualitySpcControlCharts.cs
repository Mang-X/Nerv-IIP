using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualitySpcControlCharts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spc_control_charts",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "SPC control chart aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the SPC chart."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the SPC chart applies."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "SKU code for the measured SPC sequence."),
                    characteristic_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Variable inspection characteristic code used for SPC."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Work center scope for the SPC sequence."),
                    subgroup_size = table.Column<int>(type: "integer", nullable: false, comment: "Xbar-R subgroup size used to calculate locked limits."),
                    center_line = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Locked Xbar center line."),
                    average_range = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Locked average subgroup range."),
                    xbar_upper_control_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Locked Xbar upper control limit."),
                    xbar_lower_control_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Locked Xbar lower control limit."),
                    range_upper_control_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Locked R chart upper control limit."),
                    range_lower_control_limit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Locked R chart lower control limit."),
                    locked = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the current control limits are locked for operational judgment."),
                    limits_calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the locked control limits were calculated."),
                    locked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the limits were locked."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the SPC chart lock record was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the SPC chart lock record was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spc_control_charts", x => x.id);
                },
                comment: "Quality SPC control chart limit locks by SKU, characteristic and work center.");

            migrationBuilder.CreateIndex(
                name: "IX_spc_control_charts_organization_id_environment_id_sku_code_~",
                schema: "quality",
                table: "spc_control_charts",
                columns: new[] { "organization_id", "environment_id", "sku_code", "characteristic_code", "work_center_id", "subgroup_size" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spc_control_charts",
                schema: "quality");
        }
    }
}
