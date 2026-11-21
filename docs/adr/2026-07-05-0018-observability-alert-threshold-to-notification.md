# ADR 0018：从可观测性阈值告警到 Notification

- 状态：已接受
- 日期：2026-07-05

## 背景

Nerv-IIP 已输出 OpenTelemetry 指标，并具备仅处理日志的 VictoriaLogs 后端。VictoriaLogs 不提供平台的指标告警规则引擎，ADR 0016 也明确将指标和追踪排除在该次范围之外。运维人员仍需要一条适用于私有化部署的默认路径，把服务健康检查失败、CAP/DLQ 积压、Connector Host 心跳过期和 PostgreSQL 资源水位告警发送至 Notification，而不是依赖人工盯看仪表板。

平台也已具备 Notification 外部投递提供方、偏好设置、订阅、去重和投递尝试。Notification 应负责投递告警通知，但不得成为可观测性规则语义的所有者。

## 决策

首个告警范围使用内置的轻量阈值扫描器，暂不引入 vmalert。

由于尚无独立的 Observability 服务，本次范围内扫描器运行在 Notification 进程中。其规则命名空间和源事件仍为 `observability.*`，唯一副作用是提交 Notification 意图。规则所有权仍归 Observability 配置和部署产物；Notification 继续负责投递、偏好设置、去重、静默窗口抑制以及告警恢复通知的投递。

首批基线规则如下：

1. 通过已配置的 `/health` 端点检测服务健康检查失败；
2. 通过现有 Notification 死信存储指标检测需处置的 CAP/DLQ 积压；
3. 通过 AppHub 内部实例查询检测 Connector Host 心跳过期；
4. 通过 PostgreSQL 系统视图/函数检测连接使用率和数据库大小水位。

规则配置在 `Observability:Alerts` 下。AppHost 和 Compose 为单机私有化部署携带一组默认基线规则。扫描器通过现有 Notification 意图管道提交 `observability.AlertFiring` 任务意图和 `observability.AlertResolved` 消息意图，并使用逐规则的去重窗口和静默窗口。

## 理由

当 Nerv-IIP 增加 VictoriaMetrics 等兼容 Prometheus 的指标存储后，vmalert 仍是首选候选方案。它在 VictoriaMetrics 生态中已有成熟的运维实践，并提供强大的规则语义。在本次范围内，引入 vmalert 还需要新增并支持指标后端、抓取拓扑、兼容 Alertmanager 的路由或自定义 webhook 桥接；对于当前单机私有化基线而言，这会引入过多基础设施。

轻量扫描器复用平台已有事实，使闭环路径保持精简：

1. 平台服务已暴露健康检查端点；
2. Notification 已具备 DLQ 指标和意图提交能力；
3. AppHub 已拥有 Connector Host 心跳事实；
4. PostgreSQL 已是默认持久化依赖。

## 后果

Nerv-IIP 由此具备默认的告警到 Notification 闭环路径，且无需引入另一个必需的运行时组件。运维人员可通过现有 Notification 渠道接收告警触发和恢复通知。

该扫描器有意保持简单。它不是完整的 Prometheus 规则引擎，不计算任意 PromQL，也不替代未来采用 VictoriaMetrics/vmalert。如果后续必须支持指标保留、PromQL 风格表达式、多目标规则组或兼容 Alertmanager 的路由，应由一份关于 VictoriaMetrics 指标存储和 vmalert 的 ADR 取代本 ADR。

由于扫描器暂时托管在 Notification 中，新的规则探针不得直接更改 Notification 领域状态。它们只能采集 Observability 样本，并通过其他生产者使用的同一公开应用路径提交 Notification 意图。

## 已考虑的替代方案

1. **立即采用 vmalert**：本次不采用，因为当前默认拓扑没有指标后端，新增指标后端会将 #735 扩大为更广泛的可观测性平台变更。
2. **仅使用客户外部监控**：不采用，因为私有化单机部署需要在客户集成自身监控栈前即可工作的内置基线。
3. **仅使用 Notification 专用 DLQ 工作器**：Notification DLQ 已有该工作器，但其范围过窄，无法覆盖服务健康、Connector Host 心跳和 PostgreSQL 水位。
