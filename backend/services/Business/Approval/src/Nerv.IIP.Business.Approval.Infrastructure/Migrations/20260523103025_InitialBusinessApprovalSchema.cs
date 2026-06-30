using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nerv.IIP.Business.Approval.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBusinessApprovalSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "business_approval");

            migrationBuilder.CreateTable(
                name: "approval_chains",
                schema: "business_approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval chain aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the chain."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the chain runs."),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Template id used to create the chain."),
                    template_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Template code copied for historical lookup."),
                    template_version = table.Column<int>(type: "integer", nullable: false, comment: "Template version copied for historical lookup."),
                    source_service = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source business service that owns the document."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source document type."),
                    document_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Source document id supplied by the owning service."),
                    document_line_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional source document line id supplied by the owning service."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Approval chain status: pending, approved, rejected or returned."),
                    started_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public actor reference that started the chain."),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the chain started."),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when the chain reached a terminal result.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_chains", x => x.id);
                },
                comment: "Business approval chain instances for source document references.");

            migrationBuilder.CreateTable(
                name: "approval_templates",
                schema: "business_approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval template aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id that owns the template."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id where the template applies."),
                    template_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Producer-stable template code."),
                    document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Business document type that can use this template."),
                    version = table.Column<int>(type: "integer", nullable: false, comment: "Template version number controlled by BusinessApproval."),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether chains may be started from this template."),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the template was created."),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the template definition was last updated.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_templates", x => x.id);
                },
                comment: "Business approval template facts by document type and environment.");

            migrationBuilder.CreateTable(
                name: "cap_locks",
                schema: "business_approval",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Instance = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    LastLockTime = table.Column<DateTime>(type: "TIMESTAMP", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cap_locks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cap_published_messages",
                schema: "business_approval",
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
                    table.PrimaryKey("PK_cap_published_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cap_received_messages",
                schema: "business_approval",
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
                    table.PrimaryKey("PK_cap_received_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                schema: "business_approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval decision id."),
                    chain_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning approval chain id."),
                    step_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval step id resolved by this decision."),
                    step_no = table.Column<int>(type: "integer", nullable: false, comment: "Approval step number resolved by this decision."),
                    actor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Actor reference type such as user, group or permission."),
                    actor_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public actor reference that made the decision."),
                    decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Decision action: approve, reject or return."),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Optional approver comment."),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the decision was recorded.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_decisions_approval_chains_chain_id",
                        column: x => x.chain_id,
                        principalSchema: "business_approval",
                        principalTable: "approval_chains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Append-only approval decision facts recorded by actor and step.");

            migrationBuilder.CreateTable(
                name: "approval_steps",
                schema: "business_approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval step id."),
                    chain_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning approval chain id."),
                    step_no = table.Column<int>(type: "integer", nullable: false, comment: "Ordered approval step number; equal numbers are resolved as a parallel group."),
                    step_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Step name copied from the template."),
                    parallel_group_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional explicit parallel group key copied from the template."),
                    approver_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Approver reference type copied from the template."),
                    approver_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public approver reference copied from the template."),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Approval step status: pending, approved, rejected or returned."),
                    due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Optional UTC due time for this step."),
                    resolved_by_actor_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Actor type that resolved this step."),
                    resolved_by_actor_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Actor reference that resolved this step."),
                    resolved_decision = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, comment: "Decision action that resolved this step."),
                    resolved_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true, comment: "Optional approver comment captured with the decision."),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC time when this step was resolved.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_steps_approval_chains_chain_id",
                        column: x => x.chain_id,
                        principalSchema: "business_approval",
                        principalTable: "approval_chains",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Runtime approval steps copied from the active template when a chain starts.");

            migrationBuilder.CreateTable(
                name: "approval_template_steps",
                schema: "business_approval",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Approval template step id."),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Owning approval template id."),
                    step_no = table.Column<int>(type: "integer", nullable: false, comment: "Ordered approval step number; equal numbers form an explicit parallel group."),
                    step_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Human-readable step name."),
                    parallel_group_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Optional explicit parallel group key for steps at the same step number."),
                    approver_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Approver reference type such as user, group or permission."),
                    approver_ref = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Public approver reference; IAM facts are not copied."),
                    due_in_hours = table.Column<int>(type: "integer", nullable: true, comment: "Optional due interval in hours after chain start.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_template_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_template_steps_approval_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "business_approval",
                        principalTable: "approval_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Ordered approver definitions owned by an approval template.");

            migrationBuilder.CreateIndex(
                name: "IX_approval_chains_organization_id_environment_id_template_cod~",
                schema: "business_approval",
                table: "approval_chains",
                columns: new[] { "organization_id", "environment_id", "template_code", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_chain_id_step_no_actor_type_actor_ref",
                schema: "business_approval",
                table: "approval_decisions",
                columns: new[] { "chain_id", "step_no", "actor_type", "actor_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_steps_approver_type_approver_ref_status_due_at_utc",
                schema: "business_approval",
                table: "approval_steps",
                columns: new[] { "approver_type", "approver_ref", "status", "due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_approval_steps_chain_id_step_no_approver_type_approver_ref",
                schema: "business_approval",
                table: "approval_steps",
                columns: new[] { "chain_id", "step_no", "approver_type", "approver_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_template_steps_template_id_step_no_approver_type_a~",
                schema: "business_approval",
                table: "approval_template_steps",
                columns: new[] { "template_id", "step_no", "approver_type", "approver_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_templates_organization_id_environment_id_template_~",
                schema: "business_approval",
                table: "approval_templates",
                columns: new[] { "organization_id", "environment_id", "template_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName",
                schema: "business_approval",
                table: "cap_published_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName",
                schema: "business_approval",
                table: "cap_published_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiresAt_StatusName1",
                schema: "business_approval",
                table: "cap_received_messages",
                columns: new[] { "ExpiresAt", "StatusName" });

            migrationBuilder.CreateIndex(
                name: "IX_Version_ExpiresAt_StatusName1",
                schema: "business_approval",
                table: "cap_received_messages",
                columns: new[] { "Version", "ExpiresAt", "StatusName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_decisions",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "approval_steps",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "approval_template_steps",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "cap_locks",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "cap_published_messages",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "cap_received_messages",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "approval_chains",
                schema: "business_approval");

            migrationBuilder.DropTable(
                name: "approval_templates",
                schema: "business_approval");
        }
    }
}
