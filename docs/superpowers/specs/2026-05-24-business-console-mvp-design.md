# Business Console MVP 设计

## 背景

BusinessMasterData、Inventory、Quality 和 MES 后端服务已存在于仓库中，并记录在
`docs/architecture/implementation-readiness.md`。#166 至 #169 尚缺的架构部分是业务前端入口、
业务 BFF（后端服务前端）以及从 OpenAPI 到 api-client 的生成链路。

ADR 0012 明确要求行业业务页面不得进入平台主控制台。当前的
`frontend/apps/console/src/pages/business/index.vue` 只是状态页面，不维护业务事实。即将建设的
SKU 维护、库存可用量、库存移动、库存盘点、检验、NCR、工单和排程页面是真实的业务 CRUD
（增删改查）与工作流界面。因此，它们必须使用业务应用入口和业务网关，而不是扩展
PlatformGateway 或主控制台。

## 决策

通过新的 `frontend/apps/business-console` 应用和新的 `backend/gateway/BusinessGateway` BFF
（后端服务前端）实现 Business Console MVP。

BusinessGateway 负责 `/api/business-console/v1/**` 下的页面级业务 facade endpoint（门面端点）。
它对控制台用户进行身份认证，向 IAM 查询所需权限，并使用内部服务令牌调用业务服务。它不负责
业务事实、持久化、排程规则、库存计算、检验处置规则或 MES 执行规则。

前端只使用为 BusinessGateway 生成的 `@nerv-iip/api-client` 导出。业务页面不得直接调用
`backend/services/Business` 服务 URL，也不得通过 deep import（深层导入）使用生成文件。

## 目标

1. 在交付首个业务 CRUD（增删改查）/工作流控制台的同时，保持 ADR 0012 完整不变。
2. 增加完整的 BusinessGateway OpenAPI 导出与 api-client 生成链路。
3. 为 #166、#167、#168 和 #169 提供小而真实的纵向切片。
4. 复用现有 Calm Control Plane 设计系统与应用壳层基础组件。
5. 保持 BusinessGateway 轻薄：仅负责授权、请求整形、响应整形和下游代理。

## 非目标

1. 不将业务页面移入 `frontend/apps/console`。
2. 不向 PlatformGateway 增加 MasterData、Inventory、Quality 或 MES facade endpoint（门面端点）。
3. 不在 BusinessGateway 与业务服务之间引入共享数据库表、跨 schema 外键或服务实现引用。
4. 不为 #169 构建甘特图。
5. 除非出现具体阻塞，否则本 MVP 不抽取 `frontend/packages/auth`。身份认证可以先采用与现有
   控制台实现一致的应用内代码。

## 架构

```text
frontend/apps/business-console
  -> @nerv-iip/api-client business-console stable exports
  -> BusinessGateway /api/business-console/v1/**
  -> IAM permission check
  -> BusinessMasterData / Inventory / Quality / MES internal APIs
```

BusinessGateway 为四个 MVP 服务使用 HTTP client（客户端）：

1. MasterData，默认本地基准 URL 为 `http://localhost:5107`。
2. Inventory，默认本地基准 URL 为 `http://localhost:5109`。
3. Quality，默认本地基准 URL 为 `http://localhost:5110`。
4. MES，默认本地基准 URL 为 `http://localhost:5111`。

BusinessGateway 还以与 PlatformGateway 相同的方式使用 IAM 身份认证与授权客户端。面向用户的请求
将用户的 bearer token（持有者令牌）传递给 BusinessGateway。由于这些服务 API 受内部服务保护，
下游业务服务调用使用 `IInternalServiceTokenProvider`。IAM 仍是最终权限来源。

## 后端 BFF 契约

BusinessGateway endpoint（端点）使用带 `BusinessConsole` 前缀、稳定的 lower camel case
（小驼峰命名）operation ID，例如：

