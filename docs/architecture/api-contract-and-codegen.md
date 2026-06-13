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
11. `/api/business-console/v1/workbench/summary` 是 Business Console 数字化工作台聚合入口，聚合 KPI、BusinessApproval 待办、Notification 消息/任务、IndustrialTelemetry 预警以及 MES/Quality 摘要。该 facade 必须按当前 principal 对每个来源分别执行 permission check；无权限来源只返回 source status，不返回对象名称、金额、消息标题或其它敏感字段。
12. `/api/business-console/v1/mes/production-plans` 的目标来源是 DemandPlanning 的 demand source/source plan 或等价来源计划；MES 计划转工单命令已持久化 source system、source document type/id、source demand reference 和 UOM 快照到 `work_orders`，并通过 production plans、work order detail 和 traceability surface 回显。MES 不读取 DemandPlanning schema，不建立跨 schema FK；既有 MES 工单、工序任务、报工、停机、完工入库和追溯接口不因此失效。
13. BusinessERP 后端已实现 Sales Opportunity，但后端契约存在不等于 BusinessGateway/Business Console 可以提前开放商机页面。ERP 商机 facade、OpenAPI 快照、api-client 生成和前端页面仍必须跟随 Business Console 菜单分期和对应 issue，不因服务端已有聚合根而扩大当前菜单范围。
14. #207 BusinessGateway 设备运行事实 facade 已进入 BusinessGateway Console OpenAPI 快照，`frontend/packages/api-client/openapi/business-gateway-console.v1.json` 与 `frontend/packages/api-client/src/generated/business-console/` 已通过 `pnpm -C frontend generate:api` 刷新，Business Console 只从 `@nerv-iip/api-client` 稳定入口消费。
15. `/api/business-console/v1/search` 是 #271 全局对象搜索后端 facade，供后续 Cmd/Ctrl+K 面板消费。该 facade 从当前 bearer token 的组织/环境上下文读取 scope，并对每个对象来源分别执行 IAM permission check；无权限来源不得查询下游，也不得返回对象标题、状态或摘要。首批支持 MES 工单、MasterData SKU 和 IndustrialTelemetry 当前报警；Inventory batch/lot 与 equipment device 因当前缺少可增量搜索下游查询，必须返回 `unsupported` source/type status，不能静默截断。当前已连接来源的搜索词匹配语义是 `source-window`：Gateway 先读取每个来源的首个请求窗口，再在窗口内匹配 `q`，响应中的 `matchScope` 和 `matchScopeDescription` 必须显式暴露该限制；后续若下游接口支持 `q`/keyword 下推，再调整该语义和契约说明。
16. MES list facade endpoints（工单、生产计划、工序任务、派工、WIP、报工、领料、完工入库、停机、交接、产能影响和相关质量项）支持服务端 `keyword` 以及适用的 `workCenterId`、`shiftId`、`deviceAssetId` 过滤；生产计划额外支持 `source` 和 `readinessStatus`。这些过滤必须在后端 `total` 计算前应用，Business Console 不得在服务端分页结果上叠加误导性的当前页关键字搜索。
17. BusinessGateway Quality list facade endpoints（`/quality/ncrs` 与 `/quality/inspection-plans`）支持服务端 `keyword` 定位；NCR 仅按 `NcrId`、`NcrCode`、`SourceDocumentId` 匹配，检验方案仅按 `InspectionPlanId`、`PlanCode` 匹配。该过滤必须在下游 Quality 服务的 `total` 计算前应用，用于 MES/Quality 跨页跳转定位和检验方案上下文穿透；不得把 SKU、缺陷原因、批次、工位、设备或文档类型等高基数字段混入定位语义。
18. BusinessGateway ERP list facade endpoints 必须对所有已开放创建入口提供对应列表查询。RFQ、商机、报价、发货单和会计凭证列表支持服务端 `status`、`keyword`、`skip`、`take`，且过滤必须在下游 BusinessERP 的 `total` 计算前应用；BusinessGateway 仅透传当前用户组织/环境上下文、internal token 和读取权限，不在 facade 层补业务规则。RFQ 和报价按对应聚合状态枚举过滤；商机当前仅接受 `open`；发货单和会计凭证当前没有持久化多状态，`status` 仅接受固定单值 `released` / `posted`，其它值返回空集。
19. BusinessGateway Approval facade endpoints 支撑 Business Console 审批中心的模板、流程实例、审批记录和委托设置。`/approval/templates` 与 `/approval/tasks` 返回 `{ items,total }` 并支持 `skip/take`；`/approval/chains` 支持 `status`、`startedBy`、`sourceService`、`documentType`、`documentId`、`skip`、`take`；`/approval/decisions` 支持 `chainId`、`actorType`、`actorRef`、`decision`、`documentType`、`documentId`、`skip`、`take`；`/approval/delegations` 支持委托查询、创建和撤销。BusinessGateway 只做 IAM permission check、上下文透传和 internal token 下游调用，不在 facade 层解释审批规则；BusinessApproval 仍不接管 Ops 运维审批。
20. BusinessGateway BarcodeLabel facade 已补齐条码规则、标签模板、打印批次和扫码记录的服务端分页读面。`/barcode/rules`、`/barcode/print-batches`、`/barcode/templates`、`/barcode/scans` 均返回 `{ items,total }` 或等价分页响应，并在下游 BarcodeLabel 服务的 `total` 计算前应用 `skip/take` 与支持的过滤条件。
21. BusinessGateway WMS facade 已补齐 PDA 一线作业所需的 #374 P0 读面：`/wms/picking-tasks`、`/wms/putaway-tasks`、`/wms/count-executions` 均返回 `{ items,total }`，支持 `skip/take`、`status`、`keyword` 和库位过滤；`picking/putaway` 接收 `operatorUserId` 预留参数，但当前 WMS 后端尚无 assigned operator 字段，传入非空值时返回空集以避免伪造个人任务。PDA “我的任务”在 #374 P1 assigned-operator 字段/收件箱 facade 落地前不得默认发送该参数。
22. BusinessGateway IndustrialTelemetry/Equipment facade 的 tags、alarm-rules、alarms、equipment alarms 列表均使用服务端分页响应，并支持设备、状态等下游已暴露过滤；BusinessGateway 不在当前页结果上二次模拟全量过滤。
23. BusinessGateway Maintenance facade 已补齐点检列表、备件列表/创建，以及工单/保养计划分页读面。停机归因仍由 MES downtime execution facts 与 Maintenance downtime reason/availability window 各自拥有，不在 Gateway facade 层合并成新的持久事实。
24. BusinessGateway MasterData facade 已补齐通用资源 detail/update/disable/enable、typed list 字段、reference-data `codeSet` 过滤、workshop/team-member 接口、面向工人选择器的 IAM worker directory facade，以及 BusinessPartner 多角色和 `taxId` 字段。`listBusinessConsoleMasterDataResources` 支持 `parentCode`、`siteCode`、`lineCode`、`workCenterCode`、`category`、`partnerType`、`keyword`、`departmentCode`、`shiftCode`、`userId`、`skillCode` 和 `all=true` 查询；列表返回包含部门 `parentDepartmentCode`、班组 `departmentCode`/`shiftCode`、人员技能 `userId`/`skillCode`/`skillLevel`/`effectiveFrom`/`effectiveTo`、UoM conversion `fromUomCode`/`toUomCode`/`factor`/`offset`/`effectiveFrom` 等 typed 字段。通用 update 支持 Department 改挂上级、Team 改挂部门/班次、Shift 调整起止时间和 paid minutes、WorkCalendar 工作时间/节假日/例外日明细替换，以及 UoM conversion 因子/偏移/精度/舍入策略更新与停用/启用；新增 `listBusinessConsolePersonnelSkillMatrix` 支持按工人/技能聚合人员技能矩阵。BusinessGateway 只透传 MasterData lifecycle 请求和 IAM 权限检查，不在 facade 层重写主数据生命周期；worker directory 使用 internal service token 读取 IAM 最小 worker DTO，不要求业务用户具备 Console IAM Admin 权限。IAM 当前没有真实工号字段，worker directory 只保证 `userId` 和可读 `displayName`，`employeeNo` 可为空。
25. BusinessGateway MasterData code-rule facade 已补齐编码规则配置页后端能力：`/master-data/code-rules` 列出当前规则，`/master-data/code-rules/{ruleKey}` 返回当前定义和版本审计，`/master-data/code-rules/{ruleKey}/versions` 创建版本化配置，`/master-data/code-rules/{ruleKey}/preview` 用候选 segments 生成样例编码。版本化配置由 MasterData `code_rule_versions` 留审计和生效边界，立即生效版本同步推进当前 `code_rules` 定义，scheduled 版本由 MasterData 后台任务在 `effectiveFromUtc` 到期后晋升；同一规则只有当前定义版本保持 active，旧版本为 superseded；预览不消耗持久流水。
26. BusinessGateway ProductEngineering facade 已补齐工程资料 Phase 1 读面。文档、工程物料和 ECO/ECN 提供分页 list 与 detail；EBOM、MBOM 和 Routing 提供 code/revision detail，列表也返回组件行、物料行、配方行或工序明细；文档注册可携带可选 `itemCode`，列表支持 `itemCode` 与 `documentType` 过滤。Gateway 只做 IAM permission check、上下文透传和 internal token 下游调用，不在 facade 层补工程版本规则；状态枚举继续使用后端 `Draft/Published/Archived` 字符串口径。

