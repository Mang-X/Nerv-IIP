using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Approval.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Issue449ApprovalActionsDecisionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.AddColumn<int>(
                name: "round_no",
                schema: "business_approval",
                table: "approval_decisions",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Approval submission round when the decision or action was recorded.");

            migrationBuilder.AddColumn<int>(
                name: "round_no",
                schema: "business_approval",
                table: "approval_chains",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Current submission round number; increments when a returned or withdrawn chain is resubmitted.");

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "step_no", "actor_type", "actor_ref", "on_behalf_of_actor_type", "on_behalf_of_actor_ref" });

            migrationBuilder.CreateIndex(
                name: "UX_approval_decisions_resolution_actor_round",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "round_no", "step_no", "actor_type", "actor_ref", "on_behalf_of_actor_type", "on_behalf_of_actor_ref" },
                unique: true,
                filter: "decision IN ('approve', 'reject', 'return')")
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.DropIndex(
                name: "UX_approval_decisions_resolution_actor_round",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "round_no",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "round_no",
                schema: "business_approval",
                table: "approval_chains");

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "step_no", "actor_type", "actor_ref", "on_behalf_of_actor_type", "on_behalf_of_actor_ref" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
