using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesAssemblyParentSuggestionReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "assembly_parent_suggestion_ids",
                schema: "mes",
                table: "work_orders",
                type: "text[]",
                nullable: true,
                comment: "DemandPlanning parent suggestion ids resolved from component pegging; MES resolves their work order ids when both suggestions are accepted.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "assembly_parent_suggestion_ids",
                schema: "mes",
                table: "work_orders");
        }
    }
}
