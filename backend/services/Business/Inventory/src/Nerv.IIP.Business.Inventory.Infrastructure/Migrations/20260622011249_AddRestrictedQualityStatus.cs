using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRestrictedQualityStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_reservations_quality_status",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_quality_status",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_quality_status",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_tasks_quality_status",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_quality_status",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_reservations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Canonical stock status reserved: unrestricted, quality, restricted or blocked.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Canonical stock status reserved: unrestricted, quality or blocked.");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_movements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Canonical stock status: unrestricted, quality, restricted or blocked.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Canonical stock status: unrestricted, quality or blocked.");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_ledgers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Quality status carried by stock facts: unrestricted, quality, restricted or blocked.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Quality status carried by stock facts.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_reservations_quality_status",
                schema: "inventory",
                table: "stock_reservations",
                sql: "quality_status in ('unrestricted','quality','restricted','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_quality_status",
                schema: "inventory",
                table: "stock_movements",
                sql: "quality_status in ('unrestricted','quality','restricted','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_quality_status",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "quality_status in ('unrestricted','quality','restricted','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_tasks_quality_status",
                schema: "inventory",
                table: "stock_count_tasks",
                sql: "quality_status in ('unrestricted','quality','restricted','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_quality_status",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "quality_status in ('unrestricted','quality','restricted','blocked')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_reservations_quality_status",
                schema: "inventory",
                table: "stock_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_movements_quality_status",
                schema: "inventory",
                table: "stock_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_ledgers_quality_status",
                schema: "inventory",
                table: "stock_ledgers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_tasks_quality_status",
                schema: "inventory",
                table: "stock_count_tasks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_count_adjustments_quality_status",
                schema: "inventory",
                table: "stock_count_adjustments");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_reservations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Canonical stock status reserved: unrestricted, quality or blocked.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Canonical stock status reserved: unrestricted, quality, restricted or blocked.");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_movements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Canonical stock status: unrestricted, quality or blocked.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Canonical stock status: unrestricted, quality, restricted or blocked.");

            migrationBuilder.AlterColumn<string>(
                name: "quality_status",
                schema: "inventory",
                table: "stock_ledgers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Quality status carried by stock facts.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldComment: "Quality status carried by stock facts: unrestricted, quality, restricted or blocked.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_reservations_quality_status",
                schema: "inventory",
                table: "stock_reservations",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_movements_quality_status",
                schema: "inventory",
                table: "stock_movements",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_ledgers_quality_status",
                schema: "inventory",
                table: "stock_ledgers",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_tasks_quality_status",
                schema: "inventory",
                table: "stock_count_tasks",
                sql: "quality_status in ('unrestricted','quality','blocked')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_count_adjustments_quality_status",
                schema: "inventory",
                table: "stock_count_adjustments",
                sql: "quality_status in ('unrestricted','quality','blocked')");
        }
    }
}
