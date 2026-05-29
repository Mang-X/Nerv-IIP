# API 契约与代码生成规范

本文档定义 Nerv-IIP 前后端接口契约的单一事实来源、代码生成链路、目录边界与变更流程。

## 总原则

1. OpenAPI 是接口的单一事实来源。
2. 前端不手写大批 DTO 与重复请求函数。
3. 生成代码与手写代码必须隔离。
4. API 契约升级必须能追踪到 ADR、后端接口变更和前端消费更新。
5. 主平台对外提供的 SDK、OpenAPI、事件和协议遵循主版本对齐、小版本兼容策略。
6. 任何 JSON/text 序列化字段进入 API、SDK、IntegrationEvent 或外部协议前，必须定义 schema/version/compat 说明；不能只把数据库中的 JSON blob 原样提升为公开契约。

## Platform SDK 与版本策略

1. Platform SDK 是主平台提供给应用、Connector Host、行业扩展和前端包的稳定能力集合，详细模块边界见 docs/architecture/platform-sdk-baseline.md。
2. Platform SDK 可包含 OpenAPI 生成客户端、公开 DTO、Connector Protocol、认证与授权上下文、文件存储客户端、上传指令 DTO、运维客户端、通知客户端、观测上下文辅助、错误模型、事件契约和缓存键辅助约定。
3. 应用、Connector Host 或扩展的主版本必须与主平台主版本对齐；例如 1.x 应用面向 1.x 主平台，2.x 应用面向 2.x 主平台。
4. 应用、Connector Host 或扩展的小版本可以低于主平台小版本；主平台 1.5 应尽量兼容基于 1.0 到 1.5 SDK 构建的应用。
5. 同一主版本内的 SDK、API、事件和协议变更应保持向后兼容，包括新增可选字段、新增端点、新增能力码和新增错误码。
6. 删除字段、改变字段语义、改变必填性、改变认证语义、改变事件含义或移除能力属于破坏性变更，必须提升主版本。
7. 主平台大版本升级时，应明确支持的旧主版本迁移窗口；迁移窗口结束后，旧主版本应用需要升级到相同主版本。
8. 这里的版本策略指 Platform SDK 兼容版本，不等同于 AppHub 记录的 ApplicationVersion。ApplicationVersion 仍表示受管应用自身的业务版本、镜像版本或发布版本。

## SDK 模块与契约生成

1. `Nerv.IIP.Sdk.Core` 提供 transport、错误模型、correlationId、idempotencyKey、组织环境上下文和版本上报。
2. `Nerv.IIP.Sdk.Auth` 提供 token、client credential 和认证头处理，但不做最终授权判断。
3. `Nerv.IIP.Sdk.ConnectorProtocol` 提供注册、心跳、状态快照和动作结果回传客户端，不拥有 AppHub 事实。
4. `Nerv.IIP.Sdk.FileStorage` 提供上传会话、上传指令、完成/取消上传和下载授权客户端，隐藏 tus、S3 multipart 和 server-proxy 差异。
5. `Nerv.IIP.Sdk.Ops` 提供运维任务与动作结果客户端，可提交审计意图，但正式 AuditRecord 仍由 Ops 服务端生成。
6. `Nerv.IIP.Sdk.Notification` 提交通知意图、查询通知、标记已读和查询待办，不直接调用外部通知通道。
7. `Nerv.IIP.Sdk.Observability` 提供 trace context、correlationId 和标准日志字段辅助，不替代日志采集或审计落库。
8. SDK 模块应优先从 OpenAPI、公开 DTO 和版本化契约生成或包装，不允许引用服务端 Domain、Infrastructure 或数据库模型。

## 契约来源

### 后端责任

1. 后端服务或 Gateway 负责输出稳定 OpenAPI 文档。
2. Gateway 暴露给前端的页面级接口也必须纳入 OpenAPI。
3. 契约变更需要遵循同主版本向后兼容或显式主版本升级原则。

### Gateway Console OpenAPI

