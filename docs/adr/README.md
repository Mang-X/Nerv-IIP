# 架构决策记录（ADR）

本目录只记录**长期有效的架构取舍、替代方案与理由**。当前系统结构放在 [`../architecture/`](../architecture/README.md)，当前工程规则放在 [`../governance/`](../governance/README.md)，操作命令与恢复步骤放在 [`../runbooks/`](../runbooks/README.md)，实施进度与证据放在 [`../status/current.md`](../status/current.md)、Issue 与 PR。

## 阅读规则

1. 先用下方“有效决策索引”定位相关 ADR，不按编号从头通读。
2. ADR 决定“为什么和必须保持什么”，不能替代当前代码、Architecture、Governance、Runbook 或验证证据。
3. `已接受（部分修订）` 表示原 ADR 仍有效，但表中列出的精确条款由后续 ADR 改写；不得把整篇旧记录视为失效。
4. 找不到复评条件或唯一现行落点时明确登记 `待核`，不根据当前实现反向编造历史。
5. 整篇或部分取代必须按 [`../governance/decisions/records.md`](../governance/decisions/records.md) 双向记录；Architecture 只能描述现态，不能成为第二份决策记录。

## 有效决策索引

审计基线：`main@3f5bb820`（2026-09-07）。共 29 条 ADR；无整篇“被取代”或“已否决”记录；已确认 4 条部分修订链。

