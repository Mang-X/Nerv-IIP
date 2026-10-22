using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesChangeoverRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "changeover_records",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Changeover record aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id owning the changeover record."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id owning the changeover record."),
                    changeover_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES business number allocated for the changeover record."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData work center public id where the changeover occurred."),
                    device_asset_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "MasterData device asset public id changed over."),
                    operator_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "IAM principal id of the operator performing the changeover."),
                    tooling_check_result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Controlled tooling or mold verification result captured at changeover start."),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the changeover started."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the changeover completed; null means it is still active.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_changeover_records", x => x.id);
                },
                comment: "MES actual changeover lifecycle records for production equipment.");

            migrationBuilder.CreateIndex(
                name: "ix_changeover_records_scope_device_open",
                schema: "mes",
                table: "changeover_records",
                columns: new[] { "organization_id", "environment_id", "device_asset_id", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_changeover_records_scope_no",
                schema: "mes",
                table: "changeover_records",
                columns: new[] { "organization_id", "environment_id", "changeover_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "changeover_records",
                schema: "mes");
        }
    }
}