| operationId | 路由 | 下游服务 |
| --- | --- | --- |
| `listBusinessConsoleSkus` | `GET /api/business-console/v1/master-data/skus` | 筛选为 SKU 的 MasterData 资源列表。 |
| `createBusinessConsoleSku` | `POST /api/business-console/v1/master-data/skus` | MasterData 创建 SKU。 |
| `listBusinessConsoleMasterDataResources` | `GET /api/business-console/v1/master-data/resources` | MasterData 资源列表。 |
| `getBusinessConsoleInventoryAvailability` | `GET /api/business-console/v1/inventory/availability` | Inventory 可用量查询。 |
| `postBusinessConsoleInventoryMovement` | `POST /api/business-console/v1/inventory/movements` | Inventory 库存移动过账。 |
| `createBusinessConsoleInventoryCountTask` | `POST /api/business-console/v1/inventory/count-tasks` | Inventory 盘点任务创建。 |
| `confirmBusinessConsoleInventoryCountAdjustment` | `POST /api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments` | Inventory 盘点调整。 |
| `listBusinessConsoleQualityInspectionPlans` | `GET /api/business-console/v1/quality/inspection-plans` | Quality 检验计划。 |
| `createBusinessConsoleQualityInspectionRecord` | `POST /api/business-console/v1/quality/inspection-records` | Quality 检验记录。 |
| `listBusinessConsoleQualityNcrs` | `GET /api/business-console/v1/quality/ncrs` | Quality NCR 列表。 |
| `submitBusinessConsoleQualityNcrDisposition` | `POST /api/business-console/v1/quality/ncrs/{ncrId}/disposition` | Quality NCR 处置。 |
| `closeBusinessConsoleQualityNcr` | `POST /api/business-console/v1/quality/ncrs/{ncrId}/close` | Quality NCR 关闭。 |
| `listBusinessConsoleMesWorkOrders` | `GET /api/business-console/v1/mes/work-orders` | MES 工单列表。 |
| `createBusinessConsoleMesRushWorkOrder` | `POST /api/business-console/v1/mes/work-orders/rush` | MES 加急工单创建。 |
| `runBusinessConsoleMesSchedule` | `POST /api/business-console/v1/mes/schedules/run` | MES 规则排程运行。 |
| `recordBusinessConsoleMesProductionReport` | `POST /api/business-console/v1/mes/production-reports` | MES 生产报工。 |

BFF 可以为 Business Console 重命名页面级请求字段、添加默认值并规范化响应结构，但不得计算属于
下游服务的领域结果。必须保留在下游的示例包括库存可用量、NCR 状态转换和排程结果生成。

## API Client 生成

增加 BusinessGateway 控制台 OpenAPI 快照：

```text
frontend/packages/api-client/openapi/business-gateway-console.v1.json
```

将其生成到单独目录：

```text
frontend/packages/api-client/src/generated/business-console/
```

增加稳定的手写导出：

```text
frontend/packages/api-client/src/business-console.ts
```

生成的 BusinessGateway 控制台代码必须与现有 PlatformGateway 生成代码及规划中的移动端生成代码
保持隔离。`src/index.ts` 可以重新导出 business-console 公共类型和 helper（辅助函数），但应用页面
应从稳定 package entry（包入口）导入，而不是从 `src/generated/**` 导入。

## 前端应用

创建 `frontend/apps/business-console` 作为首个真实业务前端。它使用 Vue 3、Vite、Vue Router
文件路由、Pinia、Pinia Colada、lucide 图标、`@nerv-iip/ui`、`@nerv-iip/app-shell` 和
`@nerv-iip/api-client`。

应用结构与现有 console 应用一致：

```text
frontend/apps/business-console/
  package.json
  vite.config.ts
  tsconfig.json
  src/
    main.ts
    App.vue
    router/
    layouts/
    pages/
    components/
    composables/
    stores/
    api/
```

身份认证最初采用应用内实现，并与当前 console 身份认证模式保持一致。Business Console 应用可以
调用 PlatformGateway Console Auth endpoint（端点）执行登录、刷新、登出和 `/me`，而所有业务
数据页面都调用 BusinessGateway。如果第二份应用内身份认证副本在实施期间造成真实维护负担，
则抽取目标为 `frontend/packages/auth`，但该抽取不是 MVP 的前置条件。

## 页面范围

### #166 MasterData 第 0 层

交付以 SKU 为中心的主数据页面：

1. 带搜索/筛选和状态指示器的 SKU 列表。
2. 使用 UOM、类别、物料类型、追踪策略、保质期、存储条件、默认条码规则、质检要求和合规标签的
   SKU 创建表单。
