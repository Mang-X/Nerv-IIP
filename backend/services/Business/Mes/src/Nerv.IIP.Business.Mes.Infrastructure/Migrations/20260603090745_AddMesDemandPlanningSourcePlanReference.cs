using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesDemandPlanningSourcePlanReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_demand_reference",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional DemandPlanning demand source reference used to trace the work order back to demand.");

            migrationBuilder.AddColumn<string>(
                name: "source_document_id",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Source production plan or planning suggestion public id copied into MES for durable traceability.");

            migrationBuilder.AddColumn<string>(
                name: "source_document_type",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Source document type copied from the planning service, for example PlanningSuggestion.");

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                schema: "mes",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Owning service that produced the source production plan reference, for example DemandPlanning.");

            migrationBuilder.AddColumn<string>(
                name: "uom_code",
                schema: "mes",
                table: "work_orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Unit of measure copied from the source production plan when the work order is converted from DemandPlanning.");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_source_plan",
                schema: "mes",
                table: "work_orders",
                columns: new[] { "source_system", "source_document_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_work_orders_source_plan",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_demand_reference",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_document_id",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_document_type",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "source_system",
                schema: "mes",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "uom_code",
                schema: "mes",
                table: "work_orders");
        }
    }
}
