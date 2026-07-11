# 统一授权矩阵

本文档汇总 Nerv-IIP 平台权限码、调用主体类型和授权范围维度，作为 IAM 授权事实与各服务权限命名的统一入口。

上下文来源：

- [IAM 认证与授权基线](iam-authentication-baseline.md)
- [平台上下文地图](context-map.md)
- [Connector Host 机器身份认证终态](connector-host-machine-auth.md)

后续 Notification、Knowledge、AI Integration 与 Observability baseline 文档创建后，应反向链接本文档，并在对应服务实现时标明权限 seed 与 enforcement 状态。

## 命名规则

权限码采用 `{domain}.{resource}.{action}` 风格，全部小写，资源名默认使用复数。既有权限码在同一主版本内不得改变语义；如果必须改变权限语义或收窄/放宽授权边界，必须提升主版本并提供迁移说明。

授权判断由四类输入共同决定：

1. `principalType`：调用主体类型。
2. permission code：稳定权限码。
3. organization/environment scope：组织与环境边界。
4. resource/capability scope：具体资源或能力边界。

Endpoint 只声明需要的权限码与上下文；业务不变式仍由 Domain guard 或应用服务校验。

## principalType 维度

| principalType | 说明 | 典型入口 | 授权事实来源 |
| --- | --- | --- | --- |
| `user` | 平台控制台用户或运维管理员。 | Console/Gateway、IAM 管理接口、后续管理后台。 | IAM User、Role、Membership、UserSession。 |
| `connector-host` | Connector Host 机器身份。 | AppHub 注册/心跳/状态同步，Ops 任务领取与结果回传。 | IAM ConnectorHostCredential、organization/environment、capability scope。 |
| `external-client` | 外部系统、平台应用或受控第三方客户端。 | Platform SDK、公开 API、Webhook 回调、后续 OAuth/OIDC client credential。 | IAM ExternalClient、AuthorizationGrant、organization/environment、resource/capability scope。 |
| `internal-service` | 平台内部服务到服务调用主体。 | Gateway 到 IAM 授权检查，服务间后台任务或事件处理后的回查。 | 平台部署身份、服务账号或后续 workload identity；不得绕过 IAM 边界事实。 |

当前 access token 基线已覆盖 `user`、`external-client`、`connector-host` 的 token claim 形态；`internal-service` 已作为服务间认证入口落地在 Ops、FileStorage、Notification 的非 health 内部 API 上。服务间调用必须携带 `Authorization: Bearer <InternalService:BearerToken>`，Development 环境使用本地默认 token，非 Development 环境必须显式配置 `InternalService:BearerToken`。该 bearer token 只证明调用方属于平台内部服务，不替代最终用户、Connector Host 或外部客户端的 IAM 授权事实。

## resource scope 维度

| scope | 含义 | 适用示例 | 校验要求 |
| --- | --- | --- | --- |
| organization | 组织级授权边界。 | IAM 用户/角色管理、组织级通知偏好、组织级模型配置。 | 请求必须携带或解析 organizationId，授权事实必须匹配。 |
| environment | 环境级授权边界。 | 开发/生产环境实例查询、运维任务、日志查询、知识源检索。 | 请求必须携带或解析 environmentId，不能跨环境隐式复用授权。 |
| resource | 单个或一组资源边界。 | application instance、operation task、file、knowledge source、notification template。 | 权限码只表达动作类别；资源所有权、可见性和状态需单独校验。 |
| capability | 能力级边界。 | Connector Host 上报能力、AI 工具执行、外部客户端可调用能力。 | 必须和 permission code 同时匹配，不能只靠能力声明授权执行。 |

## 主体与范围矩阵

