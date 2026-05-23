using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.ProductEngineering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteProductEngineeringReleaseFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "engineering_boms",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Engineering BOM aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the EBOM is valid."),
                    bom_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Engineering BOM business code."),
                    revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Engineering BOM revision."),
                    parent_item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Parent engineering item code."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Engineering BOM lifecycle status."),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "First effective date after release."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the EBOM was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the EBOM was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_boms", x => x.id);
                },
                comment: "ProductEngineering released and draft engineering BOM versions.");

            migrationBuilder.CreateTable(
                name: "engineering_changes",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Engineering change aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the change applies."),
                    change_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Engineering change order or notice number."),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, comment: "Engineering change reason."),
                    approval_reference_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Business approval chain or approval result reference id."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Engineering change lifecycle status."),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "First effective date after release."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the engineering change was opened."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the engineering change was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_changes", x => x.id);
                },
                comment: "ProductEngineering ECO and ECN change release facts.");

            migrationBuilder.CreateTable(
                name: "engineering_documents",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Engineering document aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the document is valid."),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business document number."),
                    revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Document revision."),
                    file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "File Storage public file id; object keys are not stored."),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false, comment: "Original or display file name."),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, comment: "File content type."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Engineering document type such as CAD drawing or process sheet."),
                    registered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the document reference was registered.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_documents", x => x.id);
                },
                comment: "ProductEngineering engineering document references to File Storage files such as CAD drawings and design packages.");

            migrationBuilder.CreateTable(
                name: "engineering_items",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Engineering item aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the item revision is valid."),
                    item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Engineering item code."),
                    revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Engineering item revision."),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Engineering item display name."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Engineering item lifecycle status."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the item revision was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the item revision was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_items", x => x.id);
                },
                comment: "ProductEngineering versioned engineering item revisions used by EBOM authoring.");

            migrationBuilder.CreateTable(
                name: "manufacturing_boms",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Manufacturing BOM aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the MBOM is valid."),
                    bom_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Manufacturing BOM business code."),
                    revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Manufacturing BOM revision."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Produced SKU code."),
                    engineering_bom_version_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Released EBOM version id used as design source."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Manufacturing BOM lifecycle status."),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "First effective date after release."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the MBOM was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the MBOM was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturing_boms", x => x.id);
                },
                comment: "ProductEngineering manufacturing BOM versions that reference released EBOM facts and process recipe lines.");

            migrationBuilder.CreateTable(
                name: "routings",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Routing aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the routing is valid."),
                    routing_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Routing business code."),
                    revision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Routing revision."),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Produced SKU code."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Routing lifecycle status."),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true, comment: "First effective date after release."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the routing was created."),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the routing was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routings", x => x.id);
                },
                comment: "ProductEngineering routing versions with ordered work center operation steps.");

            migrationBuilder.CreateTable(
                name: "engineering_bom_lines",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    child_item_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Child engineering item code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Component quantity."),
                    unit_of_measure_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quantity unit of measure code."),
                    engineering_bom_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning engineering BOM id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_bom_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_engineering_bom_lines_engineering_boms_engineering_bom_id",
                        column: x => x.engineering_bom_id,
                        principalSchema: "product_engineering",
                        principalTable: "engineering_boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Engineering BOM component lines.");

            migrationBuilder.CreateTable(
                name: "engineering_change_affected_versions",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version_kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Affected version kind such as document, engineering-bom, manufacturing-bom, routing or production-version."),
                    version_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Affected version id."),
                    engineering_change_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning engineering change id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_engineering_change_affected_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_engineering_change_affected_versions_engineering_changes_en~",
                        column: x => x.engineering_change_id,
                        principalSchema: "product_engineering",
                        principalTable: "engineering_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Engineering change affected document, BOM, routing or production version references.");

            migrationBuilder.CreateTable(
                name: "manufacturing_bom_material_lines",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sku_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Consumed SKU code."),
                    quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Consumed material quantity."),
                    unit_of_measure_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Quantity unit of measure code."),
                    scrap_rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Expected scrap rate for the material line."),
                    manufacturing_bom_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning manufacturing BOM id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturing_bom_material_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_manufacturing_bom_material_lines_manufacturing_boms_manufac~",
                        column: x => x.manufacturing_bom_id,
                        principalSchema: "product_engineering",
                        principalTable: "manufacturing_boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Manufacturing BOM SKU material lines.");

            migrationBuilder.CreateTable(
                name: "manufacturing_bom_recipe_lines",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    parameter_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Process parameter code."),
                    target_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Target process parameter value."),
                    unit_of_measure_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Parameter unit of measure code."),
                    manufacturing_bom_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning manufacturing BOM id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_manufacturing_bom_recipe_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_manufacturing_bom_recipe_lines_manufacturing_boms_manufactu~",
                        column: x => x.manufacturing_bom_id,
                        principalSchema: "product_engineering",
                        principalTable: "manufacturing_boms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Manufacturing BOM process recipe and formula parameter lines.");

            migrationBuilder.CreateTable(
                name: "routing_operations",
                schema: "product_engineering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sequence = table.Column<int>(type: "integer", nullable: false, comment: "Positive operation sequence number."),
                    work_center_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData work center code reference."),
                    operation_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Operation display name."),
                    standard_minutes = table.Column<int>(type: "integer", nullable: false, comment: "Standard operation duration in minutes."),
                    routing_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning routing id.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_routing_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_routing_operations_routings_routing_id",
                        column: x => x.routing_id,
                        principalSchema: "product_engineering",
                        principalTable: "routings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Routing ordered operation steps and work center references.");

            migrationBuilder.CreateIndex(
                name: "IX_engineering_bom_lines_engineering_bom_id_child_item_code",
                schema: "product_engineering",
                table: "engineering_bom_lines",
                columns: new[] { "engineering_bom_id", "child_item_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_boms_organization_id_environment_id_bom_code_re~",
                schema: "product_engineering",
                table: "engineering_boms",
                columns: new[] { "organization_id", "environment_id", "bom_code", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_boms_organization_id_environment_id_parent_item~",
                schema: "product_engineering",
                table: "engineering_boms",
                columns: new[] { "organization_id", "environment_id", "parent_item_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_engineering_change_affected_versions_engineering_change_id_~",
                schema: "product_engineering",
                table: "engineering_change_affected_versions",
                columns: new[] { "engineering_change_id", "version_kind", "version_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_changes_organization_id_environment_id_change_n~",
                schema: "product_engineering",
                table: "engineering_changes",
                columns: new[] { "organization_id", "environment_id", "change_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_changes_organization_id_environment_id_status",
                schema: "product_engineering",
                table: "engineering_changes",
                columns: new[] { "organization_id", "environment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_engineering_documents_organization_id_environment_id_docume~",
                schema: "product_engineering",
                table: "engineering_documents",
                columns: new[] { "organization_id", "environment_id", "document_number", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_documents_organization_id_environment_id_file_i~",
                schema: "product_engineering",
                table: "engineering_documents",
                columns: new[] { "organization_id", "environment_id", "file_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_items_organization_id_environment_id_item_code_~",
                schema: "product_engineering",
                table: "engineering_items",
                columns: new[] { "organization_id", "environment_id", "item_code", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_engineering_items_organization_id_environment_id_status",
                schema: "product_engineering",
                table: "engineering_items",
                columns: new[] { "organization_id", "environment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_bom_material_lines_manufacturing_bom_id_sku_c~",
                schema: "product_engineering",
                table: "manufacturing_bom_material_lines",
                columns: new[] { "manufacturing_bom_id", "sku_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_bom_recipe_lines_manufacturing_bom_id_paramet~",
                schema: "product_engineering",
                table: "manufacturing_bom_recipe_lines",
                columns: new[] { "manufacturing_bom_id", "parameter_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_boms_organization_id_environment_id_bom_code_~",
                schema: "product_engineering",
                table: "manufacturing_boms",
                columns: new[] { "organization_id", "environment_id", "bom_code", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_manufacturing_boms_organization_id_environment_id_sku_code_~",
                schema: "product_engineering",
                table: "manufacturing_boms",
                columns: new[] { "organization_id", "environment_id", "sku_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_routing_operations_routing_id_sequence",
                schema: "product_engineering",
                table: "routing_operations",
                columns: new[] { "routing_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routings_organization_id_environment_id_routing_code_revisi~",
                schema: "product_engineering",
                table: "routings",
                columns: new[] { "organization_id", "environment_id", "routing_code", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_routings_organization_id_environment_id_sku_code_status",
                schema: "product_engineering",
                table: "routings",
                columns: new[] { "organization_id", "environment_id", "sku_code", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "engineering_bom_lines",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "engineering_change_affected_versions",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "engineering_documents",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "engineering_items",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "manufacturing_bom_material_lines",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "manufacturing_bom_recipe_lines",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "routing_operations",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "engineering_boms",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "engineering_changes",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "manufacturing_boms",
                schema: "product_engineering");

            migrationBuilder.DropTable(
                name: "routings",
                schema: "product_engineering");
        }
    }
}
