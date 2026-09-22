using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesDowntimeExecutionReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "operation_task_id",
                schema: "mes",
                table: "work_center_unavailabilities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MES operation task public id associated with the downtime window, when available.");

            migrationBuilder.AddColumn<string>(
                name: "work_order_id",
                schema: "mes",
                table: "work_center_unavailabilities",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "MES work order public id associated with the downtime window, when available.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "operation_task_id",
                schema: "mes",
                table: "work_center_unavailabilities");

            migrationBuilder.DropColumn(
                name: "work_order_id",
                schema: "mes",
                table: "work_center_unavailabilities");
        }
    }
}
