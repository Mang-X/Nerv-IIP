using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.AppHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "apphub");

            migrationBuilder.CreateTable(
                name: "application_instances",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application instance aggregate id"),
                    OrganizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization id"),
                    EnvironmentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id"),
                    ApplicationKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Application protocol key"),
                    Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Application version"),
                    NodeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Node protocol key"),
                    InstanceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Instance protocol key"),
                    InstanceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Instance display name"),
                    ReportedStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Reported status"),
                    HealthStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Health status"),
                    Metadata = table.Column<string>(type: "text", nullable: false),
                    Capabilities = table.Column<string>(type: "text", nullable: false),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag"),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application aggregate id"),
                    OrganizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization id"),
                    EnvironmentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id"),
                    ApplicationKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Application protocol key"),
                    ApplicationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Application display name"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag"),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "apphub",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Instance = table.Column<string>(type: "text", nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_locks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cap_published_messages",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusName = table.Column<string>(type: "text", nullable: false),
                    DataSourceName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_published_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_received_messages",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Group = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StatusName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_received_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "managed_nodes",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Managed node aggregate id"),
                    OrganizationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization id"),
                    EnvironmentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id"),
                    NodeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Node protocol key"),
                    NodeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Node display name"),
                    DeploymentKind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Node deployment kind"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag"),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_nodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "registration_idempotency",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Registration idempotency id"),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Idempotency key"),
                    RegistrationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Registration id"),
                    InstanceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false, comment: "Instance protocol key"),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag"),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registration_idempotency", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "instance_heartbeat",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Heartbeat id"),
                    ApplicationInstanceId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application instance aggregate id"),
                    LastHeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Last heartbeat time"),
                    Reachable = table.Column<bool>(type: "boolean", nullable: false, comment: "Reachability flag"),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false, comment: "Observed latency in milliseconds")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_heartbeat", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_heartbeat_application_instances_ApplicationInstanc~",
                        column: x => x.ApplicationInstanceId,
                        principalSchema: "apphub",
                        principalTable: "application_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_state_history",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "State history id"),
                    ApplicationInstanceId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application instance aggregate id"),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "State observation time"),
                    ReportedStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Reported status"),
                    HealthStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Health status"),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "State summary")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_state_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_state_history_application_instances_ApplicationIns~",
                        column: x => x.ApplicationInstanceId,
                        principalSchema: "apphub",
                        principalTable: "application_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instance_status_changes",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Status change id"),
                    ApplicationInstanceId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application instance aggregate id"),
                    PreviousStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Previous reported status"),
                    CurrentStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Current reported status"),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Status change time")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_status_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instance_status_changes_application_instances_ApplicationIn~",
                        column: x => x.ApplicationInstanceId,
                        principalSchema: "apphub",
                        principalTable: "application_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_versions",
                schema: "apphub",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application version id"),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false, comment: "Application aggregate id"),
                    Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Application version")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_application_versions_applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "apphub",
                        principalTable: "applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_instances_InstanceKey",
                schema: "apphub",
                table: "application_instances",
                column: "InstanceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_instances_OrganizationId_EnvironmentId_Applicat~",
                schema: "apphub",
                table: "application_instances",
                columns: new[] { "OrganizationId", "EnvironmentId", "ApplicationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_application_versions_ApplicationId_Version",
                schema: "apphub",
                table: "application_versions",
                columns: new[] { "ApplicationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_applications_OrganizationId_EnvironmentId_ApplicationKey",
                schema: "apphub",
                table: "applications",
                columns: new[] { "OrganizationId", "EnvironmentId", "ApplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instance_heartbeat_ApplicationInstanceId",
                schema: "apphub",
                table: "instance_heartbeat",
                column: "ApplicationInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instance_state_history_ApplicationInstanceId_ObservedAtUtc",
                schema: "apphub",
                table: "instance_state_history",
                columns: new[] { "ApplicationInstanceId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_instance_status_changes_ApplicationInstanceId_ChangedAtUtc",
                schema: "apphub",
                table: "instance_status_changes",
                columns: new[] { "ApplicationInstanceId", "ChangedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_managed_nodes_OrganizationId_EnvironmentId_NodeKey",
                schema: "apphub",
                table: "managed_nodes",
                columns: new[] { "OrganizationId", "EnvironmentId", "NodeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_registration_idempotency_IdempotencyKey",
                schema: "apphub",
                table: "registration_idempotency",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_versions",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "instance_heartbeat",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "instance_state_history",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "instance_status_changes",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "managed_nodes",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "registration_idempotency",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "applications",
                schema: "apphub");

            migrationBuilder.DropTable(
                name: "application_instances",
                schema: "apphub");
        }
    }
}
