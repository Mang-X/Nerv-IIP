using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenOrderUrgencyArchiveLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "batch_id",
                schema: "scheduling",
                table: "order_urgency_archive_batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Stable source-row-generation-derived archive batch id.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "Stable content-derived archive batch id.");

            migrationBuilder.AddColumn<long>(
                name: "revision",
                schema: "scheduling",
                table: "order_urgency_archive_batches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Monotonic optimistic concurrency revision protecting lifecycle transitions.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revision",
                schema: "scheduling",
                table: "order_urgency_archive_batches");

            migrationBuilder.AlterColumn<string>(
                name: "batch_id",
                schema: "scheduling",
                table: "order_urgency_archive_batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                comment: "Stable content-derived archive batch id.",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldComment: "Stable source-row-generation-derived archive batch id.");
        }
    }
}
