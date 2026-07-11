using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAssignmentStandardOperationCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "standard_operation_code",
                schema: "scheduling",
                table: "schedule_plan_assignments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "ProductEngineering standard operation code used by MES to resolve current SOP or electronic work instructions.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "standard_operation_code",
                schema: "scheduling",
                table: "schedule_plan_assignments");
        }
    }
}