1. 控制台前端只直接消费 PlatformGateway 暴露的 `/api/console/**` 接口，不直接调用 AppHub、Ops、Iam、FileStorage 等领域服务接口。
2. PlatformGateway 的 OpenAPI 文档通过 FastEndpoints.Swagger 输出，第三迭代起固定本地文档入口为 `/swagger/v1/swagger.json`。
3. 控制台接口必须提供稳定 `operationId`，供 Hey API 生成可读、可追踪的 query 与 mutation helper。
4. Gateway Endpoint 仍优先保持属性路由风格；控制台 `operationId` 由 `UseFastEndpoints` 的 Endpoint name generator 集中映射到稳定名称。只有单个 Endpoint 需要复杂 metadata 时，才完整切换为 `Configure()` 并使用 `Description(x => x.WithName(...))`。
5. `operationId` 使用 lower camelCase，并以业务动作表达意图，例如 `listConsoleInstances`、`getConsoleInstanceDetail`、`restartConsoleInstance`、`getConsoleOperationTask`。
6. 新增或修改 Gateway 控制台接口时，必须先更新后端 Endpoint 与 OpenAPI 测试，再导出 OpenAPI 快照并重新生成前端 api-client。
7. OpenAPI 是契约事实来源；导出的 JSON 快照是前端生成输入，不允许手改快照来绕过后端契约。

### BusinessGateway Console OpenAPI

业务控制台前端只直接消费 BusinessGateway 暴露的 `/api/business-console/v1/**` 接口，不直接调用 BusinessMasterData、Inventory、Quality、MES、IAM 或 FileStorage 的服务 URL。BusinessGateway 属于业务平台聚合入口，不属于 PlatformGateway；它可以执行用户鉴权、IAM 权限检查、上下文透传、internal token 下游调用和页面级响应整理，但不承载业务规则或持久事实。

BusinessGateway Console OpenAPI 的生成链路固定为：

1. BusinessGateway 通过 FastEndpoints.Swagger 输出 `/swagger/v1/swagger.json`。
2. 导出脚本将 BusinessGateway Console OpenAPI 快照写入 `frontend/packages/api-client/openapi/business-gateway-console.v1.json`。
3. `frontend/packages/api-client/openapi-ts.config.ts` 增加 business-console input，生成到 `frontend/packages/api-client/src/generated/business-console/`，与现有 PlatformGateway generated 文件和移动端 generated 文件隔离；多 input 生成任务必须避免互相清理输出目录，当前 Hey API 配置使用独立 output path，并在 `generate` script 中先清理已知 generated 文件，再关闭 per-job clean，避免并行 job 互删子目录。
4. `frontend/packages/api-client/src/business-console.ts` 提供业务控制台稳定导出；`src/index.ts` 可以重新导出业务控制台需要的类型、SDK 和 Pinia Colada query/mutation options。
5. `frontend/apps/business-console` 只从 `@nerv-iip/api-client` 稳定入口消费，不深 import `src/generated/business-console/*`。
6. OpenAPI 快照是生成输入，不允许手改；新增或修改 business-console endpoint 时必须先更新 BusinessGateway endpoint、OpenAPI/authorization/proxy tests，再导出快照并运行 `pnpm -C frontend generate:api`。
7. `@hey-api/openapi-ts` 当前生成的 fetch client 内含上游 TODO 注释；该注释属于 generated artifact，不在项目内手改，升级生成器前通过 `frontend/packages/api-client/scripts/clean-generated.mjs` 保持生成目录无陈旧文件。
8. BusinessGateway facade 转发下游查询时，布尔开关参数采用“默认 false 省略、true 显式发送”的约定；例如 MasterData `includeDisabled=false` 不进入下游 query string，`includeDisabled=true` 才会发送，避免 Gateway 把下游默认值重复编码进 facade。
9. `runBusinessConsoleMesSchedule` 是 MES 规则排程的过渡入口，用于 #206 前的确定性规则排程触发和结果状态查看；它不是长期 APS 权威接口。#206 落地 BusinessScheduling/APS lite 后，Business Console 的正式排程视图和甘特/RFC 应消费 BusinessScheduling 输出 DTO，MES 只按已发布方案落地执行域变化。
10. `/api/business-console/v1/mes/foundation-readiness/**` 是系统管理和数据就绪诊断入口，用于检查主数据、工程资料、供应齐套、质量、设备和条码编码准备状态；它不应作为一线 MES 日常执行菜单的优先入口。日常执行仍从驾驶舱、生产计划、工单、派工、工序执行、报工、质量、入库和追溯等业务任务进入。
11. `/api/business-console/v1/mes/production-plans` 的目标来源是 DemandPlanning 的 demand source/source plan 或等价来源计划；当前 MES surface 仍缺 durable link，列表和转工单链路不能被描述为已消费 DemandPlanning。若上游计划来源缺失导致新转单受阻，BusinessGateway 应把阻塞原因暴露为计划就绪问题（#233/#206 后续补齐）；既有 MES 工单、工序任务、报工、停机、完工入库和追溯接口不因此失效。
12. BusinessERP 后端已实现 Sales Opportunity，但后端契约存在不等于 BusinessGateway/Business Console 可以提前开放商机页面。ERP 商机 facade、OpenAPI 快照、api-client 生成和前端页面仍必须跟随 Business Console 菜单分期和对应 issue，不因服务端已有聚合根而扩大当前菜单范围。

