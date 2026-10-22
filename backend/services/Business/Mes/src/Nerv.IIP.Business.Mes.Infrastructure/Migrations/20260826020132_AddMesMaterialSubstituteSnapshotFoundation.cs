using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialSubstituteSnapshotFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "material_requirement_snapshot_production_version_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Production version provenance for the frozen material requirement outcome; it normally matches the current work order version, while a released engineering-change auto-rebind retains the release version.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Production version id whose material requirement snapshot outcome was proved; it must match the current work order version.");

            migrationBuilder.AddColumn<string>(
                name: "substitute_material_ids_json",
                schema: "mes",
                table: "material_requirements",
                type: "text",
                nullable: false,
                defaultValue: "[]",
                comment: "JSON array of normalized substitute material ids produced by the MES MBOM snapshot adapter; consumers are MES readiness and material issue flows; compatibility is an append-only candidate list.");

            migrationBuilder.AddColumn<string>(
                name: "substituted_material_id",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional primary material SKU replaced by the actually issued material; reserved for substitute issue audit activation.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "substitute_material_ids_json",
                schema: "mes",
                table: "material_requirements");

            migrationBuilder.DropColumn(
                name: "substituted_material_id",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.AlterColumn<string>(
                name: "material_requirement_snapshot_production_version_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Production version id whose material requirement snapshot outcome was proved; it must match the current work order version.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Production version provenance for the frozen material requirement outcome; it normally matches the current work order version, while a released engineering-change auto-rebind retains the release version.");
        }
    }
}
