# Observability 当前架构

本文描述 Nerv-IIP 当前可观测性的数据所有权、采集/查询边界与 Notification 协作方式。M2-K 迁移前的混合基线包含版本、命令、部署参数、计划表族和建表前置清单，已按原 Git blob 冻结到 [`../../reports/observability-baseline-pre-m2-k-2026-09-07.md`](../../reports/observability-baseline-pre-m2-k-2026-09-07.md)。

## 事实所有权

Observability 拥有日志 chunk/可选条目索引、诊断包和日志包元数据、trace/correlation/operation/instance 关联索引，以及观测数据 retention、清理和归档结果。它不拥有业务事实、Ops 审计、IAM 授权、Notification 投递或 File Storage 文件字节。

## 当前采集与存储边界

1. 服务本地滚动 JSONL 与 OpenTelemetry/OTLP 构成当前日志采集入口；集中日志当前进入 VictoriaLogs，具体版本和 endpoint 由 AppHost、部署配置与运行生产者决定。
2. OpenTelemetry Collector 是可部署的采集/转发边界；它不成为业务事实来源，也不改变服务自身 telemetry 语义。
3. 关闭后的日志文件可以归档为 chunk 并交给 File Storage 保存字节；Observability 只保存引用与查询/保留所需元数据。
4. 平台 Web host 通过共享 `Nerv.IIP.Observability` 接入日志、trace、metric 与 correlation；业务服务不各自形成第二套 provider 配置。
5. PlatformGateway 对外提供受 IAM 约束的查询 facade，前端不直连 VictoriaLogs、Collector、数据库或 File Storage 内部地址。

## 告警边界

Observability 拥有平台级运行指标阈值、规则命名与 firing/resolved 语义；Notification 负责消息、待办、去重、静默和外部通道投递。业务域告警（例如工业设备报警）仍由相应业务服务拥有其领域状态机，不能因为最终投递到了 Notification 就转移事实所有权。

## 权威路由

- 当前部署拓扑：[`deployment.md`](deployment.md)。
- 部署与观测操作：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)。
- 当前 Schema 人工目录：[`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md)。
- Notification 投递边界：[`notification.md`](notification.md)。

精确日志后端版本、配置键、命令、planned tables、一次性测量与阶段状态不在 Current Architecture 维护副本；需要精确事实时回到 AppHost、共享库、Gateway adapter、配置、迁移和测试。
