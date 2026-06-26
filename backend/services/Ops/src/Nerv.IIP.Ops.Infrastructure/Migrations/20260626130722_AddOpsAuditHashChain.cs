using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsAuditHashChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IntegrityHash",
                schema: "ops",
                table: "audit_records",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "Tamper-evident SHA-256 hash over immutable audit fields plus sequence and previous hash.",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "Tamper-evident SHA-256 hash over immutable audit fields.");

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentId",
                schema: "ops",
                table: "audit_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "Environment scope for the Ops audit chain.");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                schema: "ops",
                table: "audit_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "Organization scope for the Ops audit chain.");

            migrationBuilder.AddColumn<string>(
                name: "PreviousIntegrityHash",
                schema: "ops",
                table: "audit_records",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                comment: "Previous audit record integrity hash in the organization and environment chain; empty for genesis.");

            migrationBuilder.AddColumn<long>(
                name: "SequenceNo",
                schema: "ops",
                table: "audit_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Monotonic Ops audit chain sequence number within organization and environment scope.");

            migrationBuilder.Sql(
                """
                WITH scoped_audit_records AS (
                    SELECT
                        ar."Id",
                        ot."OrganizationId",
                        ot."EnvironmentId",
                        row_number() OVER (
                            PARTITION BY ot."OrganizationId", ot."EnvironmentId"
                            ORDER BY ar."OccurredAtUtc", ar."Id"
                        ) AS sequence_no,
                        lag(ar."IntegrityHash") OVER (
                            PARTITION BY ot."OrganizationId", ot."EnvironmentId"
                            ORDER BY ar."OccurredAtUtc", ar."Id"
                        ) AS previous_integrity_hash
                    FROM ops.audit_records AS ar
                    INNER JOIN ops.operation_tasks AS ot ON ot."Id" = ar."OperationTaskId"
                )
                UPDATE ops.audit_records AS ar
                SET
                    "OrganizationId" = scoped."OrganizationId",
                    "EnvironmentId" = scoped."EnvironmentId",
                    "SequenceNo" = scoped.sequence_no,
                    "PreviousIntegrityHash" = COALESCE(scoped.previous_integrity_hash, '')
                FROM scoped_audit_records AS scoped
                WHERE scoped."Id" = ar."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_OrganizationId_EnvironmentId_SequenceNo",
                schema: "ops",
                table: "audit_records",
                columns: new[] { "OrganizationId", "EnvironmentId", "SequenceNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_records_OrganizationId_EnvironmentId_SequenceNo",
                schema: "ops",
                table: "audit_records");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                schema: "ops",
                table: "audit_records");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                schema: "ops",
                table: "audit_records");

            migrationBuilder.DropColumn(
                name: "PreviousIntegrityHash",
                schema: "ops",
                table: "audit_records");

            migrationBuilder.DropColumn(
                name: "SequenceNo",
                schema: "ops",
                table: "audit_records");

            migrationBuilder.AlterColumn<string>(
                name: "IntegrityHash",
                schema: "ops",
                table: "audit_records",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                comment: "Tamper-evident SHA-256 hash over immutable audit fields.",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldComment: "Tamper-evident SHA-256 hash over immutable audit fields plus sequence and previous hash.");
        }
    }
}
