using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicInspectionQuantityWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_generated_quantity_window_sequence",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Last atomically generated cumulative quantity-window sequence; zero before generation.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_watermark",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts",
                sql: "last_generated_quantity_window_sequence >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_periodic_inspection_runtime_quantity_watermark",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");

            migrationBuilder.DropColumn(
                name: "last_generated_quantity_window_sequence",
                schema: "quality",
                table: "periodic_inspection_runtime_contexts");
        }
    }
}