Business Console operationId 使用 lower camelCase，并带 `BusinessConsole` 语义前缀：

| operationId | Route | 用途 |
| --- | --- | --- |
| `listBusinessConsoleSkus` | `GET /api/business-console/v1/master-data/skus` | SKU 列表和筛选。 |
| `createBusinessConsoleSku` | `POST /api/business-console/v1/master-data/skus` | 创建 SKU。 |
| `listBusinessConsoleMasterDataResources` | `GET /api/business-console/v1/master-data/resources` | 查询 UOM、站点、产线、工作中心、设备等基础资源。 |
| `getBusinessConsoleInventoryAvailability` | `GET /api/business-console/v1/inventory/availability` | 查询库存可用量。 |
| `postBusinessConsoleInventoryMovement` | `POST /api/business-console/v1/inventory/movements` | 提交库存移动。 |
| `createBusinessConsoleInventoryCountTask` | `POST /api/business-console/v1/inventory/count-tasks` | 创建盘点任务。 |
| `confirmBusinessConsoleInventoryCountAdjustment` | `POST /api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments` | 确认盘点调整。 |
| `listBusinessConsoleQualityInspectionPlans` | `GET /api/business-console/v1/quality/inspection-plans` | 检验计划列表。 |
| `createBusinessConsoleQualityInspectionRecord` | `POST /api/business-console/v1/quality/inspection-records` | 创建检验记录。 |
| `listBusinessConsoleQualityNcrs` | `GET /api/business-console/v1/quality/ncrs` | NCR 列表。 |
| `submitBusinessConsoleQualityNcrDisposition` | `POST /api/business-console/v1/quality/ncrs/{ncrId}/disposition` | 提交 NCR 处置。 |
| `closeBusinessConsoleQualityNcr` | `POST /api/business-console/v1/quality/ncrs/{ncrId}/close` | 关闭 NCR。 |
| `getBusinessConsoleMesFoundationReadiness` | `GET /api/business-console/v1/mes/foundation-readiness` | MES 基础准备聚合检查；系统管理/数据就绪诊断，不作为日常执行优先入口。 |
| `getBusinessConsoleMesMasterDataReadiness` | `GET /api/business-console/v1/mes/foundation-readiness/master-data` | MasterData 就绪检查。 |
| `getBusinessConsoleMesProductEngineeringReadiness` | `GET /api/business-console/v1/mes/foundation-readiness/product-engineering` | 工艺/BOM/生产版本就绪检查。 |
| `getBusinessConsoleMesSupplyReadiness` | `GET /api/business-console/v1/mes/foundation-readiness/supply` | 供应、齐套和线边供料就绪检查。 |
| `getBusinessConsoleMesQualityReadiness` | `GET /api/business-console/v1/mes/foundation-readiness/quality` | 质量标准和质量状态就绪检查。 |
| `getBusinessConsoleMesEquipmentReadiness` | `GET /api/business-console/v1/mes/foundation-readiness/equipment` | 设备和维护状态就绪检查。 |
| `getBusinessConsoleMesBarcodeNumberingReadiness` | `GET /api/business-console/v1/mes/foundation-readiness/barcode-numbering` | 条码、标签和编码规则就绪检查。 |
| `getBusinessConsoleMesOverview` | `GET /api/business-console/v1/mes/overview` | MES 驾驶舱计数、阻塞和待办。 |
| `listBusinessConsoleMesProductionPlans` | `GET /api/business-console/v1/mes/production-plans` | 生产计划列表；目标来源是 DemandPlanning/source plan，当前仍缺 durable link，新转单受阻不影响既有 MES 工单执行。 |
| `getBusinessConsoleMesProductionPlanReadiness` | `GET /api/business-console/v1/mes/production-plans/{productionPlanId}/readiness` | 生产计划转工单前就绪检查。 |
| `convertBusinessConsoleMesPlanToWorkOrder` | `POST /api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders` | 生产计划转执行工单。 |
| `listBusinessConsoleMesWorkOrders` | `GET /api/business-console/v1/mes/work-orders` | 工单列表。 |
| `getBusinessConsoleMesWorkOrderDetail` | `GET /api/business-console/v1/mes/work-orders/{workOrderId}` | 工单详情和工序任务。 |
| `releaseBusinessConsoleMesWorkOrder` | `POST /api/business-console/v1/mes/work-orders/{workOrderId}/release` | 释放生产工单。 |
| `createBusinessConsoleMesRushWorkOrder` | `POST /api/business-console/v1/mes/work-orders/rush` | 创建急单。 |
| `getBusinessConsoleMesMaterialReadiness` | `GET /api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness` | 工单齐套检查。 |
| `createBusinessConsoleMesMaterialIssueRequest` | `POST /api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests` | 创建领料/备料申请。 |
| `listBusinessConsoleMesMaterialIssueRequests` | `GET /api/business-console/v1/mes/material-issue-requests` | 领料申请列表。 |
| `confirmBusinessConsoleMesLineSideMaterialReceipt` | `POST /api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts` | 确认线边接收。 |
| `listBusinessConsoleMesDispatchTasks` | `GET /api/business-console/v1/mes/dispatch-tasks` | 派工任务列表。 |
| `assignBusinessConsoleMesDispatchTask` | `POST /api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign` | 分派工序任务。 |
| `listBusinessConsoleMesOperationTasks` | `GET /api/business-console/v1/mes/operation-tasks` | 工序执行任务列表。 |
| `startBusinessConsoleMesOperationTask` | `POST /api/business-console/v1/mes/operation-tasks/{operationTaskId}/start` | 工序开工。 |
| `pauseBusinessConsoleMesOperationTask` | `POST /api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause` | 工序暂停。 |
| `resumeBusinessConsoleMesOperationTask` | `POST /api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume` | 工序恢复。 |
| `completeBusinessConsoleMesOperationTask` | `POST /api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete` | 工序完工。 |
| `getBusinessConsoleMesWipSummary` | `GET /api/business-console/v1/mes/wip` | 在制状态汇总。 |
| `listBusinessConsoleMesProductionReports` | `GET /api/business-console/v1/mes/production-reports` | 报工记录列表。 |
| `recordBusinessConsoleMesProductionReport` | `POST /api/business-console/v1/mes/production-reports` | 创建生产报工。 |
| `recordBusinessConsoleMesDefect` | `POST /api/business-console/v1/mes/defects` | 记录制程不良。 |
| `listBusinessConsoleMesRelatedQualityItems` | `GET /api/business-console/v1/mes/related-quality-items` | 关联质量事项列表。 |
| `listBusinessConsoleMesFinishedGoodsReceiptRequests` | `GET /api/business-console/v1/mes/finished-goods-receipt-requests` | 完工入库请求列表。 |
| `createBusinessConsoleMesFinishedGoodsReceiptRequest` | `POST /api/business-console/v1/mes/finished-goods-receipt-requests` | 创建完工入库请求。 |
| `listBusinessConsoleMesDowntimeEvents` | `GET /api/business-console/v1/mes/downtime-events` | 停机事件列表。 |
| `recordBusinessConsoleMesDowntimeEvent` | `POST /api/business-console/v1/mes/downtime-events` | 记录停机事件。 |
| `confirmBusinessConsoleMesDowntimeRecovery` | `POST /api/business-console/v1/mes/downtime-events/{downtimeEventId}/recover` | 确认停机恢复。 |
| `listBusinessConsoleMesShiftHandovers` | `GET /api/business-console/v1/mes/shift-handovers` | 班次交接列表。 |
| `createBusinessConsoleMesShiftHandover` | `POST /api/business-console/v1/mes/shift-handovers` | 创建班次交接。 |
| `acceptBusinessConsoleMesShiftHandover` | `POST /api/business-console/v1/mes/shift-handovers/{handoverId}/accept` | 接收班次交接。 |
| `getBusinessConsoleMesWorkOrderTraceability` | `GET /api/business-console/v1/mes/traceability/work-orders/{workOrderId}` | 按工单追溯。 |
| `getBusinessConsoleMesBatchTraceability` | `GET /api/business-console/v1/mes/traceability/batches/{batchOrSerial}` | 按批次/序列追溯。 |
| `getBusinessConsoleMesMaterialLotTraceability` | `GET /api/business-console/v1/mes/traceability/material-lots/{materialLotId}` | 按物料批次追溯。 |
| `listBusinessConsoleMesCapacityImpacts` | `GET /api/business-console/v1/mes/capacity-impacts` | 产能影响列表。 |
| `runBusinessConsoleMesSchedule` | `POST /api/business-console/v1/mes/schedules/run` | 运行 MES 规则排程过渡入口；#206 后消费 BusinessScheduling 输出，不作为长期 APS 权威。 |

