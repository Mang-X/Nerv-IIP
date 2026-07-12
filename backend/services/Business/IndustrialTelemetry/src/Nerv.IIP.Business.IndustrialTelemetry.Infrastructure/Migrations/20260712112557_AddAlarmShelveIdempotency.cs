using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlarmShelveIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alarm_shelve_idempotency",
                schema: "industrial_telemetry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Shelve idempotency record identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning organization identifier."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Owning environment identifier."),
                    alarm_event_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Referenced alarm event identifier (string form; no cross-aggregate FK)."),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Caller-minted idempotency key of the shelve operation."),
                    payload_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "SHA-256 hex of the canonical shelve payload; same key + same fingerprint replays, same key + different fingerprint conflicts."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the idempotency record was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarm_shelve_idempotency", x => x.Id);
                },
                comment: "Persistent per-(alarm, idempotency key) shelve dedup records with a payload fingerprint; makes shelve durably idempotent independent of the alarm window/status.");

            migrationBuilder.CreateIndex(
                name: "IX_alarm_shelve_idempotency_organization_id_environment_id_ala~",
                schema: "industrial_telemetry",
                table: "alarm_shelve_idempotency",
                columns: new[] { "organization_id", "environment_id", "alarm_event_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm_shelve_idempotency",
                schema: "industrial_telemetry");
        }
    }
}
