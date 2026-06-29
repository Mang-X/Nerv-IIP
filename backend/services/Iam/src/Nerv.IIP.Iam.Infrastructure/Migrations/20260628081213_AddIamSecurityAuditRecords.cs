using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Iam.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIamSecurityAuditRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "security_audit_records",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Security audit record identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Organization scope for the audited IAM security event."),
                    EnvironmentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Environment scope for the audited IAM security event."),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "IAM security audit action."),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Actor that caused or attempted the security event."),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Audited target type, for example user, session or role."),
                    TargetId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Audited target identifier."),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Audit outcome, for example success or failure."),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Security audit occurrence time in UTC."),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Correlation identifier for the security event."),
                    SourceIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "Source IP address associated with the security event when available."),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false, comment: "Structured JSON details for before and after values or decision diagnostics.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_audit_records", x => x.Id);
                },
                comment: "IAM security audit records for authentication decisions, session revocation and authorization administration.");

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_records_Action_OccurredAtUtc",
                schema: "iam",
                table: "security_audit_records",
                columns: new[] { "Action", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_records_OrganizationId_EnvironmentId_Occurre~",
                schema: "iam",
                table: "security_audit_records",
                columns: new[] { "OrganizationId", "EnvironmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_security_audit_records_TargetType_TargetId_OccurredAtUtc",
                schema: "iam",
                table: "security_audit_records",
                columns: new[] { "TargetType", "TargetId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "security_audit_records",
                schema: "iam");
        }
    }
}
