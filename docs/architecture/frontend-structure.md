# 前端结构与命名规范

本文档定义 Nerv-IIP 前端工作区的目录职责、配置分层、路由规则、页面共置策略、状态管理边界和命名习惯。

## 总体原则

1. 目录职责借鉴 Nuxt 的清晰语义，但运行时坚持 Vue 原生范式。
2. 默认入口采用 src/main.ts + src/App.vue，不默认创建 src/app。
3. 文件路由与显式 router/index.ts 同时存在。
4. 页面保持薄，视图拼装与局部交互逻辑优先放入 composables 和 query/mutation 层。
5. 生成代码与手写代码目录必须隔离。

## 工作区结构

```text
frontend/
  apps/
    console/
      src/
    business-console/
      src/
    business-pda/              # PDA 一线作业（Capacitor APK 基线，已建）
    design-system/             # VitePress 设计系统文档/预览站
    docs/                      # VitePress 产品文档站
    screen/                    # 工业数据大屏：车间/产线/仓库/工厂（公共展示/指挥中心，只读，独立 app）
  packages/
    ui/
    ui-mobile/                # 触摸组件层（PDA/移动密度，复制重建、原版零改）
    business-core/            # 同源 SOP/字典(CodeSet)/领域类型/命令构造器
    app-shell/
    api-client/
    auth/
```

第三迭代只创建控制台纵切必需包：`api-client`、`ui`、`app-shell`。`auth` 已在 Console 与 Business Console 出现真实登录复用压力后创建为共享包，承载 Gateway auth DTO mapping、storage adapter、refresh orchestration、logout/session revoke 组合、unauthorized handler 和 app-agnostic route helper；各 app 必须显式注入 storage key、Pinia store id、API client、登录路径和文案。`ui-mobile` 与 `business-core` 已随 PDA 轨道落地，用于移动密度组件和 PC/PDA 同源 SOP、字典、标签与领域流程。`layer-base`、`layer-platform`、`shared-types` 仍是已冻结的长期边界，等跨包复用真正出现时再创建，避免空包提前漂移。

产品文档站 `frontend/apps/docs` 是独立 VitePress app，用于承载面向最终用户的上手指南、业务流程图和当前能力边界说明。正文不得放入 Business Console；内部缺口记录放在 docs app 的 `docs/internal/gaps` 下并在文档中标注证据页面，不作为官网对外文案。

移动端（PDA 优先）实施轨新增以下 app/包，事实来源为 `docs/superpowers/specs/2026-06-09-mobile-pda-design.md` 与 `docs/architecture/mobile-pda-module-product-design.md`：

- `apps/business-pda`：手持 PDA 一线作业（WMS/MES 扫码任务 + 轻量设备报修/点检），Capacitor 打包 Android APK；与 `business-console` 同源消费 `@nerv-iip/api-client` 的 business-console 稳定导出，当前只经 BusinessGateway `/api/business-console/v1/**` facade，不直连业务服务 URL，不复用 PC 菜单树。独立 `/api/mobile/v1/**` facade、mobile OpenAPI 快照和 `api-client/src/mobile.ts` 属于后续移动专用 API 轨道。
- `packages/ui-mobile`：触摸/PDA 区块组件层（Reka UI + Tailwind + 复用 `@nerv-iip/ui` 的设计 token），按「原版零改、复制重建」doctrine 自建移动密度组件（ScanBar、TabBar、BottomSheet、ListRow 等），不 import PC FE-2 区块以避免桌面密度污染。
- `packages/business-core`：与 PC 同源的内核——领域类型 + SOP/状态机 + 字典(CodeSet) + 命令构造器，由 `business-console` 现有 `src/data/*.ts` 有界抽取，PC 与移动端共用；PC 端逐步迁移消费，不一次性改写。

`apps/business-workstation`（工位机/平板触摸操作台）为 roadmap 预留，v1 不实现。大屏只读看板已由 `apps/screen` 落地（取代原 `business-board` 占位命名，见 GitHub Epic #562）：独立 Vite app，全屏深色 `ScreenLayout` + `ScreenScaler` 等比缩放，复用 `ui` 的 screen 层组件与 `--nv-scr-*` token，不依赖 `ui-mobile`（展示态非触摸操作态）。

