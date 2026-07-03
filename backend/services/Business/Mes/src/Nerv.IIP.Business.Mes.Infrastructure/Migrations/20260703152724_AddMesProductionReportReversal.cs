using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesProductionReportReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reversal_reason",
                schema: "mes",
                table: "production_reports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Operator or system reason captured when this report reverses an original production report.");

            migrationBuilder.AddColumn<string>(
                name: "reversed_report_no",
                schema: "mes",
                table: "production_reports",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Original MES production report number reversed by this negative correction report.");

            migrationBuilder.AlterColumn<decimal>(
                name: "consumed_quantity",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Consumed material quantity for this lot; reversal reports store a negative quantity to request Inventory line-side replenishment.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Consumed material quantity for this lot.");

            migrationBuilder.CreateIndex(
                name: "ix_production_reports_scope_reversed_report_no",
                schema: "mes",
                table: "production_reports",
                columns: new[] { "organization_id", "environment_id", "reversed_report_no" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_production_reports_scope_reversed_report_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "reversal_reason",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.DropColumn(
                name: "reversed_report_no",
                schema: "mes",
                table: "production_reports");

            migrationBuilder.AlterColumn<decimal>(
                name: "consumed_quantity",
                schema: "mes",
                table: "production_report_material_consumptions",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                comment: "Consumed material quantity for this lot.",
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldComment: "Consumed material quantity for this lot; reversal reports store a negative quantity to request Inventory line-side replenishment.");
        }
    }
}