| principalType | organization | environment | resource | capability | 说明 |
| --- | --- | --- | --- | --- | --- |
| `user` | 必须 | 常规必须 | 按入口需要 | 按入口需要 | 控制台和管理后台的默认主体；角色权限只在组织/环境 membership 内生效。 |
| `connector-host` | 必须 | 必须 | 常规必须 | 必须 | 只能访问自身被授予的实例、任务和上报能力；不能获得 IAM 管理权限。 |
| `external-client` | 必须 | 常规必须 | 常规必须 | 常规必须 | 面向平台应用或第三方系统；授权必须有有效期和可撤销授予。 |
| `internal-service` | 必须 | 按调用需要 | 按调用需要 | 按调用需要 | 仅用于平台服务间调用；调用方身份不替代最终用户或机器主体授权。 |

## 已实现服务权限码表

下表来自当前 IAM seed 权限集和 Gateway 已接入的 permission enforcement。状态为“已 seed”表示 IAM 初始角色权限会包含该权限码；状态为“Gateway 已 enforcement”表示现有 Console API 已实际声明并转发到 IAM 授权检查。

| 权限码 | 服务域 | 建议 principalType | 建议 scope | 当前状态 | 说明 |
| --- | --- | --- | --- | --- | --- |
| `iam.users.read` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查；Gateway Console facade 已 enforcement | 查看用户。 |
| `iam.users.manage` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查；Gateway Console facade 已 enforcement | 创建、编辑、禁用、重置用户。 |
| `iam.roles.read` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查；Gateway Console facade 已 enforcement | 查看角色与权限。 |
| `iam.roles.manage` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查；Gateway Console facade 已 enforcement | 创建角色、调整角色权限。 |
| `iam.sessions.read` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查；Gateway Console facade 已 enforcement | 查看会话。 |
| `iam.sessions.revoke` | IAM | `user` | organization | 已 seed；IAM 管理端点已检查；Gateway Console facade 已 enforcement | 撤销会话。 |
| `connectors.registrations.write` | Connectors/AppHub | `connector-host` | environment + capability | 已 seed | Connector Host 注册或更新应用实例事实。 |
| `connectors.heartbeats.write` | Connectors/AppHub | `connector-host` | environment + resource + capability | 已 seed | Connector Host 上报心跳。 |
| `connectors.state-snapshots.write` | Connectors/AppHub | `connector-host` | environment + resource + capability | 已 seed | Connector Host 上报实例状态快照。 |
| `apphub.instances.read` | AppHub | `user` / `external-client` / `internal-service` | environment + resource | 已 seed；Gateway 已 enforcement | 查看应用实例列表与详情。 |
| `files.upload` | File Storage | `user` / `connector-host` / `external-client` | environment + resource | 已 seed | 创建上传会话并完成文件上传。 |
| `files.read` | File Storage | `user` / `connector-host` / `external-client` / `internal-service` | environment + resource | 已 seed | 查看文件元数据。 |
| `files.download-grants.create` | File Storage | `user` / `external-client` / `internal-service` | environment + resource | 已 seed | 创建短期下载授权。 |
| `files.archive` | File Storage | `user` / `internal-service` | environment + resource | 已 seed | 归档文件。 |
| `ops.tasks.create` | Ops | `user` / `external-client` / `internal-service` | environment + resource + capability | 已 seed；Gateway 已 enforcement；ExternalClient client_credentials grant 已可校验 | 创建运维任务。 |
| `ops.tasks.read` | Ops | `user` / `connector-host` / `external-client` / `internal-service` | environment + resource | 已 seed；Gateway 已 enforcement | 查看运维任务。 |
| `ops.results.write` | Ops | `connector-host` | environment + resource + capability | 已 seed | Connector Host 回传动作结果。 |
| `ops.audit.read` | Ops | `user` / `internal-service` | organization / environment + resource | 已 seed | 查看审计记录。 |
| `observability.logs.read` | Observability | `user` / `internal-service` | organization / environment + resource | 已 seed；Gateway Console facade 已 enforcement | 查询集中平台日志；Gateway 不暴露 VictoriaLogs URL 或 LogsQL。 |

## Console IAM Admin Facade 权限映射

