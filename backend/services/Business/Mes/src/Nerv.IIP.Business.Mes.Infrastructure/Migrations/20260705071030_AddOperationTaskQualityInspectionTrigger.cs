using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTaskQualityInspectionTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "planned_quantity",
                schema: "mes",
                table: "operation_tasks",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m,
                comment: "Planned operation quantity used as the default good quantity for operation completion inspection triggers.");

            migrationBuilder.AddColumn<bool>(
                name: "requires_quality_inspection",
                schema: "mes",
                table: "operation_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this operation completion should trigger a Quality inspection task.");

            migrationBuilder.AddColumn<string>(
                name: "sku_code",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Produced SKU code copied from the MES work order for downstream inspection triggers.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                comment: "Produced quantity unit of measure for downstream inspection triggers.");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "mes",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending or Replayed.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "planned_quantity",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "requires_quality_inspection",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "sku_code",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "mes",
                table: "integration_event_dead_letters",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Dead-letter status: Pending or Replayed.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Dead-letter status: Pending, Replayed, Failed, or Ignored.");
        }
    }
}
