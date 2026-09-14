using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenInspectionTaskTriggerIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "trigger_idempotency_key",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(474)",
                maxLength: 474,
                nullable: false,
                comment: "Idempotency key derived from the source event and source line; upper bound governed by InspectionTaskTriggerKey.MaxLength.",
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldComment: "Idempotency key derived from the source event and source line.");

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Inspection source type; value domain is QualityInspectionSourceTypes.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Inspection source type: receiving, operation, final, maintenance or customer-return.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "trigger_idempotency_key",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                comment: "Idempotency key derived from the source event and source line.",
                oldClrType: typeof(string),
                oldType: "character varying(474)",
                oldMaxLength: 474,
                oldComment: "Idempotency key derived from the source event and source line; upper bound governed by InspectionTaskTriggerKey.MaxLength.");

            migrationBuilder.AlterColumn<string>(
                name: "source_type",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Inspection source type: receiving, operation, final, maintenance or customer-return.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Inspection source type; value domain is QualityInspectionSourceTypes.");
        }
    }
}
