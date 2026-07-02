using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ScopeWcsTaskExternalTaskIdByTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wcs_tasks_external_task_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.DropIndex(
                name: "IX_wcs_tasks_organization_id_environment_id_external_task_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_organization_id_environment_id_external_task_id",
                schema: "wms",
                table: "wcs_tasks",
                columns: new[] { "organization_id", "environment_id", "external_task_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wcs_tasks_organization_id_environment_id_external_task_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_external_task_id",
                schema: "wms",
                table: "wcs_tasks",
                column: "external_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_organization_id_environment_id_external_task_id",
                schema: "wms",
                table: "wcs_tasks",
                columns: new[] { "organization_id", "environment_id", "external_task_id" });
        }
    }
}
