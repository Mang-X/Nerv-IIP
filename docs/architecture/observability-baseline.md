# Observability 观测与日志索引基线

本文档定义 Nerv-IIP 的 Observability 事实归属、索引边界和后续建表前置条件。它承接 `docs/architecture/deployment-baseline.md` 中的日志持久化方案，但不冻结具体第三方日志平台。

## 定位

Observability 负责让平台运行状态可诊断、可关联、可导出。它不拥有业务事实，也不替代 Ops 审计或 Notification 通知。

## 事实归属

Observability 拥有：

1. 日志 chunk 索引，例如 `LogChunk`。
2. 可选日志条目索引，例如 `LogEntryIndex`。
3. 诊断包、日志包、采集任务和归档任务的元数据。
4. trace、correlation、operationTask、instance 等上下文索引。
5. 观测数据 retention、清理记录和归档结果。

Observability 不拥有：

1. 运维任务、动作结果和审计记录，这些归 Ops。
2. 用户、角色、组织、环境和授权事实，这些归 IAM。
3. 通知意图、站内消息、投递尝试和已读状态，这些归 Notification。
4. 文件对象和下载授权，这些归 FileStorage；Observability 只保存 `fileId` 或 `FileReference`。
5. 原始业务日志正文的长期关系表存储；日志正文在滚动文件和 File Storage chunk 中。

## 默认存储模型

1. 热日志写入服务本机滚动 JSONL 文件。
2. OpenTelemetry Collector 接收日志、trace 和 metric，可启用本地 `file_storage` 作为发送队列。
3. Log Archive Worker 把关闭后的 JSONL 文件压缩成 `.jsonl.gz` chunk，上传 File Storage。
4. `observability` schema 只保存 chunk 和可选 entry 索引，不保存完整原始日志正文。
5. PlatformGateway 查询日志时先查索引，再通过 File Storage 读取 chunk，并对返回结果做鉴权、分页、脱敏和限流。

## 运行时 telemetry 路由

1. 本地 `.\nerv.ps1 dev` 默认由 Aspire AppHost 注入 Dashboard OTLP endpoint，服务不应手工覆盖 `OTEL_EXPORTER_OTLP_ENDPOINT` 到自建 Collector。这样 Aspire Dashboard 的 Structured logs、Traces 和 Metrics 页面能直接看到 AppHost 内资源的 telemetry。
2. AppHost 内的 OpenTelemetry Collector 是显式测试路径，只在 `Observability:UseCollector=true` 时启用。该路径用于验证 Collector/Compose-like 转发，不是普通本地开发默认值。
3. Compose、PoC 和生产路径以 OpenTelemetry Collector 作为采集入口。Collector 可以通过 `NERV_IIP_ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT` 把数据转发到 standalone Aspire Dashboard 的 OTLP/HTTP endpoint，用于短期诊断。
4. standalone Aspire Dashboard 只保存内存态 telemetry，适合开发、联调、PoC 和现场短期排障。生产长期日志、trace 检索、审计保留和导出必须走滚动 JSONL、Log Archive Worker、File Storage chunk、`observability` 索引或客户已有观测平台。
5. CLI 校验优先使用 `aspire otel logs` 和 `aspire otel traces`，不要只看资源状态判断 telemetry 是否进入 Dashboard。

## 计划表族

后续真正建表前，必须把本节扩展为和 AppHub/Ops 同粒度的 schema catalog。

| Table | Build now? | Purpose | Notes |
| --- | --- | --- | --- |
| `log_chunks` | No | 一个压缩日志块的索引，记录 fileId、服务、环境、实例、时间范围、行数、大小、hash、level 摘要和保留到期时间。 | 首批必须表。 |
| `log_entry_indexes` | No | 可选细粒度索引，记录 chunk 内特定日志行的 timestamp、level、operationTaskId、correlationId、traceId、lineNumber 或 byteOffset。 | 只有查询性能需要时启用。 |
| `diagnostic_packages` | No | 一次诊断导出包的元数据，引用 File Storage 中的归档文件。 | 用于现场排障和交付支持。 |
| `archive_jobs` | No | 日志归档任务执行记录。 | 记录成功、失败、重试和耗时。 |
| `retention_runs` | No | retention 清理执行记录。 | 证明索引和 File Storage chunk 同步清理。 |

## 索引与查询

1. `log_chunks` 默认按时间和服务查询，常用索引为 `(serviceName, fromUtc, toUtc)`、`(instanceKey, fromUtc)`、`(operationTaskId, fromUtc)`、`(correlationId, fromUtc)`、`(traceId, fromUtc)`。
2. 大量追加场景可在 PostgreSQL profile 下使用分区或 BRIN，但这些优化不进入跨 profile 最小模型。
3. `log_entry_indexes` 启用前必须证明 chunk 扫描已经成为瓶颈；否则先保留粗粒度索引。
4. Gateway API 不暴露内部表名、File Storage object key、外部日志平台 URL 或查询语言。

## Retention

1. 每个部署 profile 必须声明日志正文 chunk、索引、诊断包和审计记录各自的保留期。
2. 删除日志 chunk 时必须同步删除 `observability` 索引。
3. 删除索引失败或删除 File Storage 对象失败都必须进入可重试状态。
4. 审计记录不跟随日志 retention 自动删除。

## 外部观测平台

1. 客户已有 Grafana、Loki、Elastic、OpenSearch、ClickHouse 或托管日志平台时，可以通过 Gateway adapter 接入。
2. 外部平台可以替代或弱化内置 `observability` 索引，但前端 OpenAPI 契约不变。
3. 外部平台凭据、查询语言和 endpoint 不暴露给前端或 SDK。
4. 第三方平台选型需要独立 ADR，说明授权、部署、保留期、成本和退出路径。

## 建表前置条件

Observability 首次 migration 前必须补齐：

1. `observability` schema catalog 表级条目。
2. `LogChunk` 和可选 `LogEntryIndex` 的字段、单位、索引意图和 retention 语义。
3. File Storage 引用规则，确保只保存 `fileId` 或 `FileReference`。
4. Gateway 日志查询 OpenAPI 的稳定 DTO 和分页游标。
5. 清理任务、归档任务和失败重试规则。
6. PostgreSQL profile 迁移测试；GaussDB/DMDB 仍按候选 profile 单独验证。

## 前端边界

1. 前端只通过 PlatformGateway 查询日志，不直连 Aspire Dashboard、Collector、File Storage、数据库或第三方观测后端。
2. Design System 未冻结前，不因为 Observability 建表或日志索引规划新增日志页面。
3. 后端 OpenAPI 变化只允许机械生成 api-client 和跑质量门禁，不能扩大成视觉实现。
