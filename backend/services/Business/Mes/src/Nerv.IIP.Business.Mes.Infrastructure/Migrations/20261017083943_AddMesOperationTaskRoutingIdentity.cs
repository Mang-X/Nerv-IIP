using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesOperationTaskRoutingIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "routing_code",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Explicit engineering routing code frozen at task creation; null when no authoritative snapshot exists.");

            migrationBuilder.AddColumn<string>(
                name: "routing_revision",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Explicit engineering routing revision frozen at task creation; null when no authoritative snapshot exists.");

            migrationBuilder.AddColumn<string>(
                name: "routing_source_production_version_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Engineering production version that supplied the task creation routing snapshot; null for unknown legacy or manual provenance.");

            migrationBuilder.AddColumn<string>(
                name: "routing_version_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Engineering routing version id frozen at task creation; never inferred from the current production version binding.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "routing_code",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "routing_revision",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "routing_source_production_version_id",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "routing_version_id",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
