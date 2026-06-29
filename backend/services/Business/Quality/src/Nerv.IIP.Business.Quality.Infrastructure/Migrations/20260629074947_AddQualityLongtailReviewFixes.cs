using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityLongtailReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at_utc",
                schema: "quality",
                table: "corrective_action_items",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when this CAPA action item was completed.");

            migrationBuilder.AddColumn<string>(
                name: "completed_by_user_id",
                schema: "quality",
                table: "corrective_action_items",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "User id that completed this CAPA action item.");

            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    duplicate_key text;
                BEGIN
                    SELECT format(
                        'organization_id=%s, environment_id=%s, source_type=%s, source_service=%s, source_document_id=%s, sku_code=%s, count=%s',
                        organization_id,
                        environment_id,
                        source_type,
                        source_service,
                        source_document_id,
                        sku_code,
                        count(*))
                    INTO duplicate_key
                    FROM quality.inspection_records
                    GROUP BY organization_id, environment_id, source_type, source_service, source_document_id, sku_code
                    HAVING count(*) > 1
                    LIMIT 1;

                    IF duplicate_key IS NOT NULL THEN
                        RAISE EXCEPTION 'Cannot add unique inspection source/SKU index because duplicate Quality inspection records already exist: %. Resolve historical duplicates before applying migration 20260629074947_AddQualityLongtailReviewFixes.', duplicate_key;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_inspection_records_organization_id_environment_id_source_t~1",
                schema: "quality",
                table: "inspection_records",
                columns: new[] { "organization_id", "environment_id", "source_type", "source_service", "source_document_id", "sku_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inspection_records_organization_id_environment_id_source_t~1",
                schema: "quality",
                table: "inspection_records");

            migrationBuilder.DropColumn(
                name: "completed_at_utc",
                schema: "quality",
                table: "corrective_action_items");

            migrationBuilder.DropColumn(
                name: "completed_by_user_id",
                schema: "quality",
                table: "corrective_action_items");
        }
    }
}
