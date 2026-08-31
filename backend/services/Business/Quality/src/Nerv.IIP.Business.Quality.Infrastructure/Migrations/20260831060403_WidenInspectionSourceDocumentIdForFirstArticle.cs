using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Quality.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenInspectionSourceDocumentIdForFirstArticle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                comment: "External source document id such as inspection plan, report or return id; NCRs opened from a first-article inspection carry that record's composite '{workOrderId}:{operationTaskId}' source identity.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldComment: "External source document id such as inspection plan, report or return id.");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                comment: "Source document public id, or the composite first-article source identity '{workOrderId}:{operationTaskId}' produced by FirstArticleInspection.SourceDocumentId.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldComment: "Source document public id.");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                comment: "Source document or operation public id, or the composite first-article source identity '{workOrderId}:{operationTaskId}' produced by FirstArticleInspection.SourceDocumentId.",
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150,
                oldComment: "Source document or operation public id.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "quality",
                table: "nonconformance_reports",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                comment: "External source document id such as inspection plan, report or return id.",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldComment: "External source document id such as inspection plan, report or return id; NCRs opened from a first-article inspection carry that record's composite '{workOrderId}:{operationTaskId}' source identity.");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "quality",
                table: "inspection_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                comment: "Source document public id.",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldComment: "Source document public id, or the composite first-article source identity '{workOrderId}:{operationTaskId}' produced by FirstArticleInspection.SourceDocumentId.");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "quality",
                table: "inspection_records",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                comment: "Source document or operation public id.",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldComment: "Source document or operation public id, or the composite first-article source identity '{workOrderId}:{operationTaskId}' produced by FirstArticleInspection.SourceDocumentId.");
        }
    }
}
