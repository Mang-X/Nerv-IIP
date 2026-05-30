using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesDispatchAssignmentFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "assigned_at_utc",
                schema: "mes",
                table: "operation_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when MES dispatch assignment facts were captured.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_user_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned operator or person public id captured by MES dispatch.");

            migrationBuilder.AddColumn<string>(
                name: "device_asset_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData device asset public id captured by MES dispatch.");

            migrationBuilder.AddColumn<string>(
                name: "shift_id",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Assigned MasterData shift public id captured by MES dispatch.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assigned_at_utc",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "assigned_user_id",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "device_asset_id",
                schema: "mes",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "shift_id",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
