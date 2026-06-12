using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteMasterDataIaGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "disabled",
                schema: "business_masterdata",
                table: "uom_conversions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Disabled flag that hides the conversion rule from active use.");

            migrationBuilder.CreateTable(
                name: "work_calendar_exceptions",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work calendar exception row id."),
                    date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Local exception date."),
                    is_working_day = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the exception date is treated as a working day."),
                    starts_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true, comment: "Optional local exception start time."),
                    ends_at = table.Column<TimeOnly>(type: "time without time zone", nullable: true, comment: "Optional local exception end time."),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true, comment: "Optional reason for the calendar exception."),
                    work_calendar_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning work calendar aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_calendar_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "FK_work_calendar_exceptions_work_calendars_work_calendar_id",
                        column: x => x.work_calendar_id,
                        principalSchema: "business_masterdata",
                        principalTable: "work_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Exception dates owned by a business master data work calendar.");

            migrationBuilder.CreateTable(
                name: "work_calendar_holidays",
                schema: "business_masterdata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Work calendar holiday row id."),
                    date = table.Column<DateOnly>(type: "date", nullable: false, comment: "Local holiday date."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Holiday display name."),
                    work_calendar_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning work calendar aggregate id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_calendar_holidays", x => x.id);
                    table.ForeignKey(
                        name: "FK_work_calendar_holidays_work_calendars_work_calendar_id",
                        column: x => x.work_calendar_id,
                        principalSchema: "business_masterdata",
                        principalTable: "work_calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Holiday dates owned by a business master data work calendar.");

            migrationBuilder.CreateIndex(
                name: "IX_uom_conversions_disabled",
                schema: "business_masterdata",
                table: "uom_conversions",
                column: "disabled");

            migrationBuilder.CreateIndex(
                name: "IX_work_calendar_exceptions_work_calendar_id",
                schema: "business_masterdata",
                table: "work_calendar_exceptions",
                column: "work_calendar_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_calendar_holidays_work_calendar_id",
                schema: "business_masterdata",
                table: "work_calendar_holidays",
                column: "work_calendar_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_calendar_exceptions",
                schema: "business_masterdata");

            migrationBuilder.DropTable(
                name: "work_calendar_holidays",
                schema: "business_masterdata");

            migrationBuilder.DropIndex(
                name: "IX_uom_conversions_disabled",
                schema: "business_masterdata",
                table: "uom_conversions");

            migrationBuilder.DropColumn(
                name: "disabled",
                schema: "business_masterdata",
                table: "uom_conversions");
        }
    }
}