| ADR | Area | 状态 | 取代 / 修订关系 | 复评触发条件 | 当前实现 / 当前权威落点 |
| --- | --- | --- | --- | --- | --- |
| [0001 后端解决方案结构与服务边界](0001-backend-solution-and-service-boundaries.md) | Platform / Backend | 已接受 | — | 待核（原文未单列） | [`../overview/repo-layout.md`](../overview/repo-layout.md)、[`../overview/context-map.md`](../overview/context-map.md)、[`../architecture/platform/core-domain-model.md`](../architecture/platform/core-domain-model.md) |
| [0002 应用接入契约与 Connector Host](0002-connector-host-and-app-integration-contract.md) | Integration | 已接受 | — | 待核（原文未单列） | [`../architecture/integration/connector-protocol-v1.md`](../architecture/integration/connector-protocol-v1.md)、[`../architecture/integration/connector-host-machine-auth.md`](../architecture/integration/connector-host-machine-auth.md) |
| [0003 数据、消息与存储基线](0003-data-and-messaging-baseline.md) | Data / Messaging / Storage | 已接受（部分修订） | [ADR 0023](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) 部分修订 FileStorage 上传传输分类：tus、Gateway proxy 与 storage provider 分轴；其余数据、消息、所有权与观测基线仍有效 | 进入多节点、长期保留或生产可用性要求时，复评本地文件与 Aspire Dashboard 兜底 | [`../architecture/data/README.md`](../architecture/data/README.md)、[`../governance/data/database-schema.md`](../governance/data/database-schema.md)、[`../architecture/platform/caching.md`](../architecture/platform/caching.md)、[`../architecture/platform/file-storage.md`](../architecture/platform/file-storage.md) |
| [0004 AI Integration 边界与治理](0004-ai-integration-boundary-and-governance.md) | AI | 已接受 | — | 出现长流程、多 Agent 或复杂人工审核需求时复评编排边界 | [`../architecture/platform/ai-boundaries.md`](../architecture/platform/ai-boundaries.md) |
| [0005 知识摄取、索引与检索](0005-knowledge-ingestion-and-retrieval.md) | AI / Knowledge | 已接受 | — | 待核（原文未单列） | [`../architecture/platform/knowledge-source-lifecycle.md`](../architecture/platform/knowledge-source-lifecycle.md) |
| [0006 前端工作区结构](0006-frontend-workspace-structure.md) | Frontend | 已接受 | — | 待核（原文未单列） | [`../architecture/frontend/workspace-structure.md`](../architecture/frontend/workspace-structure.md) |
| [0007 Vue Router 文件路由与共置](0007-vue-router-file-routing-colocation.md) | Frontend / Routing | 已接受 | — | 待核（原文未单列） | [`../architecture/frontend/workspace-structure.md`](../architecture/frontend/workspace-structure.md)、[`../governance/frontend/navigation.md`](../governance/frontend/navigation.md) |
| [0008 多目标部署与 Aspire AppHost](0008-multi-target-deployment-and-aspire-apphost.md) | Deployment | 已接受 | — | 待核（原文未单列） | [`../architecture/platform/deployment.md`](../architecture/platform/deployment.md)、[`../runbooks/deployment.md`](../runbooks/deployment.md) |
| [0009 数据库迁移、发布与种子策略](0009-database-migration-release-and-seed-strategy.md) | Data / Release | 已接受（部分修订） | [ADR 0028](0028-retired-vertical-slice-script-entry-boundary.md) 部分修订“第四纵切真实依赖验证脚本可作为本地门禁”的后果；其余迁移、发布与 seed 原则仍有效 | 待核（原文未单列） | [`../governance/data/database-schema.md`](../governance/data/database-schema.md)、[`../reference/data/database-schema-catalog.md`](../reference/data/database-schema-catalog.md)、[`../runbooks/database-release.md`](../runbooks/database-release.md) |
| [0010 自动化脚本可信执行治理](0010-automation-script-trusted-execution-governance.md) | Automation | 已接受 | — | 待核（原文未单列） | [`../governance/script-automation.md`](../governance/script-automation.md)、[`../runbooks/script-automation.md`](../runbooks/script-automation.md) |
| [0011 集成事件契约基线](0011-integration-event-contract-baseline.md) | Integration / Messaging | 已接受 | — | 待核（原文未单列） | [`../governance/backend/integration-events.md`](../governance/backend/integration-events.md) |
| [0012 业务平台领域分层](0012-business-platform-domain-layering.md) | Business Architecture | 已接受（部分修订） | [ADR 0014](0014-aps-and-iiot-scheduling-boundary.md) 部分修订决策 3：Scheduling 与 Field 从 Business 拆出；其余分层、依赖和事实所有权原则仍有效 | 待核（原文未单列） | [`../architecture/business/domain-architecture.md`](../architecture/business/domain-architecture.md)、[`../overview/context-map.md`](../overview/context-map.md) |
| [0013 业务主数据治理](0013-business-master-data-governance.md) | Business / Master Data | 已接受 | — | 待核（原文未单列） | [`../architecture/business/master-data-field-ownership.md`](../architecture/business/master-data-field-ownership.md)、[`../architecture/business/master-data-process-manufacturing.md`](../architecture/business/master-data-process-manufacturing.md)、[`../governance/data/master-data-ownership.md`](../governance/data/master-data-ownership.md) |
| [0014 APS 与 IIoT 排程边界](0014-aps-and-iiot-scheduling-boundary.md) | Scheduling / Field | 已接受（部分修订） | [ADR 0025](0025-field-capability-scope-shift.md) 部分修订决策 3：独立 Field Registry 改为 Field 能力组；Scheduling 独立、事实归属和排程边界仍有效 | 待核（原文未单列） | [`../architecture/business/domain-architecture.md`](../architecture/business/domain-architecture.md)、[`../architecture/business/equipment-status-event-flow.md`](../architecture/business/equipment-status-event-flow.md)、[`../architecture/business/scheduling-order-urgency-retention.md`](../architecture/business/scheduling-order-urgency-retention.md) |
| [0015 网关 HTTP 客户端弹性策略](0015-gateway-http-client-resilience-strategy.md) | Gateway / Resilience | 已接受 | — | 出现第三个 Gateway 时复评共享弹性扩展点 | [`../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/GatewayHttpClientResilience.cs`](../../backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/GatewayHttpClientResilience.cs)、[`../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/BusinessGatewayHttpClientResilience.cs`](../../backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/BusinessGatewayHttpClientResilience.cs) |
| [0016 VictoriaLogs 中央日志后端](0016-victorialogs-central-log-backend.md) | Observability | 已接受 | — | 待核（原文未单列） | [`../architecture/platform/observability.md`](../architecture/platform/observability.md)；镜像版本与运行配置以部署 producer 为准 |
| [0017 业务主链流程管理器与补偿策略](0017-business-process-manager-and-compensation-strategy.md) | Business / Process | 已接受 | — | 出现跨域可管理流程实例、无归属超时、三个以上严格补偿步骤、流程级重放或外部流程引擎硬需求时复评 | [`../architecture/business/domain-architecture.md`](../architecture/business/domain-architecture.md)、[`../governance/backend/integration-events.md`](../governance/backend/integration-events.md) |
| [0018 可观测性阈值告警到 Notification](0018-observability-alert-threshold-to-notification.md) | Observability / Notification | 已接受 | — | 需要指标保留、PromQL、多目标规则组或 Alertmanager 路由时，由新 ADR 复评 VictoriaMetrics / vmalert | [`../architecture/platform/observability.md`](../architecture/platform/observability.md)、[`../architecture/platform/notification.md`](../architecture/platform/notification.md) |
| [0019 WMS 到 Inventory RPC 幂等性](0019-wms-inventory-rpc-idempotency.md) | WMS / Inventory | 已接受 | — | 待核（原文未单列） | [`../architecture/business/wms-inventory-rpc-idempotency.md`](../architecture/business/wms-inventory-rpc-idempotency.md) |
| [0020 NvUI 命名、token 命名空间与样式隔离](0020-nvui-naming-token-namespaces-and-style-isolation.md) | Frontend / Design System | 已接受 | — | 待核（原文未单列） | [`../../frontend/DESIGN/nvui-naming-map.md`](../../frontend/DESIGN/nvui-naming-map.md)、[`../governance/frontend/design-system.md`](../governance/frontend/design-system.md)；实施状态以关联 Issue 为准 |
| [0021 产品文档信息架构与内容治理](0021-product-docs-information-architecture.md) | Product Documentation | 已接受 | — | 待核（原文未单列） | [`../../frontend/apps/docs/`](../../frontend/apps/docs/)、[`../product/README.md`](../product/README.md) |
| [0022 排程演进冻结](0022-scheduling-rescheduling-evolution-freeze.md) | Scheduling | 已接受 | — | 引入求解器、改变三层重排边界或提前拆批时必须新建或修订 ADR | [`../architecture/business/domain-architecture.md`](../architecture/business/domain-architecture.md)、[`../architecture/business/scheduling-order-urgency-retention.md`](../architecture/business/scheduling-order-urgency-retention.md)；交付状态以关联 Issue 为准 |
| [0023 FileStorage tus、staging/final 与 complete 不变量](0023-filestorage-tus-proxy-staging-final-complete-invariants.md) | File Storage | 已接受 | 部分修订 [ADR 0003](0003-data-and-messaging-baseline.md) 的 FileStorage 上传传输分类；不改变 FileStorage 事实所有权 | 待核（原文未单列） | [`../architecture/platform/file-storage.md`](../architecture/platform/file-storage.md)；当前 endpoint、schema、provider 与测试以代码事实为准 |
| [0024 FileStorage provider 与 Local 生产语义](0024-filestorage-storage-provider-and-local-production-semantics.md) | File Storage | 已接受 | 继承 ADR 0023，不取代其 complete 不变量 | 待核（原文未单列） | [`../architecture/platform/file-storage.md`](../architecture/platform/file-storage.md)；当前 provider、配置、迁移与测试以代码事实为准 |
| [0025 Field 能力范围调整](0025-field-capability-scope-shift.md) | Field / Business Architecture | 已接受 | 部分修订 [ADR 0014](0014-aps-and-iiot-scheduling-boundary.md) 决策 3；不改变 Scheduling 独立边界 | 待核（原文未单列） | [`../architecture/business/domain-architecture.md`](../architecture/business/domain-architecture.md)、[`../architecture/business/equipment-status-event-flow.md`](../architecture/business/equipment-status-event-flow.md)；交付状态以关联 Issue 为准 |
| [0026 工业遥测历史数据库存储](0026-industrial-telemetry-historian-storage.md) | Industrial Telemetry | 已接受 | — | PostgreSQL 基准无法满足目标客户配置时复评 TimescaleDB 或其它物理存储策略 | 待核（未确认唯一 Historian 当前架构页；代码、迁移、配置与测试为准） |
| [0027 FileStorage 停服离线迁移与回退](0027-filestorage-offline-migration-cutover-and-rollback.md) | File Storage / Migration | 已接受 | 继承 ADR 0023、0024 | 在线迁移、多 active provider / placement、evidence/remap 失效、跨系统原子能力、容量权威或 cleanup 合规原则变化时复评 | [`../architecture/platform/file-storage.md`](../architecture/platform/file-storage.md)、[`../runbooks/file-storage-offline-migration.md`](../runbooks/file-storage-offline-migration.md)；交付状态以关联 Issue 为准 |
| [0028 退役纵切脚本入口边界](0028-retired-vertical-slice-script-entry-boundary.md) | Automation / Migration | 已接受 | 部分修订 [ADR 0009](0009-database-migration-release-and-seed-strategy.md) 后果 2 | 四个兼容墓碑路径被物理删除时复评墓碑许可与历史引用 | [`../governance/script-automation.md`](../governance/script-automation.md)、[`../runbooks/script-automation.md`](../runbooks/script-automation.md)；物理删除状态以关联 Issue 为准 |
| [0029 参考数据词表独立读权限](0029-reference-data-vocabulary-read-permission.md) | Security / Authorization | 已接受 | — | 专用词表读权限增多到出现按域聚合权限的真实需求时复评命名形态 | [`../governance/security/authorization.md`](../governance/security/authorization.md)、[`../reference/security/authorization-catalog.md`](../reference/security/authorization-catalog.md) |

