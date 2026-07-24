using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityReinspectionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inspection_records_organization_id_environment_id_source_t~1",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.AddColumn<int>(
                name: "attempt_number",
                schema: "quality",
                table: "inspection_records",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "One-based inspection attempt number within the same source and SKU history.");

            migrationBuilder.AddColumn<Guid>(
                name: "reinspection_of_inspection_record_id",
                schema: "quality",
                table: "inspection_records",
                type: "uuid",
                nullable: true,
                comment: "Previous inspection record id targeted by this reinspection attempt; null for the initial attempt.");

            migrationBuilder.CreateIndex(
                name: "ux_inspection_records_reinspection_predecessor",
                schema: "quality",
                table: "inspection_records",
                column: "reinspection_of_inspection_record_id",
                unique: true,
                filter: "\"reinspection_of_inspection_record_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_inspection_records_source_attempt",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_service", "source_document_id", "sku_code", "attempt_number" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_inspection_records_attempt_positive",
                schema: "quality",
                table: "inspection_records",
                sql: "attempt_number > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_inspection_records_inspection_records_reinspection_of_inspe~",
                schema: "quality",
                table: "inspection_records",
                column: "reinspection_of_inspection_record_id",
                principalSchema: "quality",
                principalTable: "inspection_records",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_inspection_records_inspection_records_reinspection_of_inspe~",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropIndex(
                name: "ux_inspection_records_reinspection_predecessor",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropIndex(
                name: "ux_inspection_records_source_attempt",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropCheckConstraint(
                name: "ck_inspection_records_attempt_positive",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "attempt_number",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "reinspection_of_inspection_record_id",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.CreateIndex(
                name: "IX_inspection_records_organization_id_environment_id_source_t~1",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_service", "source_document_id", "sku_code" },
                unique: true);
        }
    }
}
