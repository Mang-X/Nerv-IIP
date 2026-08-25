using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesLineSideReturnIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "line_side_return_idempotency_keys_json",
                schema: "mes",
                table: "material_issue_requests",
                type: "text",
                nullable: false,
                defaultValue: "{}",
                comment: "Processed line-side return idempotency keys and quantities for safe replay after a lost response.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "line_side_return_idempotency_keys_json",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