### BusinessGateway Mobile OpenAPI

移动 PDA 前端只直接消费 BusinessGateway 暴露的 `/api/mobile/v1/**` 接口，不直接调用 WMS、Inventory、MES、Quality、Maintenance、IAM 或 FileStorage 的服务 URL。BusinessGateway 复用 PlatformGateway 已验证的 facade 口径，但它属于业务平台聚合入口，不应把 WMS/MES/Inventory 等移动聚合逻辑写入 PlatformGateway。

Mobile OpenAPI 的生成链路固定为：

1. BusinessGateway 通过 FastEndpoints.Swagger 输出 `/swagger/v1/swagger.json`。
2. 导出脚本将 BusinessGateway OpenAPI 快照写入 `frontend/packages/api-client/openapi/business-gateway-mobile.v1.json`。
3. `frontend/packages/api-client/openapi-ts.config.ts` 增加 mobile input，生成到 `frontend/packages/api-client/src/generated/mobile/`，与现有 PlatformGateway generated 文件隔离。
4. `frontend/packages/api-client/src/mobile.ts` 提供 PDA 稳定导出；`src/index.ts` 可重新导出移动端需要的类型、SDK 和 Pinia Colada online query options。
5. PDA app 只从 `@nerv-iip/api-client` 稳定入口消费，不深 import `src/generated/mobile/*`。
6. BusinessGateway 与 PlatformGateway 可以在部署层共用公网域名和反向代理，也可以使用不同 base URL；api-client transport 必须允许 PDA 的 `gatewayBaseUrl` 指向 BusinessGateway。
7. OpenAPI 快照是生成输入，不允许手改；新增或修改 mobile endpoint 时必须先更新 BusinessGateway endpoint、OpenAPI/authorization tests，再导出快照并运行 `pnpm -C frontend generate:api`。

