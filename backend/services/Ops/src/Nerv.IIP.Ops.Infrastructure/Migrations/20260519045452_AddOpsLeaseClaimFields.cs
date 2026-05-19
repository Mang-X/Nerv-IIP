using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpsLeaseClaimFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbandonReason",
                schema: "ops",
                table: "operation_attempts",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                comment: "Reason recorded when the attempt lease is abandoned or times out.");

            migrationBuilder.AddColumn<int>(
                name: "AttemptNo",
                schema: "ops",
                table: "operation_attempts",
                type: "integer",
                nullable: true,
                comment: "One-based attempt number for this operation task.");

            migrationBuilder.AddColumn<string>(
                name: "LeaseId",
                schema: "ops",
                table: "operation_attempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                comment: "Lease identifier returned by Ops claim and required for heartbeat or abandon updates; null for legacy attempts created before lease claim protocol fields existed.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeasedAtUtc",
                schema: "ops",
                table: "operation_attempts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when Ops granted this lease.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeasedUntilUtc",
                schema: "ops",
                table: "operation_attempts",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the lease expires and becomes eligible for requeue.");

            migrationBuilder.AddColumn<int>(
                name: "MaxAttempts",
                schema: "ops",
                table: "operation_attempts",
                type: "integer",
                nullable: true,
                comment: "Maximum attempts allowed before an expired or abandoned task becomes failed.");

            migrationBuilder.CreateIndex(
                name: "IX_operation_attempts_Status_LeasedUntilUtc",
                schema: "ops",
                table: "operation_attempts",
                columns: new[] { "Status", "LeasedUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_operation_attempts_Status_LeasedUntilUtc",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.DropColumn(
                name: "AbandonReason",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.DropColumn(
                name: "AttemptNo",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.DropColumn(
                name: "LeasedAtUtc",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.DropColumn(
                name: "LeasedUntilUtc",
                schema: "ops",
                table: "operation_attempts");

            migrationBuilder.DropColumn(
                name: "MaxAttempts",
                schema: "ops",
                table: "operation_attempts");
        }
    }
}
