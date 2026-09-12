using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAssetRetirementRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC local terminal completion time, absent for pending or unknown outcomes.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replay_until_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Frozen UTC replay deadline, local completion plus the agreed horizon.");

            migrationBuilder.CreateTable(
                name: "template_asset_retirement_replay_fences",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Original retirement decision identity, not a new generated identity."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization owning the retired asset."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment owning the retired asset."),
                    template_file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "File identity permanently prohibited from reuse."),
                    idempotency_key_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "SHA-256 uppercase hex digest of the UTF-8 caller key; no original caller text."),
                    replay_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Frozen UTC boundary for replay-window-expired.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_asset_retirement_replay_fences", x => x.id);
                },
                comment: "Permanent minimal retirement facts; no requester, reason, proof or object content.");

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirement_decisions_status_replay_until_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions",
                columns: new[] { "status", "replay_until_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirement_replay_fences_organization_id_en~1",
                schema: "barcode",
                table: "template_asset_retirement_replay_fences",
                columns: new[] { "organization_id", "environment_id", "template_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirement_replay_fences_organization_id_env~",
                schema: "barcode",
                table: "template_asset_retirement_replay_fences",
                columns: new[] { "organization_id", "environment_id", "idempotency_key_digest" },
                unique: true);

            // The previous terminal CAS froze H and its local commit clock in updated_at_utc.
            // Preserve that clock, rather than granting historical decisions a new window.
            migrationBuilder.Sql("""
                UPDATE barcode.template_asset_retirement_decisions
                SET completed_at_utc = updated_at_utc,
                    replay_until_utc = updated_at_utc + replay_horizon_seconds * interval '1 second'
                WHERE status = 'quota-released';

                INSERT INTO barcode.template_asset_retirement_replay_fences
                    (id, organization_id, environment_id, template_file_id, idempotency_key_digest, replay_until_utc)
                SELECT id, organization_id, environment_id, template_file_id,
                    upper(encode(sha256(convert_to(idempotency_key, 'UTF8')), 'hex')), replay_until_utc
                FROM barcode.template_asset_retirement_decisions WHERE status = 'quota-released';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "template_asset_retirement_replay_fences",
                schema: "barcode");

            migrationBuilder.DropIndex(
                name: "IX_template_asset_retirement_decisions_status_replay_until_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "completed_at_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");

            migrationBuilder.DropColumn(
                name: "replay_until_utc",
                schema: "barcode",
                table: "template_asset_retirement_decisions");
        }
    }
}
