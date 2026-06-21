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

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "step_no", "actor_type", "actor_ref", "on_behalf_of_actor_type", "on_behalf_of_actor_ref" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions");

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
