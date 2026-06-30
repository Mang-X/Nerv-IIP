# Quality inspection duplicate remediation runbook

This runbook is the operator path for migration
`20260629074947_AddQualityLongtailReviewFixes`, which adds the unique
`quality.inspection_records` idempotency scope:

`organization_id + environment_id + source_type + source_service + source_document_id + sku_code`.

The migration precheck fails before creating the unique index when historical
duplicates exist. It intentionally does not merge, delete, or rewrite business
facts automatically.

## Guardrails

1. Take and verify a database backup before any data change.
2. Run the report query first and attach the result to the release ticket.
3. Do not delete or rewrite CAP outbox/inbox rows, dead letters, processed event
   inbox rows, or external audit evidence.
4. Do not silently delete `inspection_records` or `inspection_result_lines`.
   Any deletion must name the canonical inspection record, the removed duplicate
   ids, the reason, the approver, and the backup evidence.
5. If duplicate records have different quantities, results, stock release
   dimensions, result-line signatures, or NCR references, stop the release and
   open a business-approved data-fix task. Do not apply the unique index until
   that task leaves zero duplicate groups.

## Duplicate report

Run this query on the target Quality PostgreSQL database before applying the
migration:

```sql
WITH duplicate_groups AS (
    SELECT
        organization_id,
        environment_id,
        source_type,
        source_service,
        source_document_id,
        sku_code,
        count(*) AS duplicate_count
    FROM quality.inspection_records
    GROUP BY
        organization_id,
        environment_id,
        source_type,
        source_service,
        source_document_id,
        sku_code
    HAVING count(*) > 1
),
line_signatures AS (
    SELECT
        inspection_record_id,
        count(*) AS line_count,
        string_agg(
            concat_ws(
                '|',
                characteristic_code,
                result,
                coalesce(defect_reason, ''),
                coalesce(defect_quantity::text, ''),
                coalesce(measured_value::text, ''),
                coalesce(unit_code, '')
            ),
            ';'
            ORDER BY characteristic_code, id
        ) AS line_signature
    FROM quality.inspection_result_lines
    GROUP BY inspection_record_id
),
ranked_records AS (
    SELECT
        r.id,
        r.organization_id,
        r.environment_id,
        r.source_type,
        r.source_service,
        r.source_document_id,
        r.sku_code,
        r.inspected_quantity,
        r.result,
        r.uom_code,
        r.site_code,
        r.location_code,
        r.source_quality_status,
        r.owner_type,
        r.owner_id,
        r.nonconformance_report_id,
        r.created_at_utc,
        coalesce(ls.line_count, 0) AS line_count,
        coalesce(ls.line_signature, '') AS line_signature,
        count(ncr.id) AS ncr_source_reference_count
    FROM quality.inspection_records r
    JOIN duplicate_groups g
        ON g.organization_id = r.organization_id
        AND g.environment_id = r.environment_id
        AND g.source_type = r.source_type
        AND g.source_service = r.source_service
        AND g.source_document_id = r.source_document_id
        AND g.sku_code = r.sku_code
    LEFT JOIN line_signatures ls
        ON ls.inspection_record_id = r.id
    LEFT JOIN quality.nonconformance_reports ncr
        ON ncr.source_inspection_record_id = r.id
    GROUP BY
        r.id,
        r.organization_id,
        r.environment_id,
        r.source_type,
        r.source_service,
        r.source_document_id,
        r.sku_code,
        r.inspected_quantity,
        r.result,
        r.uom_code,
        r.site_code,
        r.location_code,
        r.source_quality_status,
        r.owner_type,
        r.owner_id,
        r.nonconformance_report_id,
        r.created_at_utc,
        ls.line_count,
        ls.line_signature
),
ranked_with_canonical AS (
    SELECT
        ranked_records.*,
        row_number() OVER (
            PARTITION BY
                organization_id,
                environment_id,
                source_type,
                source_service,
                source_document_id,
                sku_code
            ORDER BY
                CASE
                    WHEN nonconformance_report_id IS NOT NULL OR ncr_source_reference_count > 0 THEN 0
                    ELSE 1
                END,
                created_at_utc,
                id
        ) AS canonical_rank
    FROM ranked_records
)
SELECT *
FROM ranked_with_canonical
ORDER BY
    organization_id,
    environment_id,
    source_type,
    source_service,
    source_document_id,
    sku_code,
    canonical_rank;
```

## Canonical record rule

For each duplicate group, keep one canonical record:

1. Prefer the record referenced by an NCR through
   `inspection_records.nonconformance_report_id` or
   `nonconformance_reports.source_inspection_record_id`.
2. If exactly one record is tied to downstream event/audit evidence, keep that
   record.
3. If no record has downstream references and all business facts match, keep the
   earliest `created_at_utc`; use the smallest `id` only as the tie breaker.
4. If multiple records have different NCRs or different downstream outcomes,
   stop and resolve with Quality, Inventory, and the source-service owner.

## Conflict handling

Treat a duplicate group as conflicting when any of these differ between records:

1. `inspected_quantity`, `result`, `disposition_reason`, or failed quantity.
2. `uom_code`, `site_code`, `location_code`, `source_quality_status`,
   `owner_type`, `owner_id`, batch, or serial.
3. Result-line count or result-line signature.
4. NCR links, NCR disposition state, Inventory movement ids, ERP return ids, or
   MES rework work-order ids.

Conflicting groups require a business-approved data-fix task. The data-fix must
preserve the original duplicate report, explain the canonical choice, and use a
compensating domain operation or explicit reviewed SQL. Do not edit CAP
outbox/inbox, processed event rows, dead letters, or external audit evidence.

## Manual remediation

For a non-conflicting group with no non-canonical NCR or event/audit references:

1. Export the duplicate report rows and all child `inspection_result_lines` for
   the group to release evidence.
2. Record the canonical `inspection_records.id`, duplicate ids to remove,
   approver, reason, backup id, and release id in the change ticket.
3. In a transaction, delete child `inspection_result_lines` for the approved
   non-canonical ids, then delete those non-canonical `inspection_records`.
4. Re-run the duplicate report query and confirm it returns no rows for the
   remediated scope before committing.

For a group where a non-canonical record has an NCR reference or other audit
evidence, do not use the delete-only path. Open a reviewed data-fix task that
decides whether to keep that referenced record as canonical or to explicitly
re-point the Quality-owned NCR reference to the canonical record. Event payloads
and system audit rows remain immutable evidence.

## Migration verification

After remediation, the duplicate group count must be zero:

```sql
SELECT count(*) AS duplicate_group_count
FROM (
    SELECT 1
    FROM quality.inspection_records
    GROUP BY
        organization_id,
        environment_id,
        source_type,
        source_service,
        source_document_id,
        sku_code
    HAVING count(*) > 1
) duplicate_groups;
```

Only after this query returns `0` may the operator apply migration
`20260629074947_AddQualityLongtailReviewFixes`. If the migration still fails,
attach the precheck error and the latest duplicate report to the release ticket
and do not bypass the unique index.
