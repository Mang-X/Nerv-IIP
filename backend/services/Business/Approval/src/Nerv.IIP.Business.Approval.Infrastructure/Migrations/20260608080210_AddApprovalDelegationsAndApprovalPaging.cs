using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Approval.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalDelegationsAndApprovalPaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approval_delegations",
                schema: "business_approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval delegation id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the delegation."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the delegation applies."),
                    delegator_actor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Delegating actor type such as user, group or permission."),
                    delegator_actor_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public actor reference that delegates approval authority."),
                    delegate_actor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Delegate actor type such as user, group or permission."),
                    delegate_actor_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public actor reference that receives delegated approval authority."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional document type scope for this delegation."),
                    effective_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the delegation starts."),
                    effective_to_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the delegation expires."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Delegation status: active or revoked."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Optional reason recorded when creating the delegation."),
                    created_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public actor reference that created the delegation."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the delegation was created."),
                    revoked_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Public actor reference that revoked the delegation."),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the delegation was revoked.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_delegations", x => x.id);
                },
                comment: "Business approval actor delegation authorizations.");

            migrationBuilder.CreateIndex(
                name: "IX_approval_delegations_organization_id_environment_id_delegat~",
                schema: "business_approval",
                table: "approval_delegations",
                columns: new[] { "organization_id", "environment_id", "delegator_actor_ref", "document_type" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_delegations_organization_id_environment_id_status_~",
                schema: "business_approval",
                table: "approval_delegations",
                columns: new[] { "organization_id", "environment_id", "status", "delegate_actor_ref" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_delegations",
                schema: "business_approval");
        }
    }
}
