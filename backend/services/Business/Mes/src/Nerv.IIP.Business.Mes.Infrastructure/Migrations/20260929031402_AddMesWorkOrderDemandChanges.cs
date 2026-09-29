using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesWorkOrderDemandChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_order_demand_changes",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "MES demand change marker identifier."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization that owns the affected work order."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment that owns the affected work order."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MES business work order id affected by the source demand change."),
                    suggestion_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "DemandPlanning suggestion from which the MES work order was converted."),
                    demand_source_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Exact demand source reference pegged to the affected work order."),
                    sales_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "ERP sales order public id carried by the demand change event."),
                    order_version = table.Column<int>(type: "integer", nullable: false, comment: "Latest ERP sales order version applied to this demand marker; older events cannot overwrite it."),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the latest source demand change cancelled the demand.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_order_demand_changes", x => x.id);
                },
                comment: "Latest sales demand change fact for each MES work order and source demand reference.");

            migrationBuilder.CreateIndex(
                name: "ux_work_order_demand_changes_scope_order_demand",
                schema: "mes",
                table: "work_order_demand_changes",
                columns: new[] { "organization_id", "environment_id", "work_order_id", "demand_source_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_order_demand_changes",
                schema: "mes");
        }
    }
}