Business Console MVP 是第二个真实应用入口：`frontend/apps/business-console` 使用 Vite Plus + Vue 3 建立独立 app shell，承载 #166 到 #169 的 MasterData、Inventory、Quality 和 MES 业务页面，并已补入 ProductEngineering、DemandPlanning 和 MES PC 工作台相关路由。它消费 BusinessGateway 的 `/api/business-console/v1/**` facade，不直接调用业务服务 URL，也不把业务 CRUD 页面放回主平台 `frontend/apps/console`。完整导航地图、能力目录、角色导航、分期和“后端存在但前端不得提前暴露”的规则见 `docs/architecture/frontend-navigation-map.md`。Business Console 的可见导航不得机械映射后端服务列表；实现时必须按 RBAC、角色任务、feature flag、近期/星标、全局搜索和上下文穿透组织入口。PC 端长期采用顶部-侧边 T 型导航，但当前 `@nerv-iip/app-shell` 只有侧边栏 `navItems` 契约；落地前必须先扩展 app-shell 公共 API 和测试，不得在业务页面局部硬拼顶部域导航。

Console Auth + shadcn-vue Baseline 当前采用共享 `frontend/packages/auth` 方案：app 内 `src/stores/auth.ts` 只配置 storage key、Pinia store id、文案和注入后的 auth API；`src/api/auth.ts` 只包装 Gateway Auth facade 的稳定 `@nerv-iip/api-client` 导出；路由守卫、redirect sanitizer、unauthorized redirect、refresh orchestration 和 logout/session revoke 组合由 `@nerv-iip/auth` 提供。共享包不直接耦合某个页面或 app shell。

第五阶段曾暂缓前端功能实施，避免后端 SDK、迁移发布和部署验证被控制台 UI 牵引。Phase 8 已把 Console Design System 基线推进到 Calm Control Plane 蓝色主题：`frontend/apps/console/src/assets/main.css` 中的 shadcn semantic tokens 负责蓝色主动作、focus ring、sidebar selected state 和 chart orientation；旧 `--legacy-color-*` 只作为兼容 token 保留。新的页面、组件皮肤、组件库迁移或 token 体系必须沿用 docs/architecture/frontend-design-system-planning.md 的 Selected Baseline。

## 配置分层

### 根级配置

- package.json：工作区脚本入口与前端依赖入口。
- pnpm-workspace.yaml：纳入 apps 与 packages。
- vite.config.ts：工作区级 Vite+ 配置，负责 check、fmt、lint、test、run 与 workspace task 定义。根级测试配置会提供 Vue SFC 解析与 workspace alias，应用 dev/build/runtime 仍由应用级 vite.config.ts 负责。
- tsconfig.base.json：前端共享 TypeScript 基线；第三阶段为了兼容 Vite+ 当前构建目标，`target` 使用 `ES2023`，`lib` 仍保留 `ES2024`。

### 应用级配置

- frontend/apps/console/package.json：控制台应用脚本。
- frontend/apps/console/vite.config.ts：Vue、Vue Router 官方文件路由插件、alias 和构建配置。
- frontend/apps/console/tsconfig.json：纳入 typed routes 相关类型。
- frontend/apps/business-console/package.json：业务控制台应用脚本，开发端口在 implementation-readiness 和端口矩阵中登记后固定。
- frontend/apps/business-console/vite.config.ts：沿用 Vue、Vue Router 官方文件路由插件、alias 和构建配置。
- frontend/apps/business-console/tsconfig.json：纳入业务控制台 typed routes 相关类型。
- frontend/apps/business-pda/package.json：PDA 应用脚本；`dev` 固定 5126，`build` 内含 `vue-tsc --noEmit`，原生同步通过 `cap:sync`。
- frontend/apps/design-system/package.json：设计系统文档/预览站脚本；本地 dev 端口固定 5180。
- frontend/apps/docs/package.json：产品文档站脚本；本地 dev 端口固定 5181。

