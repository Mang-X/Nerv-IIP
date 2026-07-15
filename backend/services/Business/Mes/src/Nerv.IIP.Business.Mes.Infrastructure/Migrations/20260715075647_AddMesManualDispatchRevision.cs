using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesManualDispatchRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_active_manual_dispatch",
                schema: "mes",
                table: "operation_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the operation currently owns an active MES manual-device dispatch lock.");

            migrationBuilder.AddColumn<long>(
                name: "manual_dispatch_revision",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Monotonic MES manual-device dispatch lifecycle revision.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_active_manual_dispatch",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "manual_dispatch_revision",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
