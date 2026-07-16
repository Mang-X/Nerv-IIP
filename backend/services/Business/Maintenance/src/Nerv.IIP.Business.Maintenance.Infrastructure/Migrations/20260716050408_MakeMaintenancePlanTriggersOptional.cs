using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Maintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeMaintenancePlanTriggersOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: true,
                comment: "Next business date on which the calendar-triggered plan is due, or null for a runtime-only plan.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldComment: "Next business date on which the preventive maintenance plan is due.");

            migrationBuilder.AlterColumn<string>(
                name: "interval",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Calendar interval expression (ISO-8601 P7D) for the calendar trigger, or null for a runtime-only plan.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Explicit maintenance interval expression, for example ISO-8601 P7D.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "next_due_on",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                comment: "Next business date on which the preventive maintenance plan is due.",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true,
                oldComment: "Next business date on which the calendar-triggered plan is due, or null for a runtime-only plan.");

            migrationBuilder.AlterColumn<string>(
                name: "interval",
                schema: "maintenance",
                table: "maintenance_plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Explicit maintenance interval expression, for example ISO-8601 P7D.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "Calendar interval expression (ISO-8601 P7D) for the calendar trigger, or null for a runtime-only plan.");
        }
    }
}
