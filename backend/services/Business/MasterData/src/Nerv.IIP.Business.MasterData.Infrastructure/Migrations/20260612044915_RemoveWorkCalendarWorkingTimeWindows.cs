using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkCalendarWorkingTimeWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ends_at",
                schema: "business_masterdata",
                table: "work_calendar_working_times");

            migrationBuilder.DropColumn(
                name: "starts_at",
                schema: "business_masterdata",
                table: "work_calendar_working_times");

            migrationBuilder.AlterTable(
                name: "work_calendars",
                schema: "business_masterdata",
                comment: "Business master data work calendars defining recurring working days, holidays, and exceptions.",
                oldComment: "Business master data work calendars defining recurring available working time.");

            migrationBuilder.AlterTable(
                name: "work_calendar_working_times",
                schema: "business_masterdata",
                comment: "Recurring working day markers owned by a business master data work calendar.",
                oldComment: "Recurring working time windows owned by a business master data work calendar.");

            migrationBuilder.AlterColumn<int>(
                name: "day_of_week",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                type: "integer",
                nullable: false,
                comment: "Day of week for the recurring working day.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Day of week for the recurring working time.");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                type: "uuid",
                nullable: false,
                comment: "Work calendar working day row id.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Work calendar working time row id.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "work_calendars",
                schema: "business_masterdata",
                comment: "Business master data work calendars defining recurring available working time.",
                oldComment: "Business master data work calendars defining recurring working days, holidays, and exceptions.");

            migrationBuilder.AlterTable(
                name: "work_calendar_working_times",
                schema: "business_masterdata",
                comment: "Recurring working time windows owned by a business master data work calendar.",
                oldComment: "Recurring working day markers owned by a business master data work calendar.");

            migrationBuilder.AlterColumn<int>(
                name: "day_of_week",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                type: "integer",
                nullable: false,
                comment: "Day of week for the recurring working time.",
                oldClrType: typeof(int),
                oldType: "integer",
                oldComment: "Day of week for the recurring working day.");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                type: "uuid",
                nullable: false,
                comment: "Work calendar working time row id.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Work calendar working day row id.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "ends_at",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                comment: "Local end time of the working window.");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "starts_at",
                schema: "business_masterdata",
                table: "work_calendar_working_times",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0),
                comment: "Local start time of the working window.");
        }
    }
}