Mobile operationId 使用 lower camelCase，并带 `Mobile` 语义前缀：

| operationId | Route | 用途 |
| --- | --- | --- |
| `getMobileBootstrap` | `GET /api/mobile/v1/bootstrap` | 登录后拉取用户、上下文、权限、设备策略。 |
| `listMobileTasks` | `GET /api/mobile/v1/tasks` | 拉取本人任务、最近任务、异常任务。 |
| `getMobileSyncDelta` | `GET /api/mobile/v1/sync/delta` | 拉取基础数据和任务增量。 |
| `batchMobileOperations` | `POST /api/mobile/v1/operations/batch` | 批量同步 PDA outbox。 |
| `interpretMobileScan` | `POST /api/mobile/v1/scans/interpret` | 在线解释条码含义。 |
| `registerMobileDevice` | `POST /api/mobile/v1/devices/register` | 登记设备或安装实例。 |
| `uploadMobileDiagnostics` | `POST /api/mobile/v1/diagnostics` | 上传诊断摘要。 |

### Console IAM Admin API

Phase 8 已在 PlatformGateway 暴露 Console IAM Admin facade。控制台仍只消费 `/api/console/v1/**`，Gateway 负责 IAM-backed permission enforcement、bearer token 转发和下游错误映射；前端通过 `@nerv-iip/api-client` 的稳定导出消费 generated SDK、Pinia Colada query/mutation options 与类型别名。

