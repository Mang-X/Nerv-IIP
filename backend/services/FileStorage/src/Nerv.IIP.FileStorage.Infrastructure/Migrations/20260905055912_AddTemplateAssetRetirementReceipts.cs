using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.FileStorage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateAssetRetirementReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "template_asset_retirements",
                schema: "filestorage",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Upstream retirement decision and audit reference."),
                    organization_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owning organization."),
                    environment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Owning environment."),
                    file_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Retired file identity; retained after file metadata removal."),
                    checksum = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Frozen canonical SHA-256 of the retired asset."),
                    owner_service = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Frozen owning service."),
                    owner_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Frozen owner resource type."),
                    owner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Frozen owner resource identity."),
                    purpose = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Authorized file purpose."),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false, comment: "Business quota bytes released by acceptance."),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Physical lifecycle state; acceptance alone is physical-hold."),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC acceptance, physical hold and quota release timestamp."),
                    replay_policy_version = table.Column<long>(type: "bigint", nullable: false, comment: "Version of the frozen horizon policy."),
                    client_window_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen upstream client replay request, in seconds."),
                    barcode_lease_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen BarcodeLabel retirement lease, in seconds."),
                    barcode_max_backoff_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen BarcodeLabel retirement maximum backoff, in seconds."),
                    physical_grace_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen physical grace, in seconds."),
                    gc_interval_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen FileStorage collector interval, in seconds."),
                    storage_lease_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen FileStorage retirement executor lease, in seconds."),
                    storage_max_backoff_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen FileStorage retirement executor maximum backoff, in seconds."),
                    replay_horizon_seconds = table.Column<long>(type: "bigint", nullable: false, comment: "Frozen shared replay duration H; terminal deadline is assigned by physical completion.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_asset_retirements", x => x.decision_id);
                },
                comment: "Durable label-template retirement receipts and frozen replay inputs.");

            migrationBuilder.CreateIndex(
                name: "IX_template_asset_retirements_organization_id_environment_id_f~",
                schema: "filestorage",
                table: "template_asset_retirements",
                columns: new[] { "organization_id", "environment_id", "file_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM filestorage.template_asset_retirements) THEN
                        RAISE EXCEPTION 'Retirement receipts exist; use a forward migration instead of discarding physical-hold audit facts.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "template_asset_retirements",
                schema: "filestorage");
        }
    }
}
