using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Ops.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalDecidedAtUtc",
                schema: "ops",
                table: "operation_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Approval decision time in UTC.");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalDecidedBy",
                schema: "ops",
                table: "operation_tasks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Actor that approved or rejected the operation.");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalDecisionReason",
                schema: "ops",
                table: "operation_tasks",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                comment: "Approval decision reason.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalRequestedAtUtc",
                schema: "ops",
                table: "operation_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Approval request time in UTC.");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalRequestedBy",
                schema: "ops",
                table: "operation_tasks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Actor that requested approval for a high-risk operation.");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                schema: "ops",
                table: "operation_tasks",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                comment: "Operation approval status when approval is required.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalDecidedAtUtc",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "ApprovalDecidedBy",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "ApprovalDecisionReason",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "ApprovalRequestedAtUtc",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "ApprovalRequestedBy",
                schema: "ops",
                table: "operation_tasks");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                schema: "ops",
                table: "operation_tasks");
        }
    }
}