Console auth `/api/console/v1/auth/me` 返回的 principal 包含 `permissionCodes`，用于前端提前禁用无权限的 IAM admin 写操作按钮；后端 Gateway/IAM permission enforcement 仍是最终授权边界。

当前 Console IAM operation IDs 固定为：

| operationId | Route | 用途 |
| --- | --- | --- |
| `listConsoleIamUsers` | `GET /api/console/v1/iam/users` | 用户分页列表。 |
| `createConsoleIamUser` | `POST /api/console/v1/iam/users` | 创建用户。 |
| `updateConsoleIamUser` | `PATCH /api/console/v1/iam/users/{userId}` | 更新用户。 |
| `disableConsoleIamUser` | `POST /api/console/v1/iam/users/{userId}/disable` | 禁用用户。 |
| `resetConsoleIamUserPassword` | `POST /api/console/v1/iam/users/{userId}/reset-password` | 重置用户密码。 |
| `listConsoleIamRoles` | `GET /api/console/v1/iam/roles` | 角色分页列表。 |
| `createConsoleIamRole` | `POST /api/console/v1/iam/roles` | 创建角色。 |
| `updateConsoleIamRolePermissions` | `PATCH /api/console/v1/iam/roles/{roleId}/permissions` | 更新角色权限。 |
| `listConsoleIamPermissions` | `GET /api/console/v1/iam/permissions` | 权限 catalog。 |
| `listConsoleIamSessions` | `GET /api/console/v1/iam/sessions` | 会话分页列表。 |
| `revokeConsoleIamSession` | `POST /api/console/v1/iam/sessions/{sessionId}/revoke` | 撤销会话。 |

新增、删除或修改任一 Gateway Console IAM facade endpoint 时，必须同步更新 Gateway OpenAPI operationId 测试、导出 `frontend/packages/api-client/openapi/platform-gateway.v1.json`，再运行 `pnpm -C frontend generate:api` 刷新 generated SDK、types 和 Pinia Colada options。生成 diff 只应保留真实 Gateway 契约变化；不得手改 OpenAPI 快照或 generated 文件来掩盖后端契约缺口。

### Console Log Query API

