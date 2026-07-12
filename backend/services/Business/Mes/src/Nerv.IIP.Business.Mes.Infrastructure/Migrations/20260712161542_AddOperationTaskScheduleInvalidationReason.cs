using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationTaskScheduleInvalidationReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "schedule_invalidation_reason_code",
                schema: "mes",
                table: "operation_tasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Latest scheduling invalidation reason code when the task is schedule invalidated; cleared when a released schedule re-plans the task.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "schedule_invalidation_reason_code",
                schema: "mes",
                table: "operation_tasks");
        }
    }
}
