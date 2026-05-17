using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "ops",
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
                schema: "ops",
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
                schema: "ops",
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
                name: "operation_tasks",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Operation task identifier."),
                    OrganizationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Organization identifier."),
                    EnvironmentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Environment identifier."),
                    InstanceKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Target instance key."),
                    OperationCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Operation code."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Operation task status."),
                    RequestedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Requester."),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Requested time in UTC."),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, comment: "Request idempotency key."),
                    IdempotencyScope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Organization and environment scoped idempotency key."),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Correlation identifier."),
                    ParametersJson = table.Column<string>(type: "text", nullable: false, comment: "Serialized operation parameters."),
                    Deleted = table.Column<bool>(type: "boolean", nullable: false, comment: "Soft delete flag."),
                    RowVersion = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_records",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Audit record identifier."),
                    OperationTaskId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Operation task identifier."),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Audit action."),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Audit actor."),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Audit occurrence time in UTC."),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Correlation identifier.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_records_operation_tasks_OperationTaskId",
                        column: x => x.OperationTaskId,
                        principalSchema: "ops",
                        principalTable: "operation_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operation_attempts",
                schema: "ops",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Operation attempt identifier."),
                    OperationTaskId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Operation task identifier."),
                    ConnectorHostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Connector host identifier."),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Attempt status."),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Attempt start time in UTC."),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Attempt finish time in UTC."),
                    FailureJson = table.Column<string>(type: "text", nullable: true, comment: "Serialized failure reason.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_operation_attempts_operation_tasks_OperationTaskId",
                        column: x => x.OperationTaskId,
                        principalSchema: "ops",
                        principalTable: "operation_tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_records_OperationTaskId_OccurredAtUtc",
                schema: "ops",
                table: "audit_records",
                columns: new[] { "OperationTaskId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_attempts_OperationTaskId",
                schema: "ops",
                table: "operation_attempts",
                column: "OperationTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_operation_tasks_IdempotencyScope",
                schema: "ops",
                table: "operation_tasks",
                column: "IdempotencyScope",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_operation_tasks_OrganizationId_EnvironmentId_Status_Request~",
                schema: "ops",
                table: "operation_tasks",
                columns: new[] { "OrganizationId", "EnvironmentId", "Status", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_records",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "operation_attempts",
                schema: "ops");

            migrationBuilder.DropTable(
                name: "operation_tasks",
                schema: "ops");
        }
    }
}
