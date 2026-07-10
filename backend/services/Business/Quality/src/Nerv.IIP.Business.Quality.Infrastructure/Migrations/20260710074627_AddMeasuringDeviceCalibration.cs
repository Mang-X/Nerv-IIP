using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasuringDeviceCalibration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "measuring_device_calibration_due_at_utc",
                schema: "quality",
                table: "inspection_records",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Snapshot of the device calibration due UTC time at inspection entry.");

            migrationBuilder.AddColumn<string>(
                name: "measuring_device_calibration_state",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Snapshot of the device calibration state at inspection entry.");

            migrationBuilder.AddColumn<string>(
                name: "measuring_device_code",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Snapshot of the measuring device business code used at inspection entry.");

            migrationBuilder.AddColumn<Guid>(
                name: "measuring_device_id",
                schema: "quality",
                table: "inspection_records",
                type: "uuid",
                nullable: true,
                comment: "Optional measuring device used for this inspection.");

            migrationBuilder.CreateTable(
                name: "measuring_devices",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Measuring device aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    device_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Quality coding-engine allocated measuring device code."),
                    device_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Measuring device type."),
                    accuracy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Declared measuring accuracy."),
                    calibration_interval_days = table.Column<int>(type: "integer", nullable: false, comment: "Calibration cycle in days."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Device lifecycle status: in-use, calibration, disabled or retired."),
                    last_calibrated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time of latest accepted calibration."),
                    calibration_due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when calibration is due.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_measuring_devices", x => x.id);
                },
                comment: "Quality measuring devices with calibration due-date lifecycle.");

            migrationBuilder.CreateTable(
                name: "calibration_records",
                schema: "quality",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Calibration record id."),
                    measuring_device_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning measuring device id."),
                    calibration_no = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Calibration record business code."),
                    calibrated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time calibration was accepted."),
                    calibration_provider = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "External calibration laboratory or service provider reference, not the application audit actor."),
                    certificate_file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional File Storage certificate reference.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calibration_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_calibration_records_measuring_devices_measuring_device_id",
                        column: x => x.measuring_device_id,
                        principalSchema: "quality",
                        principalTable: "measuring_devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Immutable accepted calibration records for Quality measuring devices.");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_records_organization_id_environment_id_measuring~",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "measuring_device_id" });

            migrationBuilder.CreateIndex(
                name: "IX_calibration_records_measuring_device_id_calibration_no",
                schema: "quality",
                table: "calibration_records",
                columns: new[] { "measuring_device_id", "calibration_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_measuring_devices_organization_id_environment_id_calibratio~",
                schema: "quality",
                table: "measuring_devices",
                columns: new[] { "organization_id", "environment_id", "calibration_due_at_utc", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_measuring_devices_organization_id_environment_id_device_code",
                schema: "quality",
                table: "measuring_devices",
                columns: new[] { "organization_id", "environment_id", "device_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calibration_records",
                schema: "quality");

            migrationBuilder.DropTable(
                name: "measuring_devices",
                schema: "quality");

            migrationBuilder.DropIndex(
                name: "IX_inspection_records_organization_id_environment_id_measuring~",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "measuring_device_calibration_due_at_utc",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "measuring_device_calibration_state",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "measuring_device_code",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "measuring_device_id",
                schema: "quality",
                table: "inspection_records");
        }
    }
}
