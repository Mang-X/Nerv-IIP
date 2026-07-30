using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nerv.IIP.Business.Wms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompleteWmsWorkPoolExecutionBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                DECLARE
                    conflicting_task_ids text;
                    invalid_wcs_lifecycle text;
                    legacy record;
                    completion_document jsonb;
                    executed_quantity numeric;
                    quantity_key text;
                BEGIN
                    SELECT string_agg(
                        conflict."warehouse_task_id"::text,
                        ', ' ORDER BY conflict."warehouse_task_id"::text)
                    INTO conflicting_task_ids
                    FROM (
                        SELECT "warehouse_task_id"
                        FROM "wms"."wcs_tasks"
                        GROUP BY "warehouse_task_id"
                        HAVING COUNT(*) > 1
                    ) AS conflict;

                    IF conflicting_task_ids IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            MESSAGE = format(
                                'WMS work-assignment migration blocked: legacy wcs_tasks contains multiple adapter rows for warehouse_task_id(s): %s.',
                                conflicting_task_ids),
                            HINT = 'Resolve each conflict to one auditable WCS record before retrying the migration.';
                    END IF;

                    FOR legacy IN
                        SELECT
                            wcs."id" AS wcs_task_id,
                            wcs."status" AS wcs_status,
                            wcs."organization_id" AS wcs_organization_id,
                            wcs."environment_id" AS wcs_environment_id,
                            wcs."dispatched_at_utc",
                            wcs."completed_at_utc" AS wcs_completed_at_utc,
                            wcs."completion_payload_json",
                            task."id" AS warehouse_task_id,
                            task."status" AS warehouse_task_status,
                            task."organization_id" AS task_organization_id,
                            task."environment_id" AS task_environment_id,
                            task."planned_quantity",
                            task."executed_quantity",
                            task."completed_at_utc" AS task_completed_at_utc
                        FROM "wms"."wcs_tasks" AS wcs
                        INNER JOIN "wms"."warehouse_tasks" AS task
                            ON task."id" = wcs."warehouse_task_id"
                    LOOP
                        IF legacy.wcs_status NOT IN ('Dispatched', 'Completed', 'Failed', 'Cancelled') THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format('%s: unsupported WCS status %s', legacy.wcs_task_id, legacy.wcs_status));
                            CONTINUE;
                        END IF;

                        IF legacy.wcs_organization_id <> legacy.task_organization_id
                            OR legacy.wcs_environment_id <> legacy.task_environment_id THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format('%s: WCS and warehouse task tenant mismatch', legacy.wcs_task_id));
                            CONTINUE;
                        END IF;

                        IF legacy.planned_quantity <= 0
                            OR legacy.executed_quantity < 0
                            OR legacy.executed_quantity > legacy.planned_quantity THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format('%s: warehouse task quantity is outside the legal range', legacy.wcs_task_id));
                            CONTINUE;
                        END IF;

                        IF legacy.wcs_status IN ('Dispatched', 'Failed') THEN
                            IF legacy.warehouse_task_status NOT IN ('Open', 'InProgress')
                                OR legacy.executed_quantity >= legacy.planned_quantity
                                OR legacy.task_completed_at_utc IS NOT NULL THEN
                                invalid_wcs_lifecycle := concat_ws(
                                    ', ',
                                    invalid_wcs_lifecycle,
                                    format(
                                        '%s: active or retryable WCS task conflicts with parent status %s',
                                        legacy.wcs_task_id,
                                        legacy.warehouse_task_status));
                            END IF;
                            CONTINUE;
                        END IF;

                        IF legacy.wcs_status = 'Cancelled' THEN
                            IF legacy.warehouse_task_status NOT IN ('Open', 'InProgress', 'Cancelled')
                                OR legacy.executed_quantity >= legacy.planned_quantity
                                OR legacy.task_completed_at_utc IS NOT NULL THEN
                                invalid_wcs_lifecycle := concat_ws(
                                    ', ',
                                    invalid_wcs_lifecycle,
                                    format(
                                        '%s: cancelled WCS task conflicts with parent status %s',
                                        legacy.wcs_task_id,
                                        legacy.warehouse_task_status));
                            END IF;
                            CONTINUE;
                        END IF;

                        IF legacy.warehouse_task_status NOT IN ('Open', 'InProgress', 'Completed')
                            OR legacy.wcs_completed_at_utc IS NULL THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format(
                                    '%s: completed WCS task conflicts with parent status %s or lacks completion time',
                                    legacy.wcs_task_id,
                                    legacy.warehouse_task_status));
                            CONTINUE;
                        END IF;

                        completion_document := NULL;
                        executed_quantity := NULL;
                        quantity_key := NULL;
                        BEGIN
                            completion_document := legacy.completion_payload_json::jsonb;
                        EXCEPTION WHEN others THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format('%s: completion payload is not valid JSON', legacy.wcs_task_id));
                            CONTINUE;
                        END;

                        IF jsonb_typeof(completion_document) <> 'object' THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format('%s: completion payload is not an object', legacy.wcs_task_id));
                            CONTINUE;
                        END IF;

                        quantity_key := CASE
                            WHEN completion_document ? 'actualQuantity' THEN 'actualQuantity'
                            WHEN completion_document ? 'executedQuantity' THEN 'executedQuantity'
                            ELSE NULL
                        END;
                        IF quantity_key IS NULL
                            OR jsonb_typeof(completion_document -> quantity_key) <> 'number' THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format(
                                    '%s: completion payload lacks an authoritative numeric executed quantity',
                                    legacy.wcs_task_id));
                            CONTINUE;
                        END IF;

                        executed_quantity := (completion_document ->> quantity_key)::numeric;
                        IF completion_document ? 'actualQuantity'
                            AND completion_document ? 'executedQuantity' THEN
                            IF jsonb_typeof(completion_document -> 'actualQuantity') <> 'number'
                                OR jsonb_typeof(completion_document -> 'executedQuantity') <> 'number' THEN
                                invalid_wcs_lifecycle := concat_ws(
                                    ', ',
                                    invalid_wcs_lifecycle,
                                    format('%s: completion quantity fields are not numeric', legacy.wcs_task_id));
                                CONTINUE;
                            END IF;

                            IF (completion_document ->> 'actualQuantity')::numeric
                                <> (completion_document ->> 'executedQuantity')::numeric THEN
                                invalid_wcs_lifecycle := concat_ws(
                                    ', ',
                                    invalid_wcs_lifecycle,
                                    format('%s: completion quantity fields disagree', legacy.wcs_task_id));
                                CONTINUE;
                            END IF;
                        END IF;

                        IF executed_quantity <> legacy.planned_quantity THEN
                            invalid_wcs_lifecycle := concat_ws(
                                ', ',
                                invalid_wcs_lifecycle,
                                format(
                                    '%s: completed WCS quantity %s does not close planned quantity %s',
                                    legacy.wcs_task_id,
                                    executed_quantity,
                                    legacy.planned_quantity));
                        END IF;
                    END LOOP;

                    IF invalid_wcs_lifecycle IS NOT NULL THEN
                        RAISE EXCEPTION USING
                            MESSAGE = format(
                                'WMS work-assignment migration blocked: legacy WCS/task lifecycle cannot be reconciled: %s.',
                                invalid_wcs_lifecycle),
                            HINT = 'Repair each legacy Completed WCS payload to contain an authoritative full executed quantity and reconcile its parent lifecycle, or remove the invalid WCS row with an audit record before retrying. Missing, invalid, or partial quantities must not be guessed.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_wcs_tasks_warehouse_task_id_adapter_type",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the task is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_pool_code",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional WMS work-pool assignment snapshot captured when the task is created.");

            migrationBuilder.AddColumn<string>(
                name: "completed_by",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Operator or system actor that completed the task.");

            migrationBuilder.AddColumn<string>(
                name: "completion_reason",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Audited reason for completion, required for a picking difference.");

            migrationBuilder.AddColumn<DateTime>(
                name: "exception_at_utc",
                schema: "wms",
                table: "warehouse_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the terminal exception was reported.");

            migrationBuilder.AddColumn<string>(
                name: "exception_by",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Operator user id that reported the terminal exception.");

            migrationBuilder.AddColumn<string>(
                name: "exception_code",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Operator-reported exception code.");

            migrationBuilder.AddColumn<string>(
                name: "exception_reason",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                comment: "Operator-reported exception reason.");

            migrationBuilder.AddColumn<string>(
                name: "execution_channel",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                comment: "Atomic execution ownership channel: legacy-unclaimed, unclaimed, manual or WCS.");

            migrationBuilder.AddColumn<DateTime>(
                name: "execution_claimed_at_utc",
                schema: "wms",
                table: "warehouse_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when the execution channel was atomically claimed.");

            migrationBuilder.AddColumn<string>(
                name: "execution_claimed_by",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Trusted operator principal id or WCS task claim reference.");

            migrationBuilder.Sql(
                """
                UPDATE "wms"."warehouse_tasks" AS task
                SET "execution_channel" = 'Wcs',
                    "execution_claimed_by" = wcs."id"::text,
                    "execution_claimed_at_utc" = wcs."dispatched_at_utc"
                FROM "wms"."wcs_tasks" AS wcs
                WHERE wcs."warehouse_task_id" = task."id"
                  AND task."execution_channel" IS NULL;
                """);

            migrationBuilder.Sql(
                """UPDATE "wms"."warehouse_tasks" SET "execution_channel" = 'LegacyUnclaimed' WHERE "execution_channel" IS NULL;""");

            migrationBuilder.AlterColumn<string>(
                name: "execution_channel",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                comment: "Atomic execution ownership channel: legacy-unclaimed, unclaimed, manual or WCS.",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true,
                oldComment: "Atomic execution ownership channel: legacy-unclaimed, unclaimed, manual or WCS.");

            migrationBuilder.AddColumn<string>(
                name: "lot_no",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional source lot number copied from the execution order line.");

            migrationBuilder.AddColumn<string>(
                name: "serial_no",
                schema: "wms",
                table: "warehouse_tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                comment: "Optional source serial number copied from the execution order line.");

            migrationBuilder.AddColumn<DateTime>(
                name: "started_at_utc",
                schema: "wms",
                table: "warehouse_tasks",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC time when manual or WCS execution started.");

            migrationBuilder.Sql(
                """
                UPDATE "wms"."warehouse_tasks" AS task
                SET "status" = CASE
                        WHEN wcs."status" = 'Completed' THEN 'Completed'
                        WHEN wcs."status" = 'Cancelled' THEN 'Cancelled'
                        ELSE 'InProgress'
                    END,
                    "started_at_utc" = wcs."dispatched_at_utc",
                    "executed_quantity" = CASE
                        WHEN wcs."status" = 'Completed' THEN
                            CASE
                                WHEN wcs."completion_payload_json"::jsonb ? 'actualQuantity'
                                    THEN (wcs."completion_payload_json"::jsonb ->> 'actualQuantity')::numeric
                                ELSE (wcs."completion_payload_json"::jsonb ->> 'executedQuantity')::numeric
                            END
                        ELSE task."executed_quantity"
                    END,
                    "completed_at_utc" = CASE
                        WHEN wcs."status" = 'Completed' THEN wcs."completed_at_utc"
                        ELSE NULL
                    END,
                    "completed_by" = CASE
                        WHEN wcs."status" = 'Completed' THEN 'system:wcs:' || wcs."id"::text
                        ELSE NULL
                    END
                FROM "wms"."wcs_tasks" AS wcs
                WHERE wcs."warehouse_task_id" = task."id";
                """);

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "wms",
                table: "warehouse_tasks",
                type: "bigint",
                nullable: true,
                comment: "Optimistic concurrency token advanced for every successful task mutation.");

            migrationBuilder.Sql(
                """UPDATE "wms"."warehouse_tasks" SET "version" = 1 WHERE "version" IS NULL;""");

            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "wms",
                table: "warehouse_tasks",
                type: "bigint",
                nullable: false,
                comment: "Optimistic concurrency token advanced for every successful task mutation.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Optimistic concurrency token advanced for every successful task mutation.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "outbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the outbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_pool_code",
                schema: "wms",
                table: "outbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional WMS work-pool assignment snapshot captured when the outbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the inbound order is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_pool_code",
                schema: "wms",
                table: "inbound_orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional WMS work-pool assignment snapshot captured when the inbound order is created.");

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "wms",
                table: "inbound_orders",
                type: "bigint",
                nullable: true,
                comment: "Optimistic concurrency token advanced for inbound assignment and lifecycle mutations.");

            migrationBuilder.Sql(
                """UPDATE "wms"."inbound_orders" SET "version" = 1 WHERE "version" IS NULL;""");

            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "wms",
                table: "inbound_orders",
                type: "bigint",
                nullable: false,
                comment: "Optimistic concurrency token advanced for inbound assignment and lifecycle mutations.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Optimistic concurrency token advanced for inbound assignment and lifecycle mutations.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "count_executions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional operator assignment snapshot captured when the count execution is created.");

            migrationBuilder.AddColumn<string>(
                name: "assigned_pool_code",
                schema: "wms",
                table: "count_executions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true,
                comment: "Optional WMS work-pool assignment snapshot captured when the count execution is created.");

            migrationBuilder.AddColumn<long>(
                name: "version",
                schema: "wms",
                table: "count_executions",
                type: "bigint",
                nullable: true,
                comment: "Optimistic concurrency token advanced for count assignment and lifecycle mutations.");

            migrationBuilder.Sql(
                """UPDATE "wms"."count_executions" SET "version" = 1 WHERE "version" IS NULL;""");

            migrationBuilder.AlterColumn<long>(
                name: "version",
                schema: "wms",
                table: "count_executions",
                type: "bigint",
                nullable: false,
                comment: "Optimistic concurrency token advanced for count assignment and lifecycle mutations.",
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true,
                oldComment: "Optimistic concurrency token advanced for count assignment and lifecycle mutations.");

            migrationBuilder.CreateTable(
                name: "warehouse_assignment_receipts",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Assignment receipt id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    resource_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Controlled assignment category."),
                    resource_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Assigned aggregate id."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Stable assignment intent key."),
                    payload_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Canonical assignment payload fingerprint."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Authorized exact site snapshot."),
                    pool_code = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Assigned WMS work-pool snapshot."),
                    operator_principal_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, comment: "Optional assigned operator principal snapshot."),
                    assigned_by_principal_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Trusted assigning principal snapshot."),
                    result_version = table.Column<long>(type: "bigint", nullable: false, comment: "Authoritative aggregate version after assignment."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC receipt creation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_assignment_receipts", x => x.id);
                },
                comment: "Durable idempotency receipts for controlled WMS assignment and reassignment.");

            migrationBuilder.CreateTable(
                name: "warehouse_task_action_receipts",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse task action receipt id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    warehouse_task_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse task targeted by the manual action."),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Stable manual action name."),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Caller-provided idempotency key scoped to the task and action."),
                    payload_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Canonical request payload fingerprint used to reject key reuse with different content."),
                    result_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, comment: "Task status returned by the first successful execution."),
                    result_version = table.Column<long>(type: "bigint", nullable: false, comment: "Task version returned by the first successful execution."),
                    result_executed_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Executed quantity returned by the first successful execution."),
                    result_difference_quantity = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false, comment: "Absolute planned-versus-executed difference returned by the first successful execution."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC time when the durable receipt was created.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_task_action_receipts", x => x.id);
                },
                comment: "Durable idempotency receipts for manual warehouse task actions.");

            migrationBuilder.CreateTable(
                name: "warehouse_work_pool_memberships",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse work-pool membership id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    pool_code = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Owning WMS work-pool code."),
                    principal_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Trusted IAM principal id qualified for the pool."),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the qualification remains active."),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "Inclusive UTC qualification start."),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "Exclusive UTC qualification end."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    deactivated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC deactivation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_work_pool_memberships", x => x.id);
                },
                comment: "Effective-dated WMS work-pool qualifications for trusted IAM principal ids; memberships grant no permission.");

            migrationBuilder.CreateTable(
                name: "warehouse_work_pools",
                schema: "wms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Warehouse work-pool id."),
                    organization_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Organization tenant id."),
                    environment_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "Environment id."),
                    pool_code = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false, comment: "Stable WMS work-pool code."),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, comment: "Operator-facing work-pool name."),
                    site_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, comment: "MasterData site code that owns the work pool."),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the work pool accepts current assignments."),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, comment: "UTC creation time."),
                    deactivated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true, comment: "UTC deactivation time.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warehouse_work_pools", x => x.id);
                },
                comment: "WMS-owned operational work pools; these are not MasterData teams and grant no IAM permission.");

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_warehouse_task_id",
                schema: "wms",
                table: "wcs_tasks",
                column: "warehouse_task_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_tasks_operator_scope",
                schema: "wms",
                table: "warehouse_tasks",
                columns: new[] { "organization_id", "environment_id", "task_type", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_tasks_pool_scope",
                schema: "wms",
                table: "warehouse_tasks",
                columns: new[] { "organization_id", "environment_id", "task_type", "status", "site_code", "assigned_pool_code", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_operator_scope",
                schema: "wms",
                table: "outbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbound_orders_pool_scope",
                schema: "wms",
                table: "outbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_pool_code", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_orders_operator_scope",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inbound_orders_pool_scope",
                schema: "wms",
                table: "inbound_orders",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_pool_code", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_count_executions_operator_scope",
                schema: "wms",
                table: "count_executions",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_operator_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_count_executions_pool_scope",
                schema: "wms",
                table: "count_executions",
                columns: new[] { "organization_id", "environment_id", "status", "site_code", "assigned_pool_code", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_assignment_receipts_key",
                schema: "wms",
                table: "warehouse_assignment_receipts",
                columns: new[] { "organization_id", "environment_id", "resource_category", "resource_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_task_action_receipts_task",
                schema: "wms",
                table: "warehouse_task_action_receipts",
                columns: new[] { "organization_id", "environment_id", "warehouse_task_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_task_action_receipts_key",
                schema: "wms",
                table: "warehouse_task_action_receipts",
                columns: new[] { "organization_id", "environment_id", "warehouse_task_id", "action", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_work_pool_memberships_principal_effective",
                schema: "wms",
                table: "warehouse_work_pool_memberships",
                columns: new[] { "organization_id", "environment_id", "principal_id", "active", "effective_from_utc", "effective_to_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_work_pool_memberships_window",
                schema: "wms",
                table: "warehouse_work_pool_memberships",
                columns: new[] { "organization_id", "environment_id", "pool_code", "principal_id", "effective_from_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouse_work_pools_site_active",
                schema: "wms",
                table: "warehouse_work_pools",
                columns: new[] { "organization_id", "environment_id", "site_code", "active" });

            migrationBuilder.CreateIndex(
                name: "ux_warehouse_work_pools_code",
                schema: "wms",
                table: "warehouse_work_pools",
                columns: new[] { "organization_id", "environment_id", "pool_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "warehouse_assignment_receipts",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "warehouse_task_action_receipts",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "warehouse_work_pool_memberships",
                schema: "wms");

            migrationBuilder.DropTable(
                name: "warehouse_work_pools",
                schema: "wms");

            migrationBuilder.DropIndex(
                name: "IX_wcs_tasks_warehouse_task_id",
                schema: "wms",
                table: "wcs_tasks");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_tasks_operator_scope",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropIndex(
                name: "ix_warehouse_tasks_pool_scope",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropIndex(
                name: "ix_outbound_orders_operator_scope",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_outbound_orders_pool_scope",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_inbound_orders_operator_scope",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_inbound_orders_pool_scope",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropIndex(
                name: "ix_count_executions_operator_scope",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropIndex(
                name: "ix_count_executions_pool_scope",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "assigned_pool_code",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "completed_by",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "completion_reason",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_at_utc",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_by",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_code",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "exception_reason",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "execution_channel",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "execution_claimed_at_utc",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "execution_claimed_by",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "lot_no",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "serial_no",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "started_at_utc",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "wms",
                table: "warehouse_tasks");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_pool_code",
                schema: "wms",
                table: "outbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_pool_code",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "wms",
                table: "inbound_orders");

            migrationBuilder.DropColumn(
                name: "assigned_operator_user_id",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropColumn(
                name: "assigned_pool_code",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "wms",
                table: "count_executions");

            migrationBuilder.CreateIndex(
                name: "IX_wcs_tasks_warehouse_task_id_adapter_type",
                schema: "wms",
                table: "wcs_tasks",
                columns: new[] { "warehouse_task_id", "adapter_type" },
                unique: true);
        }
    }
}
