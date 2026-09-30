using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesWorkOrderRushFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "priority",
                schema: "mes",
                table: "work_orders",
                type: "integer",
                nullable: false,
                comment: "Business priority, independent of the rush work order flag.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Scheduling priority; rush work orders use a high priority value.");

            migrationBuilder.AddColumn<bool>(
                name: "is_rush",
                schema: "mes",
                table: "work_orders",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Explicit rush work order flag; not inferred from business priority.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_rush",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.AlterColumn<int>(
                name: "priority",
                schema: "mes",
                table: "work_orders",
                type: "integer",
                nullable: false,
                comment: "Scheduling priority; rush work orders use a high priority value.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Business priority, independent of the rush work order flag.");
        }
    }
}