1. 控制台日志查看属于 PlatformGateway 页面级 API，不属于前端直连观测后端能力。
2. 前端通过生成客户端调用 `/api/console/v1/logs/query`、`/api/console/v1/instances/{instanceKey}/logs` 或 `/api/console/v1/operation-tasks/{operationTaskId}/logs`；这些接口的 `operationId` 建议为 `queryConsoleLogs`、`getConsoleInstanceLogs`、`getConsoleOperationLogs`。
3. Gateway 内部默认接入内置日志归档 profile：先查 `observability` 索引元数据，再通过 File Storage 读取 `.jsonl.gz` chunk；也可以接入 Aspire Dashboard 短期 telemetry API、滚动 JSONL 热文件、后续生产日志后端或客户侧托管平台。OpenAPI DTO 必须保持平台中立，不暴露后端查询语言、内部 API、tenant header、数据源 URL 或凭据。
4. 查询请求必须包含受控过滤条件：`from`、`to`、`limit`、`cursor`、`level`、`service`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId` 和 `text`。Gateway 负责把这些条件映射为后端查询。
5. 查询响应建议包含 `items`、`nextCursor`、`partial` 和 `backendStatus`。单条日志建议包含 `timestamp`、`level`、`service`、`message`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId`、`labels`、`fields`、`source`。`source` 只表达 `hotFile`、`archiveChunk`、`dashboard`、`externalBackend` 等平台中立来源，不暴露实际存储路径或对象 key。
6. 日志接口必须执行 IAM 鉴权、组织与环境隔离、最大时间窗口、最大返回条数、速率限制和敏感字段脱敏。OpenAPI 测试至少覆盖越权过滤、超大窗口拒绝、分页和脱敏。
7. 实时日志 tail 如果落地，应新增 SSE 或 WebSocket 契约，并继续由 Gateway 代理后端查询；普通页面不得直接打开观测后端连接。
8. OpenAPI 不暴露内部 `LogChunk`、`LogEntryIndex` 或 File Storage object key；前端只能看到可展示日志条目和分页游标。
9. `LogChunk` 与 `LogEntryIndex` 只是 Gateway 内部定位数据的索引模型，不属于前端契约；索引字段变化不应造成 Console API breaking change。

### 前端责任

1. 前端通过 Hey API 从 OpenAPI 生成 types、sdk、client 与 Pinia Colada 查询、变更函数。
2. 页面和 composables 不直接拼接 URL，也不绕过生成客户端手写重复网络层。
3. 后端 SDK 和 OpenAPI 变更可以触发 `frontend/packages/api-client` 机械生成，但这不授权新增控制台视图。若后端契约暂不被当前控制台使用，应保持生成客户端变更可追溯，并用生成契约测试覆盖。

## 推荐目录结构

```text
frontend/packages/api-client/
  openapi-ts.config.ts
  openapi/
    platform-gateway.v1.json
    business-gateway-console.v1.json
    business-gateway-mobile.v1.json
  src/
    generated/
      business-console/
      mobile/
    transport/
      base-url.ts
      auth.ts
      error.ts
      client-config.ts
    console.ts
    business-console.ts
    mobile.ts
    index.ts
```

### generated

- 只放代码生成文件。
- 推荐包含 client.gen.ts、sdk.gen.ts、types.gen.ts，以及 Colada 查询与变更生成文件。
- 不允许手改。
- Business Console generated 文件放在 `src/generated/business-console/`，Mobile generated 文件放在 `src/generated/mobile/`，避免与 PlatformGateway Console generated 文件在同一目录内发生 operationId 或类型名冲突。

### openapi

- 保存由脚本从 Gateway 导出的版本化 OpenAPI 快照。
- `platform-gateway.v1.json` 对应 PlatformGateway 当前主版本控制台 API。
- `business-gateway-console.v1.json` 对应 BusinessGateway 当前主版本业务控制台 API。
- `business-gateway-mobile.v1.json` 对应 BusinessGateway 当前主版本移动 PDA API。
- 快照更新必须能追溯到后端 Endpoint、测试和文档变化。
- 快照是生成产物输入，格式以导出脚本输出为准，不纳入 Vite+ formatter 检查。

### transport

- base-url.ts
- auth.ts
- error.ts
- client-config.ts

职责是统一 baseURL、认证头、错误归一化和请求级策略。

### index.ts

- 作为稳定导出入口。
- 应用层只消费这里，而不消费 generated 深层路径。
- `business-console.ts` 是业务控制台专用稳定导出入口；`frontend/apps/business-console` 不得绕过它深 import generated。
- `mobile.ts` 是 PDA 专用稳定导出入口；`index.ts` 可以重新导出它，但页面不得绕过 `mobile.ts` 深 import generated。

## 生成链路

