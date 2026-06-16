using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloseBusinessMasterData407Gaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "bottleneck",
                schema: "business_masterdata",
                table: "work_centers",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Whether this work center is treated as a bottleneck resource for planning.");

            migrationBuilder.AddColumn<string>(
                name: "cost_center_code",
                schema: "business_masterdata",
                table: "work_centers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional ERP costing cost center code for the work center.");

            migrationBuilder.AddColumn<decimal>(
                name: "efficiency_rate",
                schema: "business_masterdata",
                table: "work_centers",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 1m,
                comment: "Default efficiency rate used to convert nominal capacity to rated capacity.");

            migrationBuilder.AddColumn<int>(
                name: "number_of_capacities",
                schema: "business_masterdata",
                table: "work_centers",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                comment: "Parallel capacity count such as machine count or labor station count.");

            migrationBuilder.AddColumn<decimal>(
                name: "utilization_rate",
                schema: "business_masterdata",
                table: "work_centers",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: false,
                defaultValue: 1m,
                comment: "Default utilization rate used to convert nominal capacity to rated capacity.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_from",
                schema: "business_masterdata",
                table: "work_calendars",
                type: "date",
                nullable: true,
                comment: "Optional local business date from which the calendar is valid.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                schema: "business_masterdata",
                table: "work_calendars",
                type: "date",
                nullable: true,
                comment: "Optional local business date through which the calendar is valid.");

            migrationBuilder.AddColumn<string>(
                name: "holiday_calendar_code",
                schema: "business_masterdata",
                table: "work_calendars",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                comment: "Optional external or reusable holiday calendar code referenced by this work calendar.");

            migrationBuilder.AddColumn<string>(
                name: "timezone",
                schema: "business_masterdata",
                table: "work_calendars",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC",
                comment: "IANA timezone used to interpret local working days, holidays and exceptions.");

            migrationBuilder.AddColumn<DateOnly>(
                name: "effective_to",
                schema: "business_masterdata",
                table: "uom_conversions",
                type: "date",
                nullable: true,
                comment: "Optional business date through which the conversion rule is effective.");

            migrationBuilder.AddColumn<string>(
                name: "abc_class",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                comment: "Optional ABC planning classification code.");

            migrationBuilder.AddColumn<int>(
                name: "goods_receipt_processing_time_days",
                schema: "business_masterdata",
                table: "skus",
                type: "integer",
                nullable: true,
                comment: "Optional goods receipt processing time in calendar days for planning snapshots.");

            migrationBuilder.AddColumn<int>(
                name: "in_house_production_time_days",
                schema: "business_masterdata",
                table: "skus",
                type: "integer",
                nullable: true,
                comment: "Optional in-house production lead time in calendar days for manufactured SKU planning.");

            migrationBuilder.AddColumn<string>(
                name: "lifecycle_status",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "active",
                comment: "SKU lifecycle status such as draft, active, blocked or obsolete.");

            migrationBuilder.AddColumn<decimal>(
                name: "lot_size_multiple",
                schema: "business_masterdata",
                table: "skus",
                type: "numeric(24,6)",
                precision: 24,
                scale: 6,
                nullable: true,
                comment: "Optional planned lot size multiple in the SKU base unit of measure.");

            migrationBuilder.AddColumn<string>(
                name: "lot_sizing_policy",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                comment: "Default lot sizing policy used by planning services when no site-specific override exists.");

            migrationBuilder.AddColumn<bool>(
                name: "manufacturing_enabled",
                schema: "business_masterdata",
                table: "skus",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether manufacturing and MES processes may use this SKU by default.");

            migrationBuilder.AddColumn<decimal>(
                name: "maximum_lot_size",
                schema: "business_masterdata",
                table: "skus",
                type: "numeric(24,6)",
                precision: 24,
                scale: 6,
                nullable: true,
                comment: "Optional maximum planned lot size in the SKU base unit of measure.");

            migrationBuilder.AddColumn<decimal>(
                name: "minimum_lot_size",
                schema: "business_masterdata",
                table: "skus",
                type: "numeric(24,6)",
                precision: 24,
                scale: 6,
                nullable: true,
                comment: "Optional minimum planned lot size in the SKU base unit of measure.");

            migrationBuilder.AddColumn<string>(
                name: "mrp_type",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Default MRP type consumed as shared SKU planning master data.");

            migrationBuilder.AddColumn<int>(
                name: "planned_delivery_time_days",
                schema: "business_masterdata",
                table: "skus",
                type: "integer",
                nullable: true,
                comment: "Optional planned delivery lead time in calendar days for externally procured SKU planning.");

            migrationBuilder.AddColumn<string>(
                name: "procurement_type",
                schema: "business_masterdata",
                table: "skus",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                comment: "Default procurement type such as make, buy or subcontract for planning snapshots.");

            migrationBuilder.AddColumn<bool>(
                name: "purchasing_enabled",
                schema: "business_masterdata",
                table: "skus",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether purchasing documents may use this SKU by default.");

            migrationBuilder.AddColumn<decimal>(
                name: "reorder_point_quantity",
                schema: "business_masterdata",
                table: "skus",
                type: "numeric(24,6)",
                precision: 24,
                scale: 6,
                nullable: true,
                comment: "Optional default reorder point quantity in the SKU base unit of measure.");

            migrationBuilder.AddColumn<decimal>(
                name: "safety_stock_quantity",
                schema: "business_masterdata",
                table: "skus",
                type: "numeric(24,6)",
                precision: 24,
                scale: 6,
                nullable: true,
                comment: "Optional default safety stock quantity in the SKU base unit of measure.");

            migrationBuilder.AddColumn<bool>(
                name: "sales_enabled",
                schema: "business_masterdata",
                table: "skus",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                comment: "Whether sales documents may use this SKU by default.");

            migrationBuilder.AddColumn<int>(
                name: "break_minutes",
                schema: "business_masterdata",
                table: "shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Planned break minutes inside the shift window.");

            migrationBuilder.AddColumn<string>(
                name: "default_currency_code",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                comment: "Optional default transaction currency code for the business partner.");

            migrationBuilder.AddColumn<string>(
                name: "payment_terms_code",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional default payment terms code for supplier or customer transactions.");

            migrationBuilder.AddColumn<string>(
                name: "primary_address",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                comment: "Optional primary address summary for procurement, sales or logistics defaults.");

            migrationBuilder.AddColumn<string>(
                name: "primary_contact_email",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Optional primary contact email address.");

            migrationBuilder.AddColumn<string>(
                name: "primary_contact_name",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                comment: "Optional primary contact display name.");

            migrationBuilder.AddColumn<string>(
                name: "primary_contact_phone",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                comment: "Optional primary contact phone number.");

            migrationBuilder.AddColumn<string>(
                name: "tax_region_code",
                schema: "business_masterdata",
                table: "business_partners",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional tax region code used by ERP procurement, sales and finance documents.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bottleneck",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "cost_center_code",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "efficiency_rate",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "number_of_capacities",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "utilization_rate",
                schema: "business_masterdata",
                table: "work_centers");

            migrationBuilder.DropColumn(
                name: "effective_from",
                schema: "business_masterdata",
                table: "work_calendars");

            migrationBuilder.DropColumn(
                name: "effective_to",
                schema: "business_masterdata",
                table: "work_calendars");

            migrationBuilder.DropColumn(
                name: "holiday_calendar_code",
                schema: "business_masterdata",
                table: "work_calendars");

            migrationBuilder.DropColumn(
                name: "timezone",
                schema: "business_masterdata",
                table: "work_calendars");

            migrationBuilder.DropColumn(
                name: "effective_to",
                schema: "business_masterdata",
                table: "uom_conversions");

            migrationBuilder.DropColumn(
                name: "abc_class",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "goods_receipt_processing_time_days",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "in_house_production_time_days",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "lifecycle_status",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "lot_size_multiple",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "lot_sizing_policy",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "manufacturing_enabled",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "maximum_lot_size",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "minimum_lot_size",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "mrp_type",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "planned_delivery_time_days",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "procurement_type",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "purchasing_enabled",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "reorder_point_quantity",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "safety_stock_quantity",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "sales_enabled",
                schema: "business_masterdata",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "break_minutes",
                schema: "business_masterdata",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "default_currency_code",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "payment_terms_code",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "primary_address",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "primary_contact_email",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "primary_contact_name",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "primary_contact_phone",
                schema: "business_masterdata",
                table: "business_partners");

            migrationBuilder.DropColumn(
                name: "tax_region_code",
                schema: "business_masterdata",
                table: "business_partners");
        }
    }
}
