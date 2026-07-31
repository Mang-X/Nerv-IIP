using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesMaterialIssueWarehouseLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "wms_picking_task_no",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "WMS picking task number created for this material issue request, when reported.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "wms_prepared_at_utc",
                schema: "mes",
                table: "material_issue_requests",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when WMS acknowledged this material issue request with warehouse work.");

            migrationBuilder.AddColumn<string>(
                name: "wms_request_id",
                schema: "mes",
                table: "material_issue_requests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "WMS outbound order number prepared for this material issue request, reported back by WMS.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "wms_picking_task_no",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "wms_prepared_at_utc",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "wms_request_id",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