3. 在下游表单需要时提供 UOM、站点、生产线、工作中心和设备资产的只读资源列表。

在后端公开更新 endpoint（端点）之前，现有 SKU 记录的编辑能力有意限制在页面结构和 API 布局；
UI 不得伪造更新。

### #167 Inventory

交付反映现有服务事实的库存操作：

1. 按组织、环境、SKU、UOM、站点以及可选库位、批次和序列号查询库存可用量。
2. 使用来源元数据、幂等键、质量状态、货主和数量执行库存移动过账。
3. 创建库存盘点任务并确认盘点调整。

### #168 Quality

围绕检验和 NCR 交付 Quality 页面：

1. 检验计划列表。
2. 检验记录创建。
3. NCR 列表和面向详情的 sheet（抽屉面板）。
4. NCR 处置提交和关闭操作。

### #169 MES

交付不含甘特图的 MES 页面：

1. 工单列表。
2. 加急工单创建。
3. 在现有 MES API 已公开相应能力时，运行规则排程并展示排程结果列表/状态。
4. 在现有 endpoint（端点）可用时，创建生产报工并展示成品入库请求。

## 错误处理

BusinessGateway 将下游 `ResponseData` 失败和 HTTP 失败映射为 PlatformGateway 使用的相同响应
envelope（信封）样式。它必须保留未认证和未授权用户对应的 401 与 403 语义。下游 4xx 响应显示为
业务表单或页面错误。下游 5xx 和无效响应显示为 BFF 错误，且不得泄露服务 URL、内部令牌或堆栈跟踪。

前端页面使用 `@nerv-iip/ui` 中现有的 alert（警告）、empty（空态）、skeleton（骨架屏）、
dialog（对话框）、sheet（抽屉面板）和 table（表格）模式。当后端操作改变工作流状态时，破坏性或
不可逆操作必须得到明确确认。

## 测试

后端测试：

1. 针对每条 MVP 路由的 BusinessGateway OpenAPI operationId 测试。
2. 证明每条路由检查预期 IAM 权限的授权测试。
3. 证明不会向内部业务服务发送用户 bearer token（持有者令牌）、而会使用内部服务令牌的下游代理测试。
4. 针对下游失败 envelope（信封）和权限禁止检查的错误映射测试。

前端测试：

1. 应用 bootstrap（引导启动）、身份认证 guard（守卫）和路由 smoke test（冒烟测试）。
2. MasterData、Inventory、Quality 和 MES query/mutation wrapper（查询/变更包装器）的 composable 测试。
3. 列表、空态、错误、提交成功和权限拒绝状态的页面测试。
4. 首批页面接入后的桌面端与移动端布局 Playwright 检查。

实施验证命令：

```powershell
dotnet test backend/Nerv.IIP.sln
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

如果新增或修改脚本，还应运行：

```powershell
scripts/check-script-governance.ps1
```

## 文档更新

实施必须保持以下文档同步：

1. `docs/adr/0012-business-platform-domain-layering.md`
2. `docs/architecture/api-contract-and-codegen.md`
3. `docs/architecture/business-platform-domain-architecture.md`
4. `docs/architecture/frontend-structure.md`
5. `docs/architecture/repo-layout.md`
6. `docs/architecture/implementation-readiness.md`

只有在 BusinessGateway、api-client 生成链路和 business-console 页面已经存在，并且验证已通过或
剩余环境阻塞已明确记录后，readiness（就绪状态）才能宣称代码已经落地。

## 风险与缓解措施

1. console 应用之间的身份认证重复实现可能产生漂移。保持第一版小而精，只有在共享行为明确后才
   抽取 `frontend/packages/auth`。
2. BusinessGateway 可能演变成业务规则宿主。测试和代码审核必须确保计算和状态转换保留在下游业务服务中。
3. 不同生成客户端之间的 operation ID 可能冲突。使用 `BusinessConsole` 前缀，并生成到
   `src/generated/business-console/`。
4. 后端服务可能缺少某些期望 UI 流程所需的更新/详情 endpoint（端点）。MVP 应如实展示创建、列表和
   工作流界面并报告缺失的后端端点，而不得伪造不受支持的行为。
