using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpandMrpInventorySnapshotSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "inventory_snapshot_source",
                schema: "demand_planning",
                table: "mrp_runs",
                type: "text",
                nullable: false,
                comment: "Source adapter used for inventory availability snapshots.",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldComment: "Source adapter used for inventory availability snapshots.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "inventory_snapshot_source",
                schema: "demand_planning",
                table: "mrp_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                comment: "Source adapter used for inventory availability snapshots.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Source adapter used for inventory availability snapshots.");
        }
    }
}
