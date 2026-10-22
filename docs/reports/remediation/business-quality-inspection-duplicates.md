# 质量检验重复项处置运行手册

本运行手册说明迁移
`20260629074947_AddQualityLongtailReviewFixes` 的操作路径；该迁移新增唯一的
`quality.inspection_records` 幂等范围：

`organization_id + environment_id + source_type + source_service + source_document_id + sku_code`.

存在历史重复项时，迁移预检查会在创建唯一索引前失败。该预检查有意不自动合并、删除或改写业务
事实。

## 护栏

1. 在进行任何数据变更前，必须创建并验证数据库备份。
2. 必须先运行报告查询，并将结果附加到发布工单。
3. 不得删除或改写 CAP 发件箱/收件箱行、死信、已处理事件收件箱行或外部审计证据。
4. 不得静默删除 `inspection_records` 或 `inspection_result_lines`。任何删除都必须注明规范检验记录、
   被移除重复项的 ID、原因、批准人和备份证据。
5. 如果重复记录的数量、结果、库存放行维度、结果行签名或 NCR 引用不同，必须停止发布并创建经业务
   批准的数据修复任务。在该任务将重复组清零前，不得应用唯一索引。

## 重复项报告

在应用迁移前，必须在目标 Quality PostgreSQL 数据库上运行此查询：

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
        r.disposition_reason,
        r.batch_no,
        r.serial_no,
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
        r.disposition_reason,
        r.batch_no,
        r.serial_no,
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

## 规范记录规则

每个重复组保留一条规范记录：

报告中的 `canonical_rank` 仅是基于 Quality 本地 NCR 引用、`created_at_utc` 和 `id` 的初始排序。
它无法检测 CAP 消息、外部事件载荷、工单附件或其他审计证据。在将已排序行视为规范记录或删除任何
非规范行之前，必须检查这些引用。

1. 应优先保留由 NCR 通过
   `inspection_records.nonconformance_report_id` 或
   `nonconformance_reports.source_inspection_record_id` 引用的记录。
2. 如果恰有一条记录关联下游事件/审计证据，必须保留该记录。
3. 如果没有记录具有下游引用且所有业务事实一致，必须保留最早的 `created_at_utc`；仅在出现并列时使用
   最小的 `id` 作为决胜条件。
4. 如果多条记录具有不同 NCR 或不同下游结果，必须停止并与 Quality、Inventory 和来源服务负责人共同
   解决。

## 冲突处理

当记录之间的以下任一项不同，必须将重复组视为存在冲突：

1. `inspected_quantity`、`result`、`disposition_reason` 或结果行
   `defect_quantity`。
2. `uom_code`、`site_code`、`location_code`、`source_quality_status`、
   `owner_type`、`owner_id`、`batch_no` 或 `serial_no`。
3. 结果行数量或结果行签名。
4. NCR 链接、NCR 处置状态、Inventory 移动 ID、ERP 退货 ID 或 MES 返工工单 ID。

存在冲突的重复组必须使用经业务批准的数据修复任务。数据修复必须保留原始重复项报告、说明规范记录
选择，并使用补偿性领域操作或经明确审核的 SQL。不得编辑 CAP 发件箱/收件箱、已处理事件行、死信或
外部审计证据。

## 人工处置

对于不存在冲突且非规范记录没有 NCR 或事件/审计引用的重复组：

1. 将重复项报告行以及该组的所有子 `inspection_result_lines` 导出为发布证据。
2. 在变更工单中记录规范 `inspection_records.id`、待移除的重复项 ID、批准人、原因、备份 ID 和发布 ID。
3. 在事务中，先删除已批准非规范 ID 的子 `inspection_result_lines`，再删除这些非规范
   `inspection_records`。
4. 在提交前重新运行重复项报告查询，并确认其对已处置范围不返回任何行。

对于非规范记录具有 NCR 引用或其他审计证据的重复组，不得使用仅删除路径。必须创建经审核的数据修复
任务，以决定保留该被引用记录作为规范记录，还是将 Quality 所有的 NCR 引用显式重新指向规范记录。事件
载荷和系统审计行仍是不可变证据。

## 迁移验证

处置后，重复组计数必须为零：

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

只有在此查询返回 `0` 后，操作员才可应用迁移
`20260629074947_AddQualityLongtailReviewFixes`。如果迁移仍然失败，必须将预检查错误和最新重复项报告
附加到发布工单，且不得绕过唯一索引。
