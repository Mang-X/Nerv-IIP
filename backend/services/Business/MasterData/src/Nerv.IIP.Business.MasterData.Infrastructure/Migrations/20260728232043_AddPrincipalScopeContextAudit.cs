using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrincipalScopeContextAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "master_data_scope_context_audit",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Scope context audit entry identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment scope."),
                    OperationKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Stable scope-context mutation kind."),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Master-data resource type."),
                    ResourceId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Persistent resource identifier."),
                    ResourceCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Stable resource code."),
                    ResourceIdentity = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "Canonical resource identity."),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Trusted authenticated principal."),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Request correlation identity."),
                    CausationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Request causation identity."),
                    OperationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Idempotency key or correlation identity."),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: false, comment: "Canonical authorization-relevant state before the mutation."),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: false, comment: "Canonical authorization-relevant state after the mutation."),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Normalized reason or stable system reason code."),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the mutation occurred.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_data_scope_context_audit", x => x.Id);
                },
                comment: "Durable audit trail for master-data changes that alter principal scope candidates.");

            migrationBuilder.CreateIndex(
                name: "ix_master_data_scope_context_audit_operation",
                schema: "business_masterdata",
                table: "master_data_scope_context_audit",
                columns: new[] { "OrganizationId", "EnvironmentId", "OperationId" });

            migrationBuilder.CreateIndex(
                name: "ix_master_data_scope_context_audit_resource",
                schema: "business_masterdata",
                table: "master_data_scope_context_audit",
                columns: new[] { "OrganizationId", "EnvironmentId", "ResourceType", "ResourceCode", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "master_data_scope_context_audit",
                schema: "business_masterdata");
        }
    }
}