Business Console operationId 使用 lower camelCase，并带 `BusinessConsole` 语义前缀：

| operationId | Route | 用途 |
| --- | --- | --- |
| `listBusinessConsoleSkus` | `GET /api/business-console/v1/master-data/skus` | SKU 列表和筛选。 |
| `createBusinessConsoleSku` | `POST /api/business-console/v1/master-data/skus` | 创建 SKU。 |
| `listBusinessConsoleMasterDataResources` | `GET /api/business-console/v1/master-data/resources` | 查询 UOM、站点、产线、工作中心、设备、部门、班组和人员技能等基础资源，支持服务端过滤和 `all=true` 全量模式。 |
| `getBusinessConsoleMasterDataResourceDetail` | `GET /api/business-console/v1/master-data/resources/{resourceType}/{code}` | 查询 SKU、UOM、伙伴、站点、车间、产线、工作中心、设备和参考数据详情。 |
| `updateBusinessConsoleMasterDataResource` | `PATCH /api/business-console/v1/master-data/resources/{resourceType}/{code}` | 更新通用 MasterData 资源字段。 |
| `disableBusinessConsoleMasterDataResource` | `POST /api/business-console/v1/master-data/resources/{resourceType}/{code}/disable` | 停用通用 MasterData 资源。 |
| `enableBusinessConsoleMasterDataResource` | `POST /api/business-console/v1/master-data/resources/{resourceType}/{code}/enable` | 启用通用 MasterData 资源。 |
| `listBusinessConsolePersonnelSkillMatrix` | `GET /api/business-console/v1/master-data/personnel-skills/matrix` | 查询人员技能矩阵，支持按工人或技能过滤。 |
| `listBusinessConsoleCodeRules` | `GET /api/business-console/v1/master-data/code-rules` | 查询当前编码规则定义列表。 |
| `getBusinessConsoleCodeRule` | `GET /api/business-console/v1/master-data/code-rules/{ruleKey}` | 查询单个编码规则当前定义和版本审计。 |
| `createBusinessConsoleCodeRuleVersion` | `POST /api/business-console/v1/master-data/code-rules/{ruleKey}/versions` | 创建编码规则版本化配置，记录生效边界和审计。 |
| `previewBusinessConsoleCodeRule` | `POST /api/business-console/v1/master-data/code-rules/{ruleKey}/preview` | 用候选 segments 生成样例编码，不消耗持久流水。 |
| `createBusinessConsoleBusinessPartner` | `POST /api/business-console/v1/master-data/partners` | 创建业务伙伴，支持多角色和可选 tax id。 |
| `listBusinessConsoleWorkshops` | `GET /api/business-console/v1/master-data/workshops` | 查询车间列表。 |
| `createBusinessConsoleWorkshop` | `POST /api/business-console/v1/master-data/workshops` | 创建车间。 |
| `listBusinessConsoleWorkers` | `GET /api/business-console/v1/master-data/workers` | 分页/关键字查询 IAM worker directory 最小读面，用于班组成员、人员技能和派工选人。 |
| `listBusinessConsoleTeamMembers` | `GET /api/business-console/v1/master-data/team-members` | 查询班组成员关系。 |
| `addBusinessConsoleTeamMember` | `POST /api/business-console/v1/master-data/team-members` | 添加班组成员关系。 |
| `removeBusinessConsoleTeamMember` | `DELETE /api/business-console/v1/master-data/team-members/{teamCode}/{userId}` | 移除班组成员关系。 |
| `getBusinessConsoleInventoryAvailability` | `GET /api/business-console/v1/inventory/availability` | 查询库存可用量。 |
| `postBusinessConsoleInventoryMovement` | `POST /api/business-console/v1/inventory/movements` | 提交库存移动。 |
| `createBusinessConsoleInventoryCountTask` | `POST /api/business-console/v1/inventory/count-tasks` | 创建盘点任务。 |
| `confirmBusinessConsoleInventoryCountAdjustment` | `POST /api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments` | 确认盘点调整。 |
| `listBusinessConsoleErpRequestsForQuotation` | `GET /api/business-console/v1/erp/procurement/rfqs` | RFQ 列表，支持服务端状态、关键字和分页过滤。 |
| `listBusinessConsoleErpOpportunities` | `GET /api/business-console/v1/erp/sales/opportunities` | 销售商机列表，支持服务端状态、关键字和分页过滤。 |
| `listBusinessConsoleErpQuotations` | `GET /api/business-console/v1/erp/sales/quotations` | 报价列表，支持服务端状态、关键字和分页过滤。 |
| `listBusinessConsoleErpDeliveryOrders` | `GET /api/business-console/v1/erp/sales/delivery-orders` | 发货单列表，支持服务端关键字和分页过滤；`status` 当前仅接受 `released`。 |
| `listBusinessConsoleErpJournalVouchers` | `GET /api/business-console/v1/erp/finance/vouchers` | 会计凭证列表，支持服务端关键字和分页过滤；`status` 当前仅接受 `posted`。 |
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
| `getBusinessConsoleEquipmentOverview` | `GET /api/business-console/v1/equipment/overview` | 设备运行看板聚合当前状态、报警数和未来窗口可用性。 |
| `getBusinessConsoleEquipmentDevice` | `GET /api/business-console/v1/equipment/devices/{deviceAssetId}` | 设备运行详情，聚合 current-state 和设备可用性窗口。 |
| `getBusinessConsoleEquipmentAvailability` | `GET /api/business-console/v1/equipment/availability` | 查询 IndustrialTelemetry 与 Maintenance 合并后的设备可用性窗口。 |
| `listBusinessConsoleEquipmentAlarms` | `GET /api/business-console/v1/equipment/alarms` | 查询当前工业报警列表。 |
| `listBusinessConsoleTelemetryTags` | `GET /api/business-console/v1/telemetry/tags` | 查询采集 tag 映射列表，返回分页总数。 |
| `listBusinessConsoleTelemetryAlarmRules` | `GET /api/business-console/v1/telemetry/alarm-rules` | 查询 IndustrialTelemetry 报警规则阈值配置。 |
| `createOrUpdateBusinessConsoleTelemetryAlarmRule` | `POST /api/business-console/v1/telemetry/alarm-rules` | 创建或更新 IndustrialTelemetry 报警规则阈值配置。 |
| `listBusinessConsoleTelemetryAlarms` | `GET /api/business-console/v1/telemetry/alarms` | 查询报警事件列表，返回分页总数。 |
| `queryBusinessConsoleTelemetryOee` | `GET /api/business-console/v1/telemetry/oee` | 基于当前设备状态事实查询窗口 P0 OEE 聚合；availability 按状态持续时间计算，performance/quality 当前为估算占位，响应标志在 P0 期间保持 true（无状态数据窗口下数值为 0 但仍非真实测量值）。 |
| `listBusinessConsoleMaintenanceInspections` | `GET /api/business-console/v1/maintenance/inspections` | 查询点检记录列表。 |
| `listBusinessConsoleMaintenanceSpareParts` | `GET /api/business-console/v1/maintenance/spare-parts` | 查询维修备件需求列表。 |
| `createBusinessConsoleMaintenanceSparePart` | `POST /api/business-console/v1/maintenance/spare-parts` | 创建维修备件需求行。 |
| `listBusinessConsoleBarcodeRules` | `GET /api/business-console/v1/barcode/rules` | 查询条码规则列表。 |
| `listBusinessConsoleBarcodePrintBatches` | `GET /api/business-console/v1/barcode/print-batches` | 查询标签打印批次列表。 |
| `listBusinessConsoleBarcodeScans` | `GET /api/business-console/v1/barcode/scans` | 查询扫码记录列表。 |
| `searchBusinessConsoleObjects` | `GET /api/business-console/v1/search` | 全局对象搜索后端 facade，按读时权限过滤 MES 工单、SKU 和当前报警，并显式返回 unsupported 类型状态。 |
| `listBusinessConsoleWmsInboundOrders` | `GET /api/business-console/v1/wms/inbound-orders` | 查询 WMS 收货/入库列表，并可按当前 scope 返回 Inventory availability context/sourceStatus。 |
| `listBusinessConsoleWmsPutawayTasks` | `GET /api/business-console/v1/wms/putaway-tasks` | 查询 WMS 上架任务列表，支持服务端分页、状态、库位和预留 `operatorUserId` 参数；当前传入非空操作员会返回空集。 |
| `listBusinessConsoleWmsOutboundOrders` | `GET /api/business-console/v1/wms/outbound-orders` | 查询 WMS 出库/拣货/复核/发货列表。 |
| `listBusinessConsoleWmsPickingTasks` | `GET /api/business-console/v1/wms/picking-tasks` | 查询 WMS 拣货任务列表，支持服务端分页、状态、库位和预留 `operatorUserId` 参数；当前传入非空操作员会返回空集。 |
| `listBusinessConsoleWmsCountExecutions` | `GET /api/business-console/v1/wms/count-executions` | 查询 WMS 盘点执行列表，支持服务端分页、状态和库位过滤。 |
| `listBusinessConsoleWmsWcsTasks` | `GET /api/business-console/v1/wms/wcs-tasks` | 查询 WCS 任务状态列表。 |
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

