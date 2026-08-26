using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesOperationActualTimeSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "actual_time_settlement_covered_report_nos_json",
                schema: "mes",
                table: "operation_tasks",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                comment: "JSON array of ordinal-sorted production report numbers covered by the active settlement; producer is MES production reporting, consumers are MES settlement/reversal and downstream event converters, compatibility is append-only and readers must ignore unknown future members.");

            migrationBuilder.AddColumn<long>(
                name: "actual_time_settlement_revision",
                schema: "mes",
                table: "operation_tasks",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Monotonic MES actual-time settlement revision; zero means the operation has never emitted a settlement.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_tasks_actual_time_settlement_revision_nonnegative",
                schema: "mes",
                table: "operation_tasks",
                sql: "actual_time_settlement_revision >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_tasks_actual_time_settlement_revision_nonnegative",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "actual_time_settlement_covered_report_nos_json",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "actual_time_settlement_revision",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
