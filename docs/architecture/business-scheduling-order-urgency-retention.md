# 订单紧急度快照保留与恢复

## 范围与策略

BusinessScheduling 负责 `order_urgency_snapshots` 的生命周期策略和审计状态；FileStorage 负责 MinIO 传输。生产基线为在线保留 180 天、总保留 1,095 天。每条策略记录均按 `organizationId + environmentId` 隔离；不支持通配范围。即使订单最新快照早于在线保留截止点，也始终在线保留。

该工作器默认禁用。范围必须被显式启用且配置有效后，才会被纳入处理。生效的法律保全会阻止该范围的源记录和归档删除。源记录删除与精确归档版本删除分别使用独立且有时限的授权记录。授权缺失或过期时，仍允许已验证的归档写入，但绝不允许删除。

```json
{
  "OrderUrgencyRetention": {
    "Enabled": true,
    "IntervalMinutes": 60,
    "Scopes": {
      "org-001-prod": {
        "Enabled": true,
        "OrganizationId": "org-001",
        "EnvironmentId": "prod",
        "OnlineRetentionDays": 180,
        "TotalRetentionDays": 1095,
        "BatchSize": 100,
        "MaxArchiveBytes": 5242879,
        "LegalHoldActive": false,
        "SourceDeletionAuthorization": {
          "Reference": "CAB-1234",
          "Actor": "user:records-manager",
          "Reason": "Approved online-retention enforcement",
          "ApprovedAtUtc": "2026-07-22T00:00:00Z",
          "ExpiresAtUtc": "2026-07-23T00:00:00Z"
        }
      }
    }
  }
}
```

不得保留常设删除授权。应当为受控运行临时注入短时有效的批准，核验其审计记录后将其移除。只有归档版本已超过总保留期且精确版本删除已获批准时，才单独添加 `ArchiveDeletionAuthorization`。

## 归档安全边界

FileStorage 要求配置 `Storage:MinIO:Endpoint`、`AccessKey`、`SecretKey` 和 `ComplianceArchiveBucket`。合规存储桶（bucket）必须预先创建并启用版本控制；使用对象存储法律保全时，还必须具备对象锁定能力。该服务不会创建或降级此存储桶。

对于每个确定性批次，Scheduling 首先在一个数据库事务中持久化 `pending` 批次意图及其有序源成员关系。带索引的 `organization + environment + snapshot` 成员关系无需扫描审计 JSON，即可阻止某个源代际进入另一批次；`batch + sequence` 可重建原始载荷顺序。新的选择之前始终先处理待处理意图和可重试的失败意图，并根据该成员关系及记录的创建时间重建载荷，因此后续对 `BatchSize` 或 `MaxArchiveBytes` 的变更不能重写运行中的批次。FileStorage 使用 S3 `If-None-Match: *` 前置条件写入带范围命名空间的 JSON 信封，取得非空版本 ID，回读该精确版本，并验证 SHA-256 与字节长度。MinIO 7 在其单一 `PutObject` 路径上原子应用该前置条件，但在 5 MiB 时切换为分段上传（multipart），因此共享契约将内容上限设为 5 MiB 减 1 字节。Scheduling 动态缩小配置的行批次以保持在 `MaxArchiveBytes` 之内；FileStorage 在调用对象存储前独立拒绝任何更大的请求。大于配置上限的单个快照会记录 `archive-payload-too-large`，保持在线状态，持久地排除在重复尝试之外，且不会阻止后续符合条件的记录继续处理。条件写入对一个批次键原子地只允许首个写入者，即使远程 I/O 期间租约到期亦然。仅当所存储的 SHA-256 和字节长度与确定性批次载荷匹配时，重试才复用当前对象版本，防止回读或数据库持久化重试创建另一未受管版本。Scheduling 在可删除源记录前持久化对象键、精确版本、哈希、大小和验证时间，并重新验证实时证据返回相同的对象键和版本 ID。在受栅栏保护的源删除事务之前，每个成员必须仍处于当前在线保留窗口之外，且对于同一订单仍存在更新的快照；任何策略延长或更新代际缺失都会保留完整的源批次。策略禁用、范围配置无效、FileStorage/MinIO 不可用、存储桶版本控制禁用、证据不完整、哈希不匹配、生效的法律保全、租约争用或授权缺失时，均安全失败：源快照保持在线。

