using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsAuditIntegrityHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntegrityHash",
                schema: "ops",
                table: "audit_records",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "legacy:unverified",
                comment: "Tamper-evident SHA-256 hash over immutable audit fields.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IntegrityHash",
                schema: "ops",
                table: "audit_records");
        }
    }
}
