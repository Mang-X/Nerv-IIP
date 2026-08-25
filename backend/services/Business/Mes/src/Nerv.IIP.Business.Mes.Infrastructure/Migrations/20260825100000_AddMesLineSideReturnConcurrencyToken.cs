using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations;

[Migration("20260825100000_AddMesLineSideReturnConcurrencyToken")]
public partial class AddMesLineSideReturnConcurrencyToken : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<long>(
            name: "line_side_return_concurrency_token",
            schema: "mes",
            table: "material_issue_requests",
            type: "bigint",
            nullable: false,
            defaultValue: 0L,
            comment: "Optimistic concurrency token for line-side return quantity and idempotency mutations.");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "line_side_return_concurrency_token",
            schema: "mes",
            table: "material_issue_requests");
}
