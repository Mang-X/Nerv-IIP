using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    public partial class AddMesBillableMachineTimeFacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "machine_time_evidence_unavailable", schema: "mes", table: "operation_tasks",
                type: "boolean", nullable: false, defaultValue: true,
                comment: "Whether the current execution window cannot produce billable machine ticks because device evidence was absent or changed.");

            migrationBuilder.AddColumn<string>(
                name: "machine_time_execution_device_asset_id", schema: "mes", table: "operation_tasks",
                type: "character varying(100)", maxLength: 100, nullable: true,
                comment: "Single device asset frozen when the current execution window started; null when no authoritative device was present.");

            migrationBuilder.AddColumn<long>(
                name: "billable_machine_ticks", schema: "mes", table: "operation_actual_time_settlements",
                type: "bigint", nullable: true,
                comment: "Nullable nonnegative billable machine duration; zero is authoritative only while status is Available.");

            migrationBuilder.AddColumn<string>(
                name: "device_asset_id", schema: "mes", table: "operation_actual_time_settlements",
                type: "character varying(100)", maxLength: 100, nullable: true,
                comment: "Single MasterData device asset frozen for available billable machine time; null otherwise.");

            migrationBuilder.AddColumn<string>(
                name: "machine_time_basis_code", schema: "mes", table: "operation_actual_time_settlements",
                type: "character varying(100)", maxLength: 100, nullable: true,
                comment: "Governed calculation basis for available billable machine time; null for non-available facts.");

            migrationBuilder.AddColumn<string>(
                name: "machine_time_status", schema: "mes", table: "operation_actual_time_settlements",
                type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Unavailable",
                comment: "Billable machine-time fact state: Available, NotApplicable, or Unavailable.");

            migrationBuilder.AlterColumn<string>(
                name: "machine_time_status", schema: "mes", table: "operation_actual_time_settlements",
                type: "character varying(32)", maxLength: 32, nullable: false,
                comment: "Billable machine-time fact state: Available, NotApplicable, or Unavailable.",
                oldClrType: typeof(string), oldType: "character varying(32)", oldMaxLength: 32,
                oldDefaultValue: "Unavailable",
                oldComment: "Billable machine-time fact state: Available, NotApplicable, or Unavailable.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_actual_time_settlements_machine_fact", schema: "mes",
                table: "operation_actual_time_settlements",
                sql: "(machine_time_status = 'Available' AND device_asset_id IS NOT NULL AND billable_machine_ticks IS NOT NULL AND billable_machine_ticks >= 0 AND machine_time_basis_code IS NOT NULL AND machine_time_basis_code = 'single-device-active-minus-explicit-pause-v1') OR (machine_time_status IN ('NotApplicable', 'Unavailable') AND device_asset_id IS NULL AND billable_machine_ticks IS NULL AND machine_time_basis_code IS NULL)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_actual_time_settlements_machine_fact", schema: "mes",
                table: "operation_actual_time_settlements");
            migrationBuilder.DropColumn(name: "machine_time_evidence_unavailable", schema: "mes", table: "operation_tasks");
            migrationBuilder.DropColumn(name: "machine_time_execution_device_asset_id", schema: "mes", table: "operation_tasks");
            migrationBuilder.DropColumn(name: "billable_machine_ticks", schema: "mes", table: "operation_actual_time_settlements");
            migrationBuilder.DropColumn(name: "device_asset_id", schema: "mes", table: "operation_actual_time_settlements");
            migrationBuilder.DropColumn(name: "machine_time_basis_code", schema: "mes", table: "operation_actual_time_settlements");
            migrationBuilder.DropColumn(name: "machine_time_status", schema: "mes", table: "operation_actual_time_settlements");
        }
    }
}
