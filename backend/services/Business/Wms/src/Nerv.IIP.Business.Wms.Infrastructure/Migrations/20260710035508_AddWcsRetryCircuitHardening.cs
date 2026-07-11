using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWcsRetryCircuitHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "device_id",
                schema: "wms",
                table: "wcs_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "",
                comment: "WCS adapter-scoped device identifier used by retry and circuit controls.");

            migrationBuilder.AddColumn<bool>(
                name: "is_terminal_failure",
                schema: "wms",
                table: "wcs_tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether bounded WCS retry attempts have been exhausted.");

            migrationBuilder.AddColumn<DateTime>(
                name: "next_retry_at_utc",
                schema: "wms",
                table: "wcs_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Earliest UTC time at which a failed WCS task may be dispatched again.");

            migrationBuilder.CreateTable(
                name: "wcs_dispatch_circuits",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "WCS dispatch circuit id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    adapter_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "WCS adapter type."),
                    device_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "WCS device identifier."),
                    consecutive_failure_count = table.Column<int>(type: "integer", nullable: false, comment: "Consecutive failed dispatch count."),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time the circuit opened."),
                    last_failure_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time of the most recent failure."),
                    reset_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC time of the latest manual reset.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wcs_dispatch_circuits", x => x.id);
                },
                comment: "Per adapter and device WCS dispatch circuit state.");

            migrationBuilder.CreateIndex(
                name: "IX_wcs_dispatch_circuits_organization_id_environment_id_adapte~",
                schema: "wms",
                table: "wcs_dispatch_circuits",
                columns: new[] { "organization_id", "environment_id", "adapter_type", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wcs_dispatch_circuits",
                schema: "wms");

            migrationBuilder.DropColumn(
                name: "device_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.DropColumn(
                name: "is_terminal_failure",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.DropColumn(
                name: "next_retry_at_utc",
                schema: "wms",
                table: "wcs_tasks");
        }
    }
}
