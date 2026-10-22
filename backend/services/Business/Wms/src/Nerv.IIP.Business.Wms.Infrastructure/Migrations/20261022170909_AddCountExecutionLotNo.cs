using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCountExecutionLotNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "wms",
                table: "count_executions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Counted lot; null identifies an unbatched ledger, including historical executions.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "wms",
                table: "count_executions");
        }
    }
}