PlatformGateway 的 Console IAM Admin facade 在转发 IAM 管理请求前，会用当前 bearer token 调 IAM current-principal/authorization path 校验组织、环境和权限码。浏览器只访问 Gateway，不直连 IAM。

| Console facade route | operationId | 权限码 |
| --- | --- | --- |
| `GET /api/console/v1/iam/users` | `listConsoleIamUsers` | `iam.users.read` |
| `POST /api/console/v1/iam/users` | `createConsoleIamUser` | `iam.users.manage` |
| `PATCH /api/console/v1/iam/users/{userId}` | `updateConsoleIamUser` | `iam.users.manage` |
| `POST /api/console/v1/iam/users/{userId}/disable` | `disableConsoleIamUser` | `iam.users.manage` |
| `POST /api/console/v1/iam/users/{userId}/reset-password` | `resetConsoleIamUserPassword` | `iam.users.manage` |
| `GET /api/console/v1/iam/roles` | `listConsoleIamRoles` | `iam.roles.read` |
| `POST /api/console/v1/iam/roles` | `createConsoleIamRole` | `iam.roles.manage` |
| `PATCH /api/console/v1/iam/roles/{roleId}/permissions` | `updateConsoleIamRolePermissions` | `iam.roles.manage` |
| `GET /api/console/v1/iam/permissions` | `listConsoleIamPermissions` | `iam.roles.read` |
| `GET /api/console/v1/iam/sessions` | `listConsoleIamSessions` | `iam.sessions.read` |
| `POST /api/console/v1/iam/sessions/{sessionId}/revoke` | `revokeConsoleIamSession` | `iam.sessions.revoke` |

## Console Observability Facade 权限映射

PlatformGateway 的 Console Observability facade 在查询 VictoriaLogs 前，会用当前 bearer token 调 IAM current-principal/authorization path 校验组织、环境和权限码。浏览器只访问 Gateway，不直连 VictoriaLogs、Collector 或 Aspire Dashboard。

| Console facade route | operationId | 权限码 |
| --- | --- | --- |
| `POST /api/console/v1/logs/query` | `queryConsoleLogs` | `observability.logs.read` |

## 待落地服务权限命名

以下权限码用于冻结后续服务和业务扩展的命名口径。已实现服务的权限必须进入 `NervIipSeedPermissions.All` 与端点授权检查；尚未实现的服务在落地时必须按本节命名进入 IAM seed、OpenAPI 测试和权限测试。

### Notification

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `notifications.messages.read` | `user` / `external-client` | organization / environment + resource | 查询站内通知、待办、已读未读和资源相关消息。 |
| `notifications.subscriptions.manage` | `user` / `external-client` | organization / environment + resource | 管理事件订阅、通知偏好和订阅过滤条件。 |
| `notifications.templates.manage` | `user` / `internal-service` | organization + resource | 管理通知模板、模板版本和多语言文本。 |
| `notifications.deliveries.manage` | `user` / `internal-service` | organization / environment + resource | 管理投递尝试、重试、失败诊断和外部通道投递状态。 |

### Knowledge

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `knowledge.retrievals.query` | `user` / `external-client` / `internal-service` | environment + resource | 执行知识检索并返回带引用的片段；必须经过权限过滤。 |
| `knowledge.sources.manage` | `user` / `external-client` | environment + resource | 创建、配置、暂停、归档知识源和同步策略。 |
| `knowledge.indexes.rebuild` | `user` / `internal-service` | environment + resource | 触发索引重建、权限同步后重建或策略变更后的重建。 |

### AI Integration

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `ai.models.configure` | `user` / `internal-service` | organization / environment | 管理模型提供方配置、模型参数和可用范围。 |
| `ai.tools.register` | `user` / `external-client` / `internal-service` | organization / environment + capability | 注册 MCP Server、Skill 或平台工具。 |
| `ai.tools.execute` | `user` / `external-client` / `internal-service` | environment + resource + capability | 执行已授权工具；执行类工具仍需 Ops 或目标服务的动作授权。 |
| `ai.approvals.manage` | `user` / `internal-service` | environment + resource | 管理工具执行审批、人机确认和高风险动作批准。 |
| `ai.prompts.manage` | `user` / `internal-service` | organization / environment + resource | 管理 prompt 模板、版本、启停和适用范围。 |

