using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesLineSideActualValueReceiptProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "pending_receipt_intent_sent",
                schema: "mes",
                table: "material_issue_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether the current receipt attempt has emitted its inbound intent in the same transaction as the outbox.");

            migrationBuilder.AddColumn<bool>(
                name: "receipt_uses_actual_issue_value",
                schema: "mes",
                table: "material_issue_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this receipt waits for all actual warehouse issue values before requesting inbound; false preserves legacy in-flight transfers.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pending_receipt_intent_sent",
                schema: "mes",
                table: "material_issue_requests");

            migrationBuilder.DropColumn(
                name: "receipt_uses_actual_issue_value",
                schema: "mes",
                table: "material_issue_requests");
        }
    }
}
