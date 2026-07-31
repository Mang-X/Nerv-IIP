using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesWorkOrderSourceDemandReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "source_demand_references",
                schema: "mes",
                table: "work_orders",
                type: "text[]",
                nullable: true,
                comment: "All DemandPlanning demand source references pegged to the source suggestion (batched suggestions peg multiple demands); includes the primary reference. Null for legacy rows, which fall back to source_demand_reference.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_demand_references",
                schema: "mes",
                table: "work_orders");
        }
    }
}