### Observability

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `observability.diagnostics.read` | `user` / `internal-service` | environment + resource | 查看诊断包、日志包和归档元数据。 |
| `observability.retention.manage` | `user` / `internal-service` | organization / environment | 管理日志 retention、清理任务和归档策略。 |

### Mobile Platform

`mobile.*` 权限码用于平台级移动设备管理、诊断和部署治理。它们区别于 `business.*` 业务执行权限：`mobile.*` 不表达收货、拣货、报工、检验或维修等业务动作；业务动作仍必须使用对应 `business.*` 权限码。PDA 执行业务操作时通常同时需要业务权限、组织/环境上下文和设备上下文；设备可见性或诊断上传再由 `mobile.*` 约束。

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `mobile.devices.read` | `user` / `internal-service` | organization / environment + resource | 查看 PDA 设备、安装实例、App 版本、最近同步时间和失败摘要。 |
| `mobile.devices.manage` | `user` / `internal-service` | organization / environment + resource | 登记、停用、解绑 PDA 设备，管理设备默认组织/环境和准入策略。 |
| `mobile.diagnostics.write` | `user` / `external-client` | environment + resource | PDA 上传脱敏诊断摘要、同步队列摘要、设备信息和最近错误。 |

### Business Platform

业务平台权限码用于 ADR 0012 与 ADR 0014 定义的关键链路领域扩展。BusinessMasterData、BusinessProductEngineering、BusinessInventory、BusinessQuality、BusinessMES、BusinessDemandPlanning、BarcodeLabel、BusinessApproval、WMS、BusinessIndustrialTelemetry、BusinessMaintenance 和 BusinessScheduling / APS lite 权限已随对应 MVP、#206 或 #207 进入 IAM seed 或服务授权基线。BusinessScheduling 当前已完成服务端 Endpoint permission metadata、IAM seed 和 BusinessGateway facade 最终用户权限 enforcement。#207 BusinessGateway 设备运行事实 facade 当前对 overview、device 和 availability 强制 `business.iiot.telemetry.read`，对 alarms 强制 `business.iiot.alarms.read`；`business.maintenance.work-orders.read` 与 `business.maintenance.plans.read` 属于 Maintenance 下游/domain catalog 和相关只读权限，当前设备 facade 合并维护窗口时通过 internal service token 调用 Maintenance，并未在 Gateway endpoint 上额外强制这两个 maintenance 权限。其他业务域权限在实现对应服务时，也必须按本表进入 IAM seed、Endpoint 鉴权、OpenAPI 测试和权限测试。

