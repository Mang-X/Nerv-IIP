using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.DemandPlanning.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMrpPeggingSourceLineReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_line_reference",
                schema: "demand_planning",
                table: "mrp_pegging_links",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Stable upstream sales order line reference; null when not known or not applicable.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_line_reference",
                schema: "demand_planning",
                table: "mrp_pegging_links");
        }
    }
}
