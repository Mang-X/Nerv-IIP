using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAuthorityResolutionPendingAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "authority_resolution_pending_audits",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Guid v7 identity of the authority-pending audit fact."),
                    event_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Integration event id that was kept pending."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Producer idempotency key bound to the pending event."),
                    reason_code = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Formal unit-cost authority pending reason code."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Pending audit lifecycle status; only Pending is valid for this seam."),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when Inventory observed the authority pending result.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authority_resolution_pending_audits", x => x.id);
                    table.CheckConstraint("ck_authority_resolution_pending_audits_event_id", "length(event_id) > 0");
                    table.CheckConstraint("ck_authority_resolution_pending_audits_idempotency_key", "length(idempotency_key) > 0");
                    table.CheckConstraint("ck_authority_resolution_pending_audits_reason_code", "length(reason_code) > 0");
                    table.CheckConstraint("ck_authority_resolution_pending_audits_status", "status = 'Pending'");
                },
                comment: "Inventory event-bound audit facts for unit-cost authority pending deliveries; one immutable fact per event id.");

            migrationBuilder.CreateIndex(
                name: "ux_authority_resolution_pending_audits_event_id",
                schema: "inventory",
                table: "authority_resolution_pending_audits",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "authority_resolution_pending_audits",
                schema: "inventory");
        }
    }
}
