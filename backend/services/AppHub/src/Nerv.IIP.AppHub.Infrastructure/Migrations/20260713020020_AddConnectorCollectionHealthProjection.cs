using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorCollectionHealthProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_registration_idempotency_IdempotencyKey",
                schema: "apphub",
                table: "registration_idempotency");

            migrationBuilder.DropIndex(
                name: "IX_application_instances_InstanceKey",
                schema: "apphub",
                table: "application_instances");

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentId",
                schema: "apphub",
                table: "registration_idempotency",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Environment scope for the registration idempotency key");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationId",
                schema: "apphub",
                table: "registration_idempotency",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Organization scope for the registration idempotency key");

            migrationBuilder.Sql("""
                UPDATE apphub.registration_idempotency AS r
                SET "OrganizationId" = i."OrganizationId",
                    "EnvironmentId" = i."EnvironmentId"
                FROM apphub.application_instances AS i
                WHERE i."InstanceKey" = r."InstanceKey";

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM apphub.registration_idempotency
                        WHERE "OrganizationId" IS NULL OR "EnvironmentId" IS NULL
                    ) THEN
                        RAISE EXCEPTION 'Cannot derive registration idempotency scope from application_instances';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "EnvironmentId",
                schema: "apphub",
                table: "registration_idempotency",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                comment: "Environment scope for the registration idempotency key");

            migrationBuilder.AlterColumn<string>(
                name: "OrganizationId",
                schema: "apphub",
                table: "registration_idempotency",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                comment: "Organization scope for the registration idempotency key");

            migrationBuilder.CreateTable(
                name: "connector_collection_health",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Collection health projection id"),
                    ApplicationInstanceId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning application instance aggregate id"),
                    OrganizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization scope for the connector identity"),
                    EnvironmentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment scope for the connector identity"),
                    ConnectorId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Stable connector identity within organization and environment scope"),
                    SourceSystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source protocol or system, such as opcua, modbus, or mqtt"),
                    CounterEpoch = table.Column<Guid>(type: "uuid", nullable: false, comment: "Process or counter epoch that makes resets explicit"),
                    ReportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Time at which Connector Host reported these metrics"),
                    ReceivedCount = table.Column<long>(type: "bigint", nullable: true, comment: "Raw source messages or sample attempts observed exactly once in this epoch before validation; null means unknown"),
                    DroppedCount = table.Column<long>(type: "bigint", nullable: true, comment: "Actual source samples intentionally dropped or rejected in this epoch; null means unknown"),
                    ErrorCount = table.Column<long>(type: "bigint", nullable: true, comment: "Actual collection or processing failures in this epoch; null means unknown"),
                    LastSampleAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Most recent actual source sample time; null means unknown"),
                    RetiredCounterEpochs = table.Column<string>(type: "text", nullable: false, comment: "Complete set of retired counter epoch identities, preventing delayed reports from reviving reset counters")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_connector_collection_health", x => x.Id);
                    table.ForeignKey(
                        name: "FK_connector_collection_health_application_instances_Applicati~",
                        column: x => x.ApplicationInstanceId,
                        principalSchema: "apphub",
                        principalTable: "application_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Latest recoverable collection health counters reported by each Connector Host connector/source.");

            migrationBuilder.CreateIndex(
                name: "IX_registration_idempotency_OrganizationId_EnvironmentId_Idemp~",
                schema: "apphub",
                table: "registration_idempotency",
                columns: new[] { "OrganizationId", "EnvironmentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_instances_OrganizationId_EnvironmentId_Instance~",
                schema: "apphub",
                table: "application_instances",
                columns: new[] { "OrganizationId", "EnvironmentId", "InstanceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_connector_collection_health_ApplicationInstanceId",
                schema: "apphub",
                table: "connector_collection_health",
                column: "ApplicationInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_connector_collection_health_OrganizationId_EnvironmentId_Co~",
                schema: "apphub",
                table: "connector_collection_health",
                columns: new[] { "OrganizationId", "EnvironmentId", "ConnectorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "connector_collection_health",
                schema: "apphub");

            migrationBuilder.DropIndex(
                name: "IX_registration_idempotency_OrganizationId_EnvironmentId_Idemp~",
                schema: "apphub",
                table: "registration_idempotency");

            migrationBuilder.DropIndex(
                name: "IX_application_instances_OrganizationId_EnvironmentId_Instance~",
                schema: "apphub",
                table: "application_instances");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                schema: "apphub",
                table: "registration_idempotency");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                schema: "apphub",
                table: "registration_idempotency");

            migrationBuilder.CreateIndex(
                name: "IX_registration_idempotency_IdempotencyKey",
                schema: "apphub",
                table: "registration_idempotency",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_instances_InstanceKey",
                schema: "apphub",
                table: "application_instances",
                column: "InstanceKey",
                unique: true);
        }
    }
}
