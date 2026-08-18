# ADR 0016：VictoriaLogs 中央日志后端

- 状态：已接受
- 日期：2026-06-10

## 背景

Nerv-IIP 已通过 Serilog Console、本地滚动文件和 OpenTelemetry OTLP 输出结构化 JSON 日志。Aspire Dashboard 仍是本地短期诊断界面，但它不是持久化日志存储。平台需要一个仅处理日志的后端，以提供集中存储和受控检索，同时不在本次范围内引入指标或追踪后端。

VictoriaLogs 被选为首条中央日志存储路径的后端。VictoriaMetrics 官方文档说明，VictoriaLogs 采用 Apache License 2.0，支持通过 `/insert/opentelemetry/v1/logs` 接收 OTLP 日志，通过 `/select/logsql/query` 提供 LogsQL 查询，并使用 `-retentionPeriod` 配置保留期。本决策核实的当前 VictoriaLogs 官方版本为 `v1.50.0`。

## 决策

1. 将 VictoriaLogs 作为本地 AppHost 和基于 Compose 部署的默认中央日志专用后端。
2. 将运行时镜像固定为 `victoriametrics/victoria-logs:v1.50.0`；不得使用 `latest`。
3. 通过专用卷配置持久化存储，并显式传入 `-storageDataPath` 和 `-retentionPeriod`。
4. 使用 OTLP/HTTP 日志端点路径 `/insert/opentelemetry/v1/logs`，将服务的 OTLP 日志路由至 VictoriaLogs。
5. 指标和追踪继续沿用现有 OpenTelemetry/Aspire Dashboard 路径。本 ADR 不引入指标或追踪后端。
6. 仅通过 PlatformGateway 门面 API 暴露日志搜索。前端代码不得直接调用 VictoriaLogs、LogsQL、Collector 或 Aspire Dashboard。
7. 提供精简的 `Nerv.IIP.Observability` VictoriaLogs 客户端和安全查询构建器，将平台过滤条件映射为 LogsQL。查询门面支持 service、correlationId、traceId、时间范围和 level 过滤条件。
8. 不得在 AppHub、IAM、Ops、FileStorage、Notification 或业务 PostgreSQL schema 中存储日志消息正文。PostgreSQL 后续可以在独立的 `observability` schema 中保存可观测性索引或元数据，但本次范围将可搜索的日志正文存储在 VictoriaLogs 中。

## 后果

- AppHost 是 VictoriaLogs 容器的拓扑来源。旧版 Compose 文件可为迁移和发布演练包含相同服务，但不得成为第二套完整平台拓扑。
- `VictoriaLogs:BaseUrl` 配置 Gateway 的日志查询访问；`OpenTelemetry:Logs:Endpoint` 和 `OpenTelemetry:Logs:Path` 配置服务日志接收。
- 首个 Gateway API 为 `POST /api/console/v1/logs/query`，其 `operationId=queryConsoleLogs`，权限为 `observability.logs.read`。
- 后端 API 可供界面集成。本 ADR 不要求在同一范围内交付 Console 日志查看界面。
- 离线和物理隔离部署准备必须包含固定版本的 VictoriaLogs 镜像、其 Apache License 2.0 声明、已配置的持久化卷和选定的保留期。
