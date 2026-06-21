using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Approval.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Issue417ApprovalWorkflowGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_decisions_chain_id_step_no_actor_type_actor_ref",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.AddColumn<string>(
                name: "completion_policy",
                schema: "business_approval",
                table: "approval_template_steps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "all",
                comment: "Completion policy for the step number group: all or any.");

            migrationBuilder.AddColumn<string>(
                name: "condition_expression",
                schema: "business_approval",
                table: "approval_template_steps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Optional simple key=value condition for routing this step when a chain starts.");

            migrationBuilder.AddColumn<string>(
                name: "completion_policy",
                schema: "business_approval",
                table: "approval_steps",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "all",
                comment: "Runtime completion policy for the step number group: all or any.");

            migrationBuilder.AddColumn<string>(
                name: "condition_expression",
                schema: "business_approval",
                table: "approval_steps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Condition that caused this runtime step to be included.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_notified_at_utc",
                schema: "business_approval",
                table: "approval_steps",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the overdue event was emitted for this step.");

            migrationBuilder.AddColumn<string>(
                name: "on_behalf_of_actor_ref",
                schema: "business_approval",
                table: "approval_decisions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Original approver actor reference when a delegate made the decision.");

            migrationBuilder.AddColumn<string>(
                name: "on_behalf_of_actor_type",
                schema: "business_approval",
                table: "approval_decisions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Original approver actor type when a delegate made the decision.");

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "step_no", "actor_type", "actor_ref", "on_behalf_of_actor_type", "on_behalf_of_actor_ref" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_approval_decisions_chain_step_actor_on_behalf",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "completion_policy",
                schema: "business_approval",
                table: "approval_template_steps");

            migrationBuilder.DropColumn(
                name: "condition_expression",
                schema: "business_approval",
                table: "approval_template_steps");

            migrationBuilder.DropColumn(
                name: "completion_policy",
                schema: "business_approval",
                table: "approval_steps");

            migrationBuilder.DropColumn(
                name: "condition_expression",
                schema: "business_approval",
                table: "approval_steps");

            migrationBuilder.DropColumn(
                name: "overdue_notified_at_utc",
                schema: "business_approval",
                table: "approval_steps");

            migrationBuilder.DropColumn(
                name: "on_behalf_of_actor_ref",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.DropColumn(
                name: "on_behalf_of_actor_type",
                schema: "business_approval",
                table: "approval_decisions");

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_chain_id_step_no_actor_type_actor_ref",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "step_no", "actor_type", "actor_ref" },
                unique: true);
        }
    }
}
