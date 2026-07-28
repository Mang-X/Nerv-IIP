using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTaskTeamAndShiftSemantics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "team_id",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "MasterData team public id (e.g. TEAM-WB-MC-A) handing over the shift; a code, never a display name.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "MasterData team public id handing over the shift.");

            migrationBuilder.AlterColumn<string>(
                name: "shift_id",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "MasterData shift public id (e.g. EARLY / MIDDLE); the shift dimension only, never a team code.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "MasterData shift public id.");

            migrationBuilder.AddColumn<string>(
                name: "team_name",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Display name of the handing-over team captured at handover time; snapshot so the read face needs no MasterData call.");

            migrationBuilder.AlterColumn<string>(
                name: "shift_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData shift public id (e.g. EARLY / MIDDLE) captured by MES dispatch; the shift dimension only, never a team code.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Assigned MasterData shift public id captured by MES dispatch.");

            migrationBuilder.AddColumn<string>(
                name: "team_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData team public id (e.g. TEAM-WB-MC-A) captured by MES dispatch.");

            migrationBuilder.AddColumn<string>(
                name: "team_name",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Display name of the assigned team captured by MES dispatch; snapshot so the read face needs no MasterData call.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "team_name",
                schema: "mes",
                table: "shift_handovers");

            migrationBuilder.DropColumn(
                name: "team_id",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "team_name",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.AlterColumn<string>(
                name: "team_id",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "MasterData team public id handing over the shift.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "MasterData team public id (e.g. TEAM-WB-MC-A) handing over the shift; a code, never a display name.");

            migrationBuilder.AlterColumn<string>(
                name: "shift_id",
                schema: "mes",
                table: "shift_handovers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "MasterData shift public id.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "MasterData shift public id (e.g. EARLY / MIDDLE); the shift dimension only, never a team code.");

            migrationBuilder.AlterColumn<string>(
                name: "shift_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData shift public id captured by MES dispatch.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Assigned MasterData shift public id (e.g. EARLY / MIDDLE) captured by MES dispatch; the shift dimension only, never a team code.");
        }
    }
}
