using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenMesQualityHoldSourceDocumentIdForQualityCompositeIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "mes",
                table: "quality_hold_transitions",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                comment: "Stable source document identity whose hold lifecycle changed; carries the Quality inspection record source identity verbatim, which for first-article and periodic inspections is a composite value rather than a bare MES id.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Stable MES source document identifier whose hold lifecycle changed.");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(250)",
                maxLength: 250,
                nullable: false,
                comment: "Source document identity copied verbatim from the Quality inspection record; width matches the Quality producer column because first-article and periodic inspections carry a composite identity, not a bare MES work order or operation task id.",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldComment: "Source document id referenced by the Quality inspection record.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "mes",
                table: "quality_hold_transitions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Stable MES source document identifier whose hold lifecycle changed.",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldComment: "Stable source document identity whose hold lifecycle changed; carries the Quality inspection record source identity verbatim, which for first-article and periodic inspections is a composite value rather than a bare MES id.");

            migrationBuilder.AlterColumn<string>(
                name: "source_document_id",
                schema: "mes",
                table: "quality_hold_contexts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                comment: "Source document id referenced by the Quality inspection record.",
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldComment: "Source document identity copied verbatim from the Quality inspection record; width matches the Quality producer column because first-article and periodic inspections carry a composite identity, not a bare MES work order or operation task id.");
        }
    }
}
