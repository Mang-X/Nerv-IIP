using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastInputsAndMrpExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_inputs",
                schema: "demand_planning",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Forecast input aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Tenant organization id that owns the forecast."),
                    environment_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Planning environment id."),
                    forecast_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Business forecast reference unique in the planning scope."),
                    sku_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Forecast SKU code snapshot."),
                    uom_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Forecast quantity unit of measure snapshot."),
                    site_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Forecast site code snapshot."),
                    period_start_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Forecast period start date."),
                    period_end_date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Forecast period end date and default MRP requirement date for remaining forecast."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Forecast quantity before order consumption."),
                    backward_consumption_days = table.Column<int>(type: "integer", nullable: false, comment: "Days before the forecast period that sales orders may consume this forecast."),
                    forward_consumption_days = table.Column<int>(type: "integer", nullable: false, comment: "Days after the forecast period that sales orders may consume this forecast."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the forecast input was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the forecast input was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_inputs", x => x.id);
                },
                comment: "DemandPlanning owned forecast input facts consumed by MRP.");

            migrationBuilder.CreateIndex(
                name: "IX_forecast_inputs_organization_id_environment_id_forecast_ref~",
                schema: "demand_planning",
                table: "forecast_inputs",
                columns: new[] { "organization_id", "environment_id", "forecast_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_forecast_inputs_organization_id_environment_id_sku_code_sit~",
                schema: "demand_planning",
                table: "forecast_inputs",
                columns: new[] { "organization_id", "environment_id", "sku_code", "site_code", "period_end_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_inputs",
                schema: "demand_planning");
        }
    }
}
