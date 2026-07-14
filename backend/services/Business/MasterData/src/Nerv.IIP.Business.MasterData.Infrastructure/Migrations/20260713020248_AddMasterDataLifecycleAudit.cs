using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterDataLifecycleAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "master_data_lifecycle_audit",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Lifecycle audit entry identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment scope."),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Master-data resource type."),
                    ResourceId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Persistent resource identifier."),
                    ResourceCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Stable resource code or public identifier."),
                    ResourceIdentity = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false, comment: "Canonical resource identity including composite-key qualifiers."),
                    TargetEnabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Lifecycle state requested by the operation."),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Trusted authenticated principal that requested the change."),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Required normalized lifecycle change reason."),
                    OperationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Correlation or idempotency identity for the operation."),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the lifecycle change occurred.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_data_lifecycle_audit", x => x.Id);
                },
                comment: "Durable audit trail for master-data lifecycle state changes.");

            migrationBuilder.CreateIndex(
                name: "ix_master_data_lifecycle_audit_resource",
                schema: "business_masterdata",
                table: "master_data_lifecycle_audit",
                columns: new[] { "OrganizationId", "EnvironmentId", "ResourceType", "ResourceCode", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_master_data_lifecycle_audit_operation",
                schema: "business_masterdata",
                table: "master_data_lifecycle_audit",
                columns: new[] { "OrganizationId", "EnvironmentId", "OperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "master_data_lifecycle_audit",
                schema: "business_masterdata");
        }
    }
}
