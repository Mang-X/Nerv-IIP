using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesQualityAndShiftHandoverFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "defect_records",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Defect record aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the defect record."),
                    defect_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES defect record number allocated by the service numbering counter."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES work order id that produced the defect."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional MES operation task id that produced the defect."),
                    defect_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Defect reason or code captured by MES."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Defect quantity captured by MES."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "MES defect lifecycle status."),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the defect was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_defect_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_defect_records_work_orders",
                        columns: x => new { x.organization_id, x.environment_id, x.work_order_id },
                        principalSchema: "mes",
                        principalTable: "work_orders",
                        principalColumns: new[] { "organization_id", "environment_id", "work_order_id" },
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "MES in-process defect facts recorded against work orders and optional operation tasks.");

            migrationBuilder.CreateTable(
                name: "shift_handovers",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shift handover aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id for the shift handover."),
                    handover_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES shift handover number allocated by the service numbering counter."),
                    shift_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData shift public id."),
                    team_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData team public id handing over the shift."),
                    handover_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Shift handover lifecycle status."),
                    open_issue_count = table.Column<int>(type: "integer", nullable: false, comment: "Number of open issues captured when the handover was created."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the handover was created."),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the receiving team accepted the handover.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handovers", x => x.id);
                },
                comment: "MES shift handover facts carrying open production, quality, material and equipment issues between teams.");

            migrationBuilder.CreateIndex(
                name: "ix_defect_records_scope_operation_time",
                schema: "mes",
                table: "defect_records",
                columns: new[] { "organization_id", "environment_id", "operation_task_id", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_defect_records_scope_work_order_time",
                schema: "mes",
                table: "defect_records",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_defect_records_scope_defect_no",
                schema: "mes",
                table: "defect_records",
                columns: new[] { "organization_id", "environment_id", "defect_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_handovers_scope_shift_time",
                schema: "mes",
                table: "shift_handovers",
                columns: new[] { "organization_id", "environment_id", "shift_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_shift_handovers_scope_handover_no",
                schema: "mes",
                table: "shift_handovers",
                columns: new[] { "organization_id", "environment_id", "handover_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "defect_records",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "shift_handovers",
                schema: "mes");
        }
    }
}