范围租约和从源记录代际派生的稳定批次标识，使重叠的工作器实例保持幂等。候选排序以不可变快照 ID 结尾，因此 `BatchSize` 边界具有确定性。已完成的 `archived` 批次复用其记录的精确版本证据，而非上传另一个对象版本；恢复的记录获得新 ID，因此形成新批次，而不会回退原始终态批次。批次生命周期转换使用乐观并发修订。每次运行的恢复工作最多处理十个批次，工作器会在远程 I/O 前后以及破坏性转换前检查或续约其十分钟租约。源记录删除、批次转换和租约修订栅栏在一个数据库事务中提交；并发接管会改变该修订，并使整个删除事务回滚。租约丢失会中止运行且保留源记录。失败的批次或已归档但未删除的批次具有持久性，可以重试而不丢失其证据。FileStorage 归档端点和 Scheduling 恢复端点使用内部服务授权，且不是 Business Console 界面。

## 恢复目标

运行恢复目标为自恢复请求获批起 24 小时内完成。恢复过程读取记录的精确对象版本，验证已存储的证据和信封范围，仅重新水合缺失的不可变快照，并为每次尝试（包括幂等重放）追加审计记录。

使用 `POST /api/business/internal/v1/scheduling/order-urgency-archives/restore` 并提供组织、环境、批次 ID 和原因。行为主体从已认证主体或规范转发的内部 `X-Actor` 请求头（header）中解析，不能在请求体中提供。宣告恢复完成前，必须核验相应的 `order_urgency_restore_audits` 记录、恢复数量、精确对象版本和应用读取路径。绝不得以手工复制对象或插入记录替代此路径。

## 指标与告警

- `nerv_iip_order_urgency_retention_runs_total{outcome,organization,environment}`：成功、失败、崩溃、保全中、因租约跳过或因配置被拒绝的运行。
- `nerv_iip_order_urgency_retention_snapshots_total{outcome,organization,environment}`：已归档、已删除源记录和已删除归档的计数。
- `nerv_iip_order_urgency_retention_eligible_snapshots{organization,environment}`：最近观测到的符合条件的积压量。
- `nerv_iip_order_urgency_retention_oldest_eligible_age_seconds{organization,environment}`：最近观测到的最早符合条件记录的时长。
- `nerv_iip_order_urgency_retention_operation_failures_total{error_code,organization,environment}`：针对持久化、栅栏和其他范围运行崩溃的稳定分类。

错误日志是配置被拒绝、归档/证据失败和工作器崩溃的运行告警。当符合条件的记录超过一个配置批次时，发出警告。对于失败/配置被拒绝结果的任何增长、重复崩溃，或在成功间隔内未下降的积压量/最早时长值触发告警。指标携带 `organization` 和 `environment` 标签，使并发配置的范围保留独立的仪表和计数器。这些值仅来自受限的运维人员配置；绝不得从任意请求数据填充，并应实施常规指标访问控制，因为范围名称可能标识租户。

## 迁移与发布

迁移 `20260722150201_AddOrderUrgencyRetentionArchive` 新增归档批次审计、保留租约和恢复审计表，以及范围/时间保留扫描索引。迁移 `20260722154723_HardenOrderUrgencyArchiveLifecycle` 新增归档批次生命周期并发修订。迁移 `20260722164839_AddOrderUrgencyArchiveMembership` 新增有序、范围隔离且带索引的源成员关系。在保留功能仍禁用时应用全部三个迁移。预先配置并验证合规存储桶（bucket），部署 FileStorage 和 Scheduling，在没有删除授权的情况下演练仅归档范围，并检查精确版本证据。随后为一个小批次启用短时有效的源删除授权。仅在了解数据库延迟、符合条件的积压量和对象存储延迟后，才增大批次大小。

回滚时先禁用全部范围并移除删除授权。随后可以回滚代码，同时保留新增的表和证据。归档或审计已存在后，除非记录治理已明确授权销毁该证据，否则不得运行迁移 `Down` 路径。

使用以下命令运行代表性的 PostgreSQL 容量/并发配置档（profile）：

```powershell
pwsh scripts/verify-business-scheduling-urgency-retention.ps1
```

该命令迁移一次性数据库，在 5,001 个订单中植入 10,002 个快照，使同一范围内的两个工作器重叠运行，验证单个取得租约的 1,000 行归档/删除批次，并证明全部 5,001 个最新快照仍然保留。JSON 证据写入 `artifacts/script-logs/business-scheduling-urgency-retention/<run-id>/`，且不会提交。