## 审计结论与待核项

- 已确认部分修订链：`0003 → 0023`、`0009 → 0028`、`0012 → 0014`、`0014 → 0025`。
- 当前没有整篇被取代或已否决 ADR；未来出现时必须在本索引中标出替代记录与精确生效范围。
- `待核（原文未单列）` 表示审计未找到可引用的复评条件，不等于“永不复评”。补录必须有历史证据或通过新 ADR 建立，不能从现行代码倒推。
- `待核（未确认唯一……当前架构页）` 表示当前代码、迁移、配置或测试可以作为事实源，但尚未确认唯一的 Current Architecture 页面；不得为填表而新造第二份权威。
- 本索引是导航与关系账本，不复制 endpoint、schema、配置值、命令、Issue 状态或 CI 结果。

## 新增与修订 ADR

1. 复制 [`template.md`](template.md)，使用下一个四位编号。
2. 只保留长期决策、理由、替代方案与后果；当前命令写入 Runbook，当前实现写入 Architecture，进度与证据写入 Status / Issue / PR。
3. 若修订既有 ADR，按决策记录 Governance 在新旧双方写明精确条款与仍有效范围，并同步本索引。
4. 运行 `pwsh ./scripts/verify-adr-format.ps1`，再按仓库 CI 要求提交。