| 权限码 | 建议 principalType | 建议 scope | 说明 |
| --- | --- | --- | --- |
| `business.masterdata.products.read` | `user` / `external-client` / `internal-service` | organization / environment + resource | 查看 SKU 和产品基础属性。 |
| `business.masterdata.products.manage` | `user` / `external-client` | organization / environment + resource | 创建、修改、启停 SKU 和产品基础属性。 |
| `business.masterdata.partners.read` | `user` / `external-client` / `internal-service` | organization / environment + resource | 查看客户、供应商、承运商。 |
| `business.masterdata.partners.manage` | `user` / `external-client` | organization / environment + resource | 创建、修改、启停业务伙伴。 |
| `business.masterdata.resources.read` | `user` / `external-client` / `internal-service` | organization / environment + resource | 查看工作中心、工作日历、设备资产、人员业务属性，以及 BusinessGateway 工人选择器所需的 IAM worker directory 最小读面。 |
| `business.masterdata.resources.manage` | `user` / `external-client` | organization / environment + resource | 管理工作中心、工作日历、设备资产和人员业务属性。 |
| `business.engineering.documents.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 CAD、图纸、工艺文件等工程文件引用和版本。 |
| `business.engineering.documents.manage` | `user` / `external-client` | environment + resource | 注册、归档和关联工程文件；文件本体仍归 File Storage。 |
| `business.engineering.items.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看工程物料及其版本状态。 |
| `business.engineering.items.manage` | `user` / `external-client` | environment + resource | 创建和发布工程物料版本。 |
| `business.engineering.boms.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 EBOM 与 MBOM 版本。 |
| `business.engineering.boms.manage` | `user` / `external-client` | environment + resource | 创建、修改、发布 EBOM 与 MBOM。 |
| `business.engineering.routings.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看工艺路线版本及工序。 |
| `business.engineering.routings.manage` | `user` / `external-client` | environment + resource | 创建、修改、发布工艺路线版本。 |
| `business.engineering.standard-operations.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看标准工序主数据、默认工作中心、默认工时和控制标志。 |
| `business.engineering.standard-operations.manage` | `user` / `external-client` | environment + resource | 创建、调整和归档标准工序主数据。 |
| `business.engineering.production-versions.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看和解析 SKU 可用生产版本绑定。 |
| `business.engineering.production-versions.manage` | `user` / `external-client` | environment + resource | 创建、调整和归档生产版本绑定。 |
| `business.engineering.changes.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 ECO/ECN、影响范围和发布记录。 |
| `business.engineering.changes.manage` | `user` / `external-client` | environment + resource | 发起、审批后发布工程变更。 |
| `business.planning.demands.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看销售订单、预测、安全库存等需求来源。 |
| `business.planning.demands.manage` | `user` / `external-client` | environment + resource | 创建和调整计划需求来源。 |
| `business.planning.mrp.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 MPS/MRP run、净需求和 pegging。 |
| `business.planning.mrp.run` | `user` / `internal-service` | environment + resource | 运行 MPS/MRP，生成计划采购建议和计划工单建议。 |
| `business.planning.suggestions.manage` | `user` / `internal-service` | environment + resource | 接受、拒绝或关闭计划建议；不直接创建正式单据。 |
| `business.inventory.locations.manage` | `user` / `external-client` | environment + resource | 管理仓库、库区、库位和容量限制。 |
| `business.inventory.ledger.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看库存台账、可用量、冻结量、批次和序列号。 |
| `business.inventory.movements.create` | `user` / `external-client` / `internal-service` | environment + resource | 创建受控库存移动；必须携带幂等键和业务来源。 |
| `business.inventory.counts.manage` | `user` / `external-client` | environment + resource | 创建盘点任务、确认差异和提交盘点调整。 |
| `business.barcodes.templates.manage` | `user` / `external-client` | organization / environment + resource | 管理条码规则和标签模板。 |
| `business.barcodes.scans.write` | `user` / `external-client` / `connector-host` | environment + resource | 写入扫码记录；PDA 或 Connector 场景必须携带幂等键。 |
| `business.barcodes.print` | `user` / `external-client` | environment + resource | 生成或打印标签。 |
| `business.approvals.read` | `user` / `external-client` / `internal-service` | organization / environment + resource | 查看业务审批链、审批记录和待办状态。 |
| `business.approvals.manage` | `user` / `external-client` / `internal-service` | organization / environment + resource | 创建审批链并处理业务审批步骤；不适用于 Ops 运维审批。 |
| `business.quality.inspection-plans.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建、激活和版本化检验计划与检验特性。 |
| `business.quality.inspection-records.create` | `user` / `external-client` / `internal-service` | environment + resource | 记录收货、工序、终检、维修或客退检验结果。 |
| `business.quality.inspection-records.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看检验计划和检验记录。 |
| `business.quality.ncr.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看不合格品报告、处置方案和关闭结果。 |
| `business.quality.ncr.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建不合格品报告、提交处置方案并记录关闭所需的外部执行引用。 |
| `business.erp.procurement.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看采购申请、询价、供应商报价、采购订单、收货和退货。 |
| `business.erp.procurement.manage` | `user` / `external-client` | environment + resource | 创建和推进采购申请、询价、订单、收货、退货；承载 SRM-lite。 |
| `business.erp.sales.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看客户、商机、报价、销售订单、发货请求和销售退货。 |
| `business.erp.sales.manage` | `user` / `external-client` | environment + resource | 创建和推进商机、报价、销售订单、发货和退货；承载 CRM-lite/OMS-lite。 |
| `business.erp.finance.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看应收、应付、凭证、成本核算和财务汇总。 |
| `business.erp.finance.manage` | `user` / `internal-service` | environment + resource | 创建凭证、生成应收应付和成本核算；首批不包含完整总账月结。 |
| `business.scheduling.plans.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 APS lite 排程方案、资源负载、冲突、不可排原因和 Gantt DTO；当前 IAM seed、服务端 Endpoint metadata 与 BusinessGateway facade enforcement 已实现。 |
| `business.scheduling.plans.manage` | `user` / `internal-service` | environment + resource | 预览和生成 APS lite 排程方案；当前 IAM seed、服务端 Endpoint metadata 与 BusinessGateway facade enforcement 已实现。 |
| `business.scheduling.plans.release` | `user` / `internal-service` | environment + resource | 发布已生成的 APS lite 排程方案供 MES 等下游消费；当前 IAM seed、服务端 Endpoint metadata 与 BusinessGateway facade enforcement 已实现。 |
| `business.wms.receipts.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看收货通知、入库单和上架任务。 |
| `business.wms.receipts.manage` | `user` / `external-client` | environment + resource | 创建和完成收货、入库、上架作业。 |
| `business.wms.shipments.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看出库单、拣货任务和复核包装。 |
| `business.wms.shipments.manage` | `user` / `external-client` | environment + resource | 创建和完成出库、拣货、复核包装作业。 |
| `business.wms.automation.manage` | `user` / `external-client` / `connector-host` | environment + resource + capability | 调度 WCS adapter 任务并处理外部自动化设备回执。 |
| `business.mes.foundation.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 MES 基础就绪、生产版本、物料、质量、设备、条码和编号阻塞项。 |
| `business.mes.overview.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看 MES 生产驾驶舱和待办摘要。 |
| `business.mes.plans.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看生产计划候选、计划就绪和转工单前检查。 |
| `business.mes.work-orders.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看工单、工单详情和释放快照。 |
| `business.mes.work-orders.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建急单、计划转工单、释放和关闭工单。 |
| `business.mes.materials.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看齐套检查、领料申请和线边收料状态。 |
| `business.mes.materials.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建领料/备料申请并确认线边收料。 |
| `business.mes.dispatch.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看派工任务和资源阻塞。 |
| `business.mes.dispatch.manage` | `user` / `external-client` / `internal-service` | environment + resource | 指派工序任务到人员、设备、班次和工作中心。 |
| `business.mes.operations.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看工序任务和 WIP 摘要。 |
| `business.mes.operations.manage` | `user` / `external-client` / `internal-service` | environment + resource | 开始、暂停、恢复和完成工序任务。 |
| `business.mes.reporting.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看报工记录和生产日报。 |
| `business.mes.reporting.write` | `user` / `external-client` | environment + resource | 提交工序报工、合格数、不良数和工时。 |
| `business.mes.quality.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看工单/工序关联的质量、缺陷和 NCR 上下文。 |
| `business.mes.quality.write` | `user` / `external-client` | environment + resource | 记录生产过程不良、返工/报废上下文，以及带理由的 MES 质量 hold 人工强制释放。 |
| `business.mes.receipts.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看完工入库请求。 |
| `business.mes.receipts.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建完工入库请求。 |
| `business.mes.downtime.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看设备停机和产能影响事件。 |
| `business.mes.downtime.manage` | `user` / `external-client` / `internal-service` | environment + resource | 记录停机、确认恢复并影响短期执行能力。 |
| `business.mes.handovers.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看班次交接和未结事项。 |
| `business.mes.handovers.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建和接收班次交接。 |
| `business.mes.traceability.read` | `user` / `external-client` / `internal-service` | environment + resource | 按工单、批次/序列号、物料批追溯执行证据。 |
| `business.mes.schedules.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看规则排程版本和执行结果。 |
| `business.mes.schedules.manage` | `user` / `external-client` / `internal-service` | environment + resource | 触发规则派工、发布或撤销排产版本。 |
| `business.mes.capacity.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看产能影响、设备不可用和执行阻塞摘要。 |
| `business.iiot.tags.manage` | `user` / `external-client` | environment + resource | 管理设备 tag 映射、采集点和单位。 |
| `business.iiot.alarm-rules.manage` | `user` / `external-client` | environment + resource | 管理设备报警规则阈值和启停状态；BusinessGateway telemetry alarm-rules 写面强制该权限。 |
| `business.iiot.telemetry.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看设备状态快照、时序摘要和 OEE 输入数据；#207 BusinessGateway equipment overview/device/availability facade 已强制该权限。 |
| `business.iiot.telemetry.write` | `user` / `connector-host` / `external-client` | environment + resource + capability | 写入受控采集样本或状态摘要；#472 BusinessGateway manual seed facade 强制该权限；不得表达 PLC/DCS 控制授权。 |
| `business.iiot.alarms.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看工业报警、状态和处理进度；#207 BusinessGateway equipment alarms facade 已强制该权限。 |
| `business.iiot.alarms.write` | `user` / `connector-host` / `external-client` | environment + resource + capability | 写入报警产生、清除和状态变化事实；#472 BusinessGateway manual alarm facade 强制该权限。 |
| `business.iiot.device-control.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看设备控制命令下发结果、历史与控制通道绑定；MAN-438/#792 BusinessGateway device-control command get/list 与 binding list facade 强制该权限。 |
| `business.iiot.device-control.write` | `user` / `external-client` | environment + resource + capability | 下发审批门禁的设备控制命令（写值/启停/参数下发）；MAN-438/#792 BusinessGateway device-control command create facade 强制该权限；不表达直连 PLC/DCS/SCADA 控制授权。 |
| `business.iiot.device-control.manage` | `user` / `external-client` | environment + resource | 维护设备控制通道绑定（设备 → 连接器主机/实例路由目标）；MAN-438/#792 BusinessGateway device-control binding create-or-update/disable facade 强制该权限，与命令下发 `.write` 分离以防止有下发权限的角色篡改路由。 |
| `business.maintenance.work-orders.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看维修工单、故障、停机和维修结果；#207 设备运行事实相关但当前 Gateway equipment facade 未额外强制该权限。 |
| `business.maintenance.work-orders.manage` | `user` / `external-client` / `internal-service` | environment + resource | 创建、分派、完成维修工单。 |
| `business.maintenance.plans.read` | `user` / `external-client` / `internal-service` | environment + resource | 查看保养计划、点检计划和执行记录；#207 availability 合并维护窗口时由下游 Maintenance/domain catalog 使用，当前 Gateway equipment facade 未额外强制该权限。 |
| `business.maintenance.plans.manage` | `user` / `external-client` | environment + resource | 创建、调整和关闭保养计划、点检计划。 |

## 落地要求

1. 新增权限码必须先更新本文档，再进入 IAM seed、迁移、端点授权检查和测试。
2. 对外 API、Gateway facade、SDK 和服务间调用不得各自发明未登记权限码。
3. `connector-host` 和 `external-client` 必须同时校验 organization、environment、resource 或 capability scope；不得只校验权限码。
4. `internal-service` 只能表达服务到服务调用身份，不得作为绕过最终用户、外部客户端或 Connector Host 授权的后门。
5. Notification、Knowledge、AI Integration、Observability 和 Mobile Platform 首次实现时，应在对应服务文档中链接本文档，并标明实际 seed 状态。
