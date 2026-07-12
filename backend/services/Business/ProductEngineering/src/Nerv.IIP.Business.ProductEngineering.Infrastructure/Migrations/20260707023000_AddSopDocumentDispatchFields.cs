using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSopDocumentDispatchFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "engineering_documents",
                schema: "product_engineering",
                comment: "ProductEngineering engineering document and SOP references to File Storage files such as CAD drawings, process sheets, and work instructions.",
                oldComment: "ProductEngineering engineering document references to File Storage files such as CAD drawings and design packages.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_date",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "date",
                nullable: true,
                comment: "Factory business date from which the SOP/work instruction version is effective.");

            migrationBuilder.AddColumn<string>(
                name: "operation_code",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional StandardOperation code when this document is a SOP/work instruction.");

            migrationBuilder.AddColumn<string>(
                name: "routing_code",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional routing code narrowing SOP dispatch to a routing operation.");

            migrationBuilder.AddColumn<string>(
                name: "routing_revision",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Optional routing revision narrowing SOP dispatch to a routing operation.");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Published",
                comment: "Engineering document lifecycle status: Published or Archived for SOP dispatch.");

            migrationBuilder.AddColumn<string>(
                name: "work_center_code",
                schema: "product_engineering",
                table: "engineering_documents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional work center code narrowing SOP dispatch for an operation.");

            migrationBuilder.CreateIndex(
                name: "IX_engineering_documents_organization_id_environment_id_docum~1",
                schema: "product_engineering",
                table: "engineering_documents",
                columns: new[] { "organization_id", "environment_id", "document_type", "operation_code", "work_center_code", "status", "effective_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_engineering_documents_organization_id_environment_id_docum~1",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "effective_date",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "operation_code",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "routing_code",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "routing_revision",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.DropColumn(
                name: "work_center_code",
                schema: "product_engineering",
                table: "engineering_documents");

            migrationBuilder.AlterTable(
                name: "engineering_documents",
                schema: "product_engineering",
                comment: "ProductEngineering engineering document references to File Storage files such as CAD drawings and design packages.",
                oldComment: "ProductEngineering engineering document and SOP references to File Storage files such as CAD drawings, process sheets, and work instructions.");
        }
    }
}
