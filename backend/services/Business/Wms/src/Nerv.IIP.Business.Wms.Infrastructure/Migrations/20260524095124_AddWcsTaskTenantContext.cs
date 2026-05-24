using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWcsTaskTenantContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "environment_id",
                schema: "wms",
                table: "wcs_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Environment id.");

            migrationBuilder.AddColumn<string>(
                name: "organization_id",
                schema: "wms",
                table: "wcs_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Organization tenant id.");

            migrationBuilder.Sql(
                """
                UPDATE wms.wcs_tasks AS wcs
                SET organization_id = tasks.organization_id,
                    environment_id = tasks.environment_id
                FROM wms.warehouse_tasks AS tasks
                WHERE wcs.warehouse_task_id = tasks.id
                """);

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_organization_id_environment_id_external_task_id",
                schema: "wms",
                table: "wcs_tasks",
                columns: new[] { "organization_id", "environment_id", "external_task_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wcs_tasks_organization_id_environment_id_external_task_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.DropColumn(
                name: "environment_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.DropColumn(
                name: "organization_id",
                schema: "wms",
                table: "wcs_tasks");
        }
    }
}