根级 `pnpm -C frontend test` 必须运行 workspace test task；根级 `pnpm -C frontend build` 当前构建 `@nerv-iip/console`、`@nerv-iip/business-console`、`@nerv-iip/design-system` 和 `@nerv-iip/docs`，避免新增业务 app 后只验证主平台 console。`@nerv-iip/business-pda` 不在 workspace build 聚合内，PDA 变更必须额外运行 `pnpm -C frontend --filter @nerv-iip/business-pda typecheck`、`pnpm -C frontend --filter @nerv-iip/business-pda test`、`pnpm -C frontend --filter @nerv-iip/business-pda build`；涉及 Capacitor native 产物时再运行 `pnpm -C frontend --filter @nerv-iip/business-pda cap:sync`。

### 包级配置

- frontend/packages/api-client/package.json：API 生成、类型检查和测试入口。
- frontend/packages/api-client/openapi-ts.config.ts：Hey API 生成配置，输入来自 Gateway OpenAPI 快照。
- frontend/packages/ui/package.json：shadcn-vue 组件库入口，负责类型检查、稳定导出和本地 token/utils；Console 应用不直接引用 `components/ui` 深层文件。
- frontend/packages/ui-mobile/package.json：PDA/触摸密度组件稳定导出入口。
- frontend/packages/auth/package.json：跨 app 登录恢复、刷新、退出和 unauthorized 处理入口。
- frontend/packages/business-core/package.json：PC/PDA 同源 SOP、字典、领域类型和命令构造器入口。
- frontend/packages/app-shell/package.json：应用壳层组件的类型检查入口。

### 第三阶段工具入口

第三阶段根级脚本固定为：

