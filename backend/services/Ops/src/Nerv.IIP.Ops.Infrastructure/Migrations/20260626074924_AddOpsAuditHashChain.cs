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

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_SequenceNo",
                schema: "ops",
                table: "audit_records",
                column: "SequenceNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_audit_records_SequenceNo",
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
