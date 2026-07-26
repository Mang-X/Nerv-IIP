using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterDataWorkersAndTeamWorkCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "work_center_code",
                schema: "business_masterdata",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional work center code the team staffs; drives MES dispatch candidate filtering.");

            migrationBuilder.CreateTable(
                name: "workers",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Worker aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the worker record."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the worker record is valid."),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Human readable employee number shown on shop floor screens."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Worker display name."),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Stable person identifier shared with team membership, personnel skills, and MES dispatch."),
                    department_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional department code the worker belongs to."),
                    job_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, comment: "Optional job title of the worker."),
                    employment_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Duty status of the worker: active, on-leave, or resigned."),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Optional contact phone number."),
                    disabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag for archived workers."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the worker record was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the worker record was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workers", x => x.id);
                },
                comment: "Business master data factory workers used by team membership, personnel skills, and MES dispatch.");

            migrationBuilder.CreateIndex(
                name: "IX_workers_organization_id_environment_id_code",
                schema: "business_masterdata",
                table: "workers",
                columns: new[] { "organization_id", "environment_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workers_organization_id_environment_id_department_code_disa~",
                schema: "business_masterdata",
                table: "workers",
                columns: new[] { "organization_id", "environment_id", "department_code", "disabled" });

            migrationBuilder.CreateIndex(
                name: "IX_workers_organization_id_environment_id_user_id",
                schema: "business_masterdata",
                table: "workers",
                columns: new[] { "organization_id", "environment_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workers",
                schema: "business_masterdata");

            migrationBuilder.DropColumn(
                name: "work_center_code",
                schema: "business_masterdata",
                table: "teams");
        }
    }
}
