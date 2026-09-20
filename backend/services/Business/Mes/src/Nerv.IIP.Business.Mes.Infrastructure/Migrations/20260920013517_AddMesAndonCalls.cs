using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Mes.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMesAndonCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "andon_calls",
                schema: "mes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Andon call aggregate id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization owning the call."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment owning the call."),
                    raise_intent_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Creation intent identity unique within organization and environment; retained after closure."),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, comment: "MaterialShortage, Equipment, Quality or Process call category."),
                    work_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source MES work order business id frozen when raised."),
                    operation_task_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source MES operation task business id frozen when raised."),
                    work_center_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Source work center public id frozen when raised."),
                    caller_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "IAM principal id that raised the call."),
                    raised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC instant when the call was raised."),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, comment: "Open, Claimed or Closed lifecycle; escalation does not change it."),
                    responder_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "First claimant IAM principal id; only this person may close the call."),
                    claim_intent_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Accepted claim intent retained for replay after closure."),
                    first_responded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "First successful claim UTC instant; null means no response yet, not zero duration."),
                    close_intent_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Accepted close intent retained for replay."),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC instant when the responder closed the call."),
                    escalated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Single unclaimed-timeout escalation UTC instant, independent of lifecycle."),
                    escalation_recipient_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, comment: "Configured escalation recipient IAM principal id frozen at escalation."),
                    row_version = table.Column<int>(type: "integer", nullable: false, comment: "Optimistic row version protecting lifecycle and escalation writes.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_andon_calls", x => x.id);
                },
                comment: "MES exception calls with immutable first response and independent single escalation facts.");

            migrationBuilder.CreateIndex(
                name: "IX_andon_calls_organization_id_environment_id_category_status_~",
                schema: "mes",
                table: "andon_calls",
                columns: new[] { "organization_id", "environment_id", "category", "status", "escalated_at_utc", "raised_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_andon_calls_organization_id_environment_id_raise_intent_key",
                schema: "mes",
                table: "andon_calls",
                columns: new[] { "organization_id", "environment_id", "raise_intent_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "andon_calls",
                schema: "mes");
        }
    }
}
