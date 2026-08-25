using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolingOperationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tooling_audit_entries",
                schema: "business_masterdata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Tooling audit entry identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization scope."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment scope."),
                    OperationKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Governed operation: tooling-register, tooling-status, or tooling-usage."),
                    ToolingAssetId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Persistent tooling asset identifier without a cross-table foreign key."),
                    ToolingCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Canonical tooling code targeted by the operation."),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Trusted authenticated principal forwarded by the authorized caller."),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Request correlation identity."),
                    CausationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Request causation identity."),
                    OperationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Stable idempotency identity for the governed operation."),
                    RequestFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false, comment: "SHA-256 fingerprint of the canonical operation, target, and whitelisted request summary."),
                    BeforeStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "Tooling status before a status operation; otherwise null."),
                    AfterStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, comment: "Tooling status after register or status operation; otherwise null."),
                    BeforeUsageCount = table.Column<long>(type: "bigint", nullable: true, comment: "Usage count before a usage operation; otherwise null."),
                    AfterUsageCount = table.Column<long>(type: "bigint", nullable: true, comment: "Usage count after register or usage operation; otherwise null."),
                    UsageDelta = table.Column<long>(type: "bigint", nullable: true, comment: "Positive usage increment for a usage operation; otherwise null."),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Normalized status change reason; otherwise null."),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC timestamp when the governed operation occurred.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tooling_audit_entries", x => x.Id);
                    table.CheckConstraint("ck_tooling_audit_operation_kind", "\"OperationKind\" IN ('tooling-register', 'tooling-status', 'tooling-usage')");
                    table.CheckConstraint("ck_tooling_audit_summary_shape", "(\"OperationKind\" = 'tooling-register' AND \"BeforeStatus\" IS NULL AND \"AfterStatus\" = 'Available' AND \"BeforeUsageCount\" IS NULL AND \"AfterUsageCount\" = 0 AND \"UsageDelta\" IS NULL AND \"Reason\" IS NULL) OR (\"OperationKind\" = 'tooling-status' AND \"BeforeStatus\" IS NOT NULL AND \"AfterStatus\" IS NOT NULL AND \"BeforeUsageCount\" IS NULL AND \"AfterUsageCount\" IS NULL AND \"UsageDelta\" IS NULL AND \"Reason\" IS NOT NULL) OR (\"OperationKind\" = 'tooling-usage' AND \"BeforeStatus\" IS NULL AND \"AfterStatus\" IS NULL AND \"BeforeUsageCount\" >= 0 AND \"AfterUsageCount\" = \"BeforeUsageCount\" + \"UsageDelta\" AND \"UsageDelta\" > 0 AND \"Reason\" IS NULL)");
                },
                comment: "Append-only audit facts for governed tooling register, status, and usage operations.");

            migrationBuilder.CreateIndex(
                name: "ix_tooling_audit_target_time",
                schema: "business_masterdata",
                table: "tooling_audit_entries",
                columns: new[] { "OrganizationId", "EnvironmentId", "ToolingCode", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_tooling_audit_operation",
                schema: "business_masterdata",
                table: "tooling_audit_entries",
                columns: new[] { "OrganizationId", "EnvironmentId", "OperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM business_masterdata.tooling_audit_entries
                    ) THEN
                        RAISE EXCEPTION
                            'Cannot downgrade AddToolingOperationAudit while tooling audit facts exist. Preserve the evidence and roll forward with a corrective migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "tooling_audit_entries",
                schema: "business_masterdata");
        }
    }
}