```powershell
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

`check`、`lint`、`fmt` 由 Vite+ 读取根级 `vite.config.ts` 中的 `fmt` / `lint` 配置。该路径要求 Node.js `>=22.18.0`，仓库根 `.node-version` 固定为 22.22.3，本机第三阶段验证使用 OpenJS.NodeJS.22 v22.22.3。

## 控制台应用目录职责

### 基础入口

- src/main.ts：应用启动、Provider 安装、router 与 store 装配。
- src/App.vue：根壳层，不承载业务逻辑。
- src/app：可选 bootstrap 目录，仅在入口拆分明显增多时引入。

### 运行时骨架

- src/router：router/index.ts、guards、meta、route helpers。
- src/layouts：布局组件。
- src/pages：真实页面入口。
- src/components：共享与局部视图组件。
- src/composables：跨组件与跨页面复用逻辑。
- src/stores：Pinia client state。
- src/api：应用侧 API 组装层。
- src/plugins：应用级插件安装。
- src/utils：纯函数工具。

## 路由规范

1. 文件路由统一采用 Vue Router 官方文件路由插件。
2. src/pages 是唯一页面来源，但必须保留显式的 src/router/index.ts。
3. route meta 描述访问控制、布局、feature flag 和页面标题。
4. guards 统一放在 src/router/guards。
5. 不引入单独 middleware runtime。

### Vue Router 插件配置

1. Vite 插件顺序固定为 `VueRouter()` 在前、`Vue()` 在后。
2. 控制台应用从 `vue-router/auto-routes` 导入生成路由，并在 `src/router/index.ts` 中调用 `createRouter`。
3. 开发模式启用 `handleHotUpdate(router)`，避免路由文件变化时必须手动刷新。
4. `typed-router.d.ts` 由官方插件生成并纳入 TypeScript 检查；控制台应用的 tsconfig 必须包含它。
5. 当页面目录内出现私有 `.vue` 组件目录时，优先通过 `routesFolder.exclude` 排除 `components`、`dialogs`、`drawers`、`fragments`，不重写全局 `filePatterns`。

### 页面命名建议

- index.vue
- users/[id]/index.vue
- settings/audit/index.vue
- apps/[appId]/index.vue

## 页面共置规则

### 简单页面

- 直接使用单文件页面，例如 apps/index.vue、audit/index.vue。

### 复杂页面

- 采用文件夹加 index.vue 模式。
- 页面私有视图组件放 components。
- 页面私有弹窗放 dialogs。
- 页面私有抽屉放 drawers。
- 页面私有片段放 fragments。
- 页面专属 columns.ts、schema.ts、useXxx.ts 可以直接共置。

### 排除策略

1. 优先使用 exclude 排除页面私有 .vue 组件目录。
2. 默认不重写 filePatterns。
3. 共置的 .ts 文件默认不会被扫描为路由。

### 上提规则

1. 只被一个页面使用的组件，留在页面目录。
2. 被同一领域多个页面复用的组件，上提到 src/components/领域名。
3. 被多个应用或多个领域复用的组件，上提到 packages/ui 或 layer 包。

## 状态与请求分层

### Pinia

- 只管理客户端状态。
- 保存用户会话、组织上下文、环境上下文、布局偏好、命令面板状态。

### Pinia Colada

- 只管理服务端状态。
- 列表、详情、查询缓存、失效、重试和 mutation 生命周期统一走 Colada。

### api-client

- frontend/packages/api-client 负责生成 types、sdk、client 以及 Colada 查询和变更函数。
- 应用层只从稳定导出入口消费，不直接引用 generated 深层路径。
- `openapi/platform-gateway.v1.json` 是 Gateway OpenAPI 导出快照，不能手动改写。
- `src/transport` 只处理 baseURL、认证头、错误归一化和请求策略，不承载业务视图逻辑。

### Console Auth

Console 登录闭环通过 PlatformGateway Console Auth facade 调用 IAM。`stores/auth.ts` 只管理客户端会话状态，`api-client` 继续由 Gateway OpenAPI 生成 SDK 与 Pinia Colada options。路由守卫放在 `src/router/guards/auth.ts`，登录页和登录表单放在 `src/pages/login.vue` 与 `src/components/auth/LoginForm.vue`。

### Business Console

Business Console 登录、刷新、退出和 `/me` 复用 PlatformGateway Console Auth facade 的契约，业务数据页只消费 BusinessGateway 生成客户端。Business Console 与 Console 共用 `@nerv-iip/auth` 的会话恢复、刷新编排、退出处理和 unauthorized handler；Business Console 只注入自身 storage key、Pinia store id、中文文案和登录路径。

业务页面按领域目录组织。下表只描述当前代码中的 route/facade 事实；长期能力目录、角色导航、分期和升级门禁以 `docs/architecture/frontend-navigation-map.md` 为准。

| 路由 | 页面范围 | 数据入口 |
| --- | --- | --- |
| `/` | 业务工作台入口；真实 KPI、跨域待办、消息和预警聚合后置。 | 当前路由入口 + 业务模块链接。 |
| `/master-data/skus` | SKU 列表、创建和基础资源选择。 | BusinessGateway MasterData facade。 |
| `/master-data/partners` | 客户与供应商过渡视图。 | BusinessGateway MasterData resource facade + 当前本地场景数据。 |
| `/master-data/resources` | 工厂、产线、工作中心、设备、班次、日历、班组和技能过渡视图。 | BusinessGateway MasterData resource facade + 当前本地场景数据。 |
| `/master-data/process` | 工艺与版本过渡视图；后续应收敛到 ProductEngineering，不继续扩展 MasterData 领域规则。 | 当前本地场景数据。 |
| `/engineering` | MBOM、工艺路线、生产版本和生产版本 resolve 的窄化工程资料工作台。 | BusinessGateway ProductEngineering facade。 |
| `/planning` | 需求、MRP run、pegging、计划建议和建议接受。 | BusinessGateway DemandPlanning facade。 |
| `/erp` | ERP 业务协同过渡聚合页。 | 当前本地场景数据；正式 ERP facade/page 尚未落地。 |
| `/inventory/availability` | 可用量查询。 | BusinessGateway Inventory facade。 |
| `/inventory/lots` | 批次与预留视图，展示 availability facade 返回的批次、序列号、预留和可用量，并提供 MES/WMS/Quality/Barcode 上下文链接；独立冻结、预留明细和库存分析仍按后端 facade 缺口处理。 | BusinessGateway Inventory availability facade。 |
| `/inventory/movements` | 库存移动工作台；新建移动通过抽屉承载。 | BusinessGateway Inventory facade。 |
| `/inventory/counts` | 盘点任务工作台；创建任务和确认差异通过抽屉承载。 | BusinessGateway Inventory facade。 |
| `/quality/inspections` | 检验任务与记录；检验记录创建通过抽屉承载。 | BusinessGateway Quality facade。 |
| `/quality/ncrs` | NCR 列表、处置和关闭。 | BusinessGateway Quality facade。 |
| `/quality/analysis` | 按同一 SKU、特性、工作中心、子组大小与样本范围展示 SPC Xbar/R 控制图、中心线、上下控制限、非纯颜色判异定位和 Cp/Cpk；缺陷 Pareto、物料/来源维度继续只基于当前 NCR 返回窗口并保留表格核查，不冒充全量历史趋势。工位/设备/班次全量聚合和 CAPA 读面仍按后续 facade 缺口处理。 | BusinessGateway Quality NCR/SPC facade。 |
| `/mes` | 生产驾驶舱，展示工单、工序、在制、阻塞和角色待办。 | BusinessGateway MES facade。 |
| `/mes/foundation` | 基础准备，展示 MasterData、ProductEngineering、Supply、Quality、Equipment、Barcode/Numbering 等开工前就绪结果。 | BusinessGateway MES facade。 |
| `/mes/plans` | 生产计划，展示可转入 MES 执行的计划和计划就绪状态。 | BusinessGateway MES facade。 |
| `/mes/work-orders` | 计划与工单；急单创建、释放、行级操作和报工通过抽屉或详情路由承载。 | BusinessGateway MES facade。 |
| `/mes/work-orders/:workOrderId` | 工单详情，展示工序、用料、开工阻塞，并作为报工、完工入库和质量检验的上下文入口。 | BusinessGateway MES facade。 |
| `/mes/work-order-detail/:workOrderId` | 旧工单详情兼容路由，进入后重定向到 `/mes/work-orders/:workOrderId`；仅为兼容入口，清理由 #196 在 PC 工作流收口前处理。 | BusinessGateway MES facade。 |
| `/mes/materials` | 齐套与物料，展示领料申请、齐套状态和线边接收入口。 | BusinessGateway MES facade。 |
| `/mes/dispatch` | 派工看板，按工作中心、设备、班次和人员查看待派工任务。 | BusinessGateway MES facade。 |
| `/mes/operation-tasks` | 工序执行任务列表，提供查看工单、查看报工、呼叫质检、记录异常和开工/暂停/恢复/完工入口。 | BusinessGateway MES facade。 |
| `/mes/wip` | 在制跟踪。 | BusinessGateway MES facade。 |
| `/mes/reports` | 报工与完工，展示生产报工、良品、不良、返工和完工相关状态。 | BusinessGateway MES facade。 |
| `/mes/production-reports` | 旧报工记录查询路由；新增报工从工单或工序上下文进入。 | BusinessGateway MES facade。 |
| `/mes/quality` | 质量与不良，展示 MES 缺陷上下文和关联 Quality 事项。 | BusinessGateway MES facade。 |
| `/mes/receipts` | 完工入库请求；新增请求通过抽屉承载，行级入口可回到工单上下文。 | BusinessGateway MES facade。 |
| `/mes/schedules` | MES 规则排程过渡页，保留执行域规则分配结果和显式运行动作；不包含甘特，不承担 APS 算法，正式排产输出进入 `/scheduling`。 | BusinessGateway MES facade。 |
| `/mes/downtime` | 设备与停机，展示停机、恢复和未结异常。 | BusinessGateway MES facade。 |
| `/mes/handovers` | 班次交接，展示待交接事项和班组交接状态。 | BusinessGateway MES facade。 |
| `/mes/traceability` | 追溯查询，可按工单、批次/序列、物料批次进入执行证据链。 | BusinessGateway MES facade。 |
| `/mes/capacity` | 产能影响，展示工作中心/设备不可用对生产计划的影响。 | BusinessGateway MES facade。 |

业务控制台的服务端状态统一放入 `src/composables/useBusinessMasterData.ts`、`useBusinessInventory.ts`、`useBusinessQuality.ts` 和 `useBusinessMes.ts`。这些 composable 只消费 `@nerv-iip/api-client` 的 business-console 稳定导出，不深 import generated，不手写业务服务 URL。

Business Console focused verification commands:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

MES PC 工作台验证命令：

```powershell
scripts/verify-business-console-mes-pc-workbench.ps1
scripts/verify-business-console-mes-pc-workbench.ps1 -E2E -ChromiumExecutablePath "C:\Program Files\Google\Chrome\Application\chrome.exe"
```

当前 e2e smoke 覆盖桌面与移动视口下的 SKU、库存可用量、Quality NCR 和 MES PC 主要路由；本地缺少 Playwright managed Chromium 时，可临时设置 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 或使用脚本的 `-ChromiumExecutablePath` 指向已安装 Chrome/Chromium 后运行。

ADR 0014 后，APS/Gantt 不进入 `/mes/schedules` 页面内部。#206 负责后端 APS lite 排程契约和内核，#78 负责甘特/排产图展示；未来独立排程工作台应消费 APS 输出 DTO，并继续通过 BusinessGateway facade 访问业务数据。

### Console IAM Admin

Phase 8 已交付 IAM Admin 控制台路由：

| 路由 | 页面 | 主要组件 | 权限语义 |
| --- | --- | --- | --- |
| `/iam/users` | `src/pages/iam/users/index.vue` | `UsersTable`、`UserCreateDialog`、`UserEditDialog`、`UserResetPasswordDialog` | 用户列表、创建、编辑、禁用、重置密码。 |
| `/iam/roles` | `src/pages/iam/roles/index.vue` | `RolesTable`、`RoleCreateDialog`、`RolePermissionEditor` | 角色列表、创建、权限编辑和权限 catalog 展示。 |
| `/iam/sessions` | `src/pages/iam/sessions/index.vue` | `SessionsTable`、`RevokeSessionDialog` | 会话列表、当前会话标识和会话撤销。 |

IAM Admin 的服务端状态统一放在 `src/composables/useIamAdmin.ts`，并拆为 `useIamUsers()`、`useIamRoles()` 和 `useIamSessions()`。这些 composable 只消费 `@nerv-iip/api-client` 的稳定 Gateway facade exports，包括 generated Pinia Colada query/mutation options 与 Console IAM 类型别名；页面和组件不得直接调用 IAM 服务 URL、不得深 import `frontend/packages/api-client/src/generated/*`，也不得绕过 PlatformGateway 直连领域服务。

### 轮询与刷新

1. 服务端状态刷新由 Pinia Colada 管理。
2. 需要轮询的任务详情使用 Pinia Colada 官方 auto-refetch 插件和 query option 表达。
3. 页面组件不直接使用 `setInterval` 拉取服务端状态。

## 命名规则

### 组件

- UiXxx：纯 UI 组件。
- AppXxx：应用壳级组件。
- TheXxx：全局结构组件。
- IamXxx、OpsXxx、HubXxx、KnowledgeXxx：领域组件。

### 逻辑

- useXxx：composable。
- authStore、layoutStore：Pinia store。
- auth.ts、permission.ts、env-context.ts：router guards。

### 插件

- 使用有序前缀，例如 10.auth.ts、20.query.ts、30.icons.ts。

## 首批脚手架交付物

1. 根级 package.json、pnpm-workspace.yaml、vite.config.ts、tsconfig.base.json。
2. console、business-console、business-pda、design-system 和 docs 应用的 main/App/vite/router 或 VitePress 入口。
3. packages/ui、ui-mobile、app-shell、api-client、auth、business-core 初版。
4. packages/layer-base、packages/layer-platform、packages/shared-types 只在出现真实跨包或跨应用复用需求时创建，不作为当前脚手架交付物。
