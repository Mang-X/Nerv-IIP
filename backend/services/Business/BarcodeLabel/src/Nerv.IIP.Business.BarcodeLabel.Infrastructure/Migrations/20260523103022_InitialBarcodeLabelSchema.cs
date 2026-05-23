using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.BarcodeLabel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBarcodeLabelSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "barcode");

            migrationBuilder.CreateTable(
                name: "barcode_rules",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Barcode rule aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the barcode rule."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the barcode rule applies."),
                    rule_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business barcode rule code unique in an organization and environment."),
                    barcode_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Barcode symbology such as code128, qr or datamatrix."),
                    prefix = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, comment: "Barcode prefix included in generated label values."),
                    length = table.Column<int>(type: "integer", nullable: false, comment: "Maximum generated barcode length."),
                    checksum_rule = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Checksum policy name for generated barcode values."),
                    allowed_source_document_types = table.Column<List<string>>(type: "text[]", nullable: false, comment: "Allowed source document types for this barcode rule."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Rule lifecycle status: active or inactive."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the rule was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the rule was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_barcode_rules", x => x.id);
                },
                comment: "Barcode rule facts for deterministic label value generation.");

            migrationBuilder.CreateTable(
                name: "CAPLock",
                schema: "barcode",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPLock", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CAPPublishedMessage",
                schema: "barcode",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPPublishedMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CAPReceivedMessage",
                schema: "barcode",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "integer", nullable: true),
                    Added = table.Column<DateTime>(type: "TIMESTAMP", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TIMESTAMP", nullable: true),
                    StatusName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CAPReceivedMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "label_print_batches",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Label print batch aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the print batch."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the print batch was created."),
                    barcode_rule_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Barcode rule id used for deterministic label generation."),
                    label_template_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Label template id used for the print batch."),
                    source_document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source workflow or document type requesting label printing."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source business document public id."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Client supplied idempotency key for print batch creation."),
                    label_values_json = table.Column<string>(type: "text", nullable: false, comment: "Label variable values JSON captured for repeatable printing."),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false, comment: "Requested number of labels generated for the batch."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Print batch status such as completed."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the print batch was created."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the print batch finished generation.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_print_batches", x => x.id);
                },
                comment: "Label print batch facts and idempotency records.");

            migrationBuilder.CreateTable(
                name: "label_templates",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Label template aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the template."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the template applies."),
                    template_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business label template code unique in an organization and environment."),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Human readable label template name."),
                    template_file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "FileStorage file id for the template asset; object keys are not stored publicly."),
                    variable_schema_json = table.Column<string>(type: "text", nullable: false, comment: "Template variable schema JSON consumed by print clients."),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Template lifecycle status: active or inactive."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the template was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the template was last changed.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_templates", x => x.id);
                },
                comment: "Label template metadata with FileStorage file id references only.");

            migrationBuilder.CreateTable(
                name: "scan_records",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Scan record aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the scan fact."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the scan occurred."),
                    device_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Device or PDA code that captured the scan."),
                    scanned_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Raw barcode value scanned by the device."),
                    source_workflow = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Workflow that produced the scan fact, such as receiving or picking."),
                    source_document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source business document public id associated with the scan."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Client supplied idempotency key for scan creation."),
                    result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "Scan result: accepted or rejected."),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true, comment: "Reason for rejected scans."),
                    scanned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the scan was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_records", x => x.id);
                },
                comment: "Append-only barcode scan facts captured from devices and workflows.");

            migrationBuilder.CreateTable(
                name: "label_print_items",
                schema: "barcode",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Label print item id."),
                    label_print_batch_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning label print batch id."),
                    sequence_no = table.Column<int>(type: "integer", nullable: false, comment: "Generated label sequence number within the print batch."),
                    label_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Generated deterministic barcode or label value."),
                    file_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional FileStorage file id for rendered label output."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the print item was generated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_label_print_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_label_print_items_label_print_batches_label_print_batch_id",
                        column: x => x.label_print_batch_id,
                        principalSchema: "barcode",
                        principalTable: "label_print_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Generated label print item facts for a print batch.");

            migrationBuilder.CreateIndex(
                name: "IX_barcode_rules_organization_id_environment_id_rule_code",
                schema: "barcode",
                table: "barcode_rules",
                columns: new[] { "organization_id", "environment_id", "rule_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "barcode",
                table: "CAPPublishedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "barcode",
                table: "CAPPublishedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "barcode",
                table: "CAPReceivedMessage",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "barcode",
                table: "CAPReceivedMessage",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_batches_organization_id_environment_id_idempote~",
                schema: "barcode",
                table: "label_print_batches",
                columns: new[] { "organization_id", "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_label_print_batches_organization_id_environment_id_source_d~",
                schema: "barcode",
                table: "label_print_batches",
                columns: new[] { "organization_id", "environment_id", "source_document_type", "source_document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_label_print_items_label_print_batch_id_sequence_no",
                schema: "barcode",
                table: "label_print_items",
                columns: new[] { "label_print_batch_id", "sequence_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_label_print_items_label_value",
                schema: "barcode",
                table: "label_print_items",
                column: "label_value");

            migrationBuilder.CreateIndex(
                name: "IX_label_templates_organization_id_environment_id_status",
                schema: "barcode",
                table: "label_templates",
                columns: new[] { "organization_id", "environment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_label_templates_organization_id_environment_id_template_code",
                schema: "barcode",
                table: "label_templates",
                columns: new[] { "organization_id", "environment_id", "template_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_device_code_sca~",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "device_code", "scanned_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_idempotency_key",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_scanned_value",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "scanned_value" });

            migrationBuilder.CreateIndex(
                name: "IX_scan_records_organization_id_environment_id_source_workflow~",
                schema: "barcode",
                table: "scan_records",
                columns: new[] { "organization_id", "environment_id", "source_workflow", "source_document_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barcode_rules",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "CAPLock",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "CAPPublishedMessage",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "CAPReceivedMessage",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "label_print_items",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "label_templates",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "scan_records",
                schema: "barcode");

            migrationBuilder.DropTable(
                name: "label_print_batches",
                schema: "barcode");
        }
    }
}