### Console FileStorage API

PlatformGateway 已暴露 FileStorage 管理 facade，用于主平台 Console 和通用 `FileUpload` 接线。控制台仍只消费 `/api/console/v1/files/**`；Gateway 负责 IAM-backed permission enforcement、internal service token 下游调用、`ResponseData<T>` 包装和传输 URL 代理路径重写。公开响应不得包含 FileStorage 内部 `objectKey`/`object_key`，也不得把 MinIO/S3 或其它对象存储直连 URL 透给前端。

当前 Console FileStorage operation IDs 固定为：

| operationId | Route | Permission | 用途 |
| --- | --- | --- | --- |
| `createConsoleFileUploadSession` | `POST /api/console/v1/files/upload-sessions` | `files.upload` | 创建上传会话并返回受控上传指令。 |
| `completeConsoleFileUploadSession` | `POST /api/console/v1/files/upload-sessions/{uploadSessionId}/complete` | `files.upload` | 完成上传会话并返回文件元数据。 |
| `listConsoleFiles` | `GET /api/console/v1/files` | `files.read` | 查询文件元数据列表，支持 purpose、uploader、created range、status、skip/take。 |
| `getConsoleFileMetadata` | `GET /api/console/v1/files/{fileId}` | `files.read` | 读取文件元数据。 |
| `createConsoleFileDownloadGrant` | `POST /api/console/v1/files/{fileId}/download-grants` | `files.download-grants.create` | 创建短期下载授权并返回受控下载指令。 |
| `getConsoleTusUploadOffset` | `HEAD /api/console/v1/files/tus/{uploadSessionId}` | `files.upload` | 代理 tus offset 查询。 |
| `patchConsoleTusUpload` | `PATCH /api/console/v1/files/tus/{uploadSessionId}` | `files.upload` | 代理 tus 字节追加，不暴露 FileStorage 服务 URL。 |
| `downloadConsoleFileGrantContent` | `GET /api/console/v1/files/download-grants/{downloadGrantId}/content` | `files.read` | 代理 download grant content，不暴露对象存储直连 URL。 |

