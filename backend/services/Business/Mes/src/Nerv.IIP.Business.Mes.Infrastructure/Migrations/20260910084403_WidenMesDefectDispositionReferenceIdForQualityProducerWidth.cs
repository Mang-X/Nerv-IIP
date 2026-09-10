using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenMesDefectDispositionReferenceIdForQualityProducerWidth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "disposition_reference_id",
                schema: "mes",
                table: "defect_records",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Downstream disposition reference copied verbatim from the Quality NCR: rework work order id, scrap movement id or supplier return document id. Width tracks the Quality producer columns nonconformance_reports.{rework_work_order_id, scrap_movement_id, return_document_id} (150) — see #3318.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldComment: "Downstream disposition reference such as rework work order, scrap movement or return document.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "disposition_reference_id",
                schema: "mes",
                table: "defect_records",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Downstream disposition reference such as rework work order, scrap movement or return document.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldNullable: true,
                oldComment: "Downstream disposition reference copied verbatim from the Quality NCR: rework work order id, scrap movement id or supplier return document id. Width tracks the Quality producer columns nonconformance_reports.{rework_work_order_id, scrap_movement_id, return_document_id} (150) — see #3318.");
        }
    }
}
