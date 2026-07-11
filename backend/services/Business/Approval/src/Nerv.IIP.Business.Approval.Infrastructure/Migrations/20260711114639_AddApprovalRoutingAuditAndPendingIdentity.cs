using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Approval.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalRoutingAuditAndPendingIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pending_identity_key",
                schema: "business_approval",
                table: "approval_chains",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Stable unique identity held only while the source document approval chain is pending.");

            migrationBuilder.AddColumn<decimal>(
                name: "routing_amount",
                schema: "business_approval",
                table: "approval_chains",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                comment: "Optional source amount used for structured approval routing and audit.");

            migrationBuilder.AddColumn<string>(
                name: "routing_department_id",
                schema: "business_approval",
                table: "approval_chains",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional department dimension used for structured approval routing and audit.");

            migrationBuilder.AddColumn<string>(
                name: "routing_organization_id",
                schema: "business_approval",
                table: "approval_chains",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional organization dimension used for structured approval routing and audit.");

            migrationBuilder.CreateIndex(
                name: "IX_approval_chains_pending_identity_key",
                schema: "business_approval",
                table: "approval_chains",
                column: "pending_identity_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_chains_pending_identity_key",
                schema: "business_approval",
                table: "approval_chains");

            migrationBuilder.DropColumn(
                name: "pending_identity_key",
                schema: "business_approval",
                table: "approval_chains");

            migrationBuilder.DropColumn(
                name: "routing_amount",
                schema: "business_approval",
                table: "approval_chains");

            migrationBuilder.DropColumn(
                name: "routing_department_id",
                schema: "business_approval",
                table: "approval_chains");

            migrationBuilder.DropColumn(
                name: "routing_organization_id",
                schema: "business_approval",
                table: "approval_chains");
        }
    }
}