新增、删除或修改任一 Gateway Console FileStorage facade endpoint 时，必须同步更新 Gateway OpenAPI operationId 测试、导出 `frontend/packages/api-client/openapi/platform-gateway.v1.json`，再运行 `pnpm -C frontend generate:api` 刷新 generated SDK、types 和 Pinia Colada options。OpenAPI 快照和 generated api-client 不允许手改。

### Console Log Query API

1. 控制台日志查看属于 PlatformGateway 页面级 API，不属于前端直连观测后端能力。
2. 前端通过生成客户端调用 `POST /api/console/v1/logs/query`；该接口的 `operationId` 固定为 `queryConsoleLogs`。后续可以新增 `/api/console/v1/instances/{instanceKey}/logs` 或 `/api/console/v1/operation-tasks/{operationTaskId}/logs`，operationId 分别使用 `getConsoleInstanceLogs`、`getConsoleOperationLogs`。
3. Gateway 内部当前接入 VictoriaLogs：通过 `Nerv.IIP.Observability` client 把平台过滤条件映射为 LogsQL，并调用 `/select/logsql/query`。后续也可以接入内置日志归档 profile、Aspire Dashboard 短期 telemetry API、滚动 JSONL 热文件或客户侧托管平台。OpenAPI DTO 必须保持平台中立，不暴露后端查询语言、内部 API、tenant header、数据源 URL 或凭据。
4. 查询请求必须包含受控过滤条件：`from`、`to`、`limit`、`cursor`、`level`、`service`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId` 和 `text`。Gateway 负责把这些条件映射为后端查询。
5. 查询响应建议包含 `items`、`nextCursor`、`partial` 和 `backendStatus`。单条日志建议包含 `timestamp`、`level`、`service`、`message`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId`、`labels`、`fields`、`source`。`source` 只表达 `hotFile`、`archiveChunk`、`dashboard`、`externalBackend` 等平台中立来源，不暴露实际存储路径或对象 key。
6. 日志接口必须执行 IAM 鉴权、组织与环境隔离、最大时间窗口、最大返回条数、速率限制和敏感字段脱敏。当前 `queryConsoleLogs` 需要 `observability.logs.read`，最大窗口为 24 小时，最大 `limit` 为 200；当 VictoriaLogs 被部署配置关闭时，Gateway 返回明确的 501 功能未启用响应，不尝试连接默认后端地址。
7. 实时日志 tail 如果落地，应新增 SSE 或 WebSocket 契约，并继续由 Gateway 代理后端查询；普通页面不得直接打开观测后端连接。
8. OpenAPI 不暴露内部 `LogChunk`、`LogEntryIndex` 或 File Storage object key；前端只能看到可展示日志条目和分页游标。
9. `LogChunk` 与 `LogEntryIndex` 只是 Gateway 内部定位数据的索引模型，不属于前端契约；索引字段变化不应造成 Console API breaking change。

当前 Console Log Query operation IDs 固定为：

| operationId | Route | Permission | 用途 |
| --- | --- | --- | --- |
| `queryConsoleLogs` | `POST /api/console/v1/logs/query` | `observability.logs.read` | 查询 VictoriaLogs-backed 集中日志，按 service、correlationId、traceId、time range 和 level 过滤。 |

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
8. CI 使用 `scripts/verify-openapi-client-drift.ps1` 作为契约漂移门禁：重新导出 PlatformGateway 与 BusinessGateway OpenAPI 快照，运行 `pnpm -C frontend generate:api`，再对 `frontend/packages/api-client/openapi/*.v1.json` 和 `frontend/packages/api-client/src/generated/**` 执行 git status/diff 检查；若提交的快照或生成客户端不同步，PR 必须失败并显示差异。

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