1. 后端更新接口并同步 OpenAPI。
2. 使用 `scripts/export-gateway-openapi.ps1` 导出 Gateway OpenAPI 快照。
3. 前端运行 `pnpm -C frontend generate:api`，通过 Vite+ workspace task 调用 Hey API 生成命令。
4. api-client 更新 generated 与 transport 组合导出。
5. console 与 business-console 应用、共享 composables 通过稳定入口消费新的 sdk/query/mutation。
6. 变更涉及 breaking change 时，必须同步更新对应页面、组合函数和文档。
7. OpenAPI 导出和 api-client 写入属于 `generate` 类脚本副作用，必须按 docs/architecture/script-automation-governance.md 声明写入路径、日志、服务启动和清理策略；纯 `verify` 脚本不得隐式写生成产物。

BusinessGateway console API 引入后，生成链路增加 `business-gateway-console.v1.json` 作为第二个 OpenAPI 输入；BusinessGateway mobile API 引入后，再增加 `business-gateway-mobile.v1.json`。这些输入仍由同一个 `frontend/packages/api-client` 包输出，但 generated 目录和稳定导出入口必须与 PlatformGateway Console 隔离。

第三迭代生成配置固定使用：

1. `@hey-api/client-fetch` 生成 fetch client。
2. `@hey-api/typescript` 生成 TypeScript DTO。
3. `@hey-api/sdk` 生成按 `operationId` 命名的调用函数。
4. `@pinia/colada` 生成查询和变更 options。

生成入口固定为 `frontend/packages/api-client/openapi-ts.config.ts`，应用侧只从 `@nerv-iip/api-client` 稳定入口消费，不从 `src/generated` 深层路径导入。第三阶段总验收入口为 `scripts/verify-third-slice-console.ps1`，该脚本会串起 Gateway OpenAPI 导出、api-client 生成、前端 typecheck/test/build；在脚本治理迁移中，该入口必须显式声明混合 `verify`/`generate` 副作用，或拆成受控 generate step 与纯验证 step。

## 使用规则

1. 页面组件和 composables 不直接写 fetch 或 axios URL。
2. 页面优先消费生成的 query/mutation helpers。
3. 少量页面特有参数整理可以放在 src/api/领域名/adapters.ts 中。
4. api-client 只做契约、transport 和稳定导出，不放业务视图逻辑。
5. 需要轮询的服务端状态通过 Pinia Colada query options 和官方 auto-refetch 插件表达，不在组件里手写 `setInterval`。
6. Design System 冻结前，不因后端契约变更新增页面、视觉组件、组件库迁移或样式 token；相关规划见 docs/architecture/frontend-design-system-planning.md。
7. 生成客户端可以承载 JSON/text 契约字段，但字段语义、版本和兼容策略必须在后端契约或服务文档中可追踪。
8. FileStorage 公开 DTO 的源码事实当前位于 `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage`；SDK/Web 边界复用同一 DTO，公开响应不得包含 `objectKey` 或 `object_key`。

## 版本与变更管理

1. OpenAPI 变更必须在 PR 中可见。
2. 生成文件允许较大 diff，但 hand-written transport 层需要保持最小、稳定。
3. 破坏性改动必须提升主版本，并同步更新前端消费点、SDK、迁移说明与文档。
4. 不允许前端在契约未更新时通过手写 DTO 临时绕过。
5. SDK 模块新增或行为变化必须同步更新 docs/architecture/platform-sdk-baseline.md。

## 反模式

1. 在页面里直接手写 URL、headers 和重复错误处理。
2. 在 generated 目录里补自定义逻辑。
3. 让多个包各自维护不同版本的相同接口类型。
4. 让 Gateway 返回未进入 OpenAPI 的隐式接口。
5. 让 SDK 变成服务发现中心、权限事实源、审计事实源、通知事实源或服务端领域模型副本。
6. 让前端直接访问 Aspire Dashboard、第三方观测后端或客户侧日志平台。
7. 把后端基础阶段的 OpenAPI/api-client 机械变更扩大成前端页面或 Design System 实施。
