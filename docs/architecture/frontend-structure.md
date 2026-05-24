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
  packages/
    ui/
    app-shell/
    api-client/
    layer-base/
    layer-platform/
    auth/
    shared-types/
```

第三迭代只创建控制台纵切必需包：`api-client`、`ui`、`app-shell`。`layer-base`、`layer-platform`、`auth`、`shared-types` 是已冻结的长期边界，等第二个应用或跨包复用真正出现时再创建，避免首批脚手架提前空转。

Business Console MVP 是第二个真实应用入口：`frontend/apps/business-console` 使用 Vite Plus + Vue 3 建立独立 app shell，承载 #166 到 #169 的 MasterData、Inventory、Quality 和 MES 业务页面。它消费 BusinessGateway 的 `/api/business-console/v1/**` facade，不直接调用业务服务 URL，也不把业务 CRUD 页面放回主平台 `frontend/apps/console`。

Console Auth + shadcn-vue Baseline 当前采用“app 内 auth”方案：`frontend/apps/console/src/stores/auth.ts` 管理会话状态，`src/api/auth.ts` 包装 Gateway Auth facade，路由守卫位于 app 内。完整 `frontend/packages/auth` 独立包方案留作后续参考；当 Console 之外出现第二个应用、插件宿主或跨包登录复用时再抽取，边界应包含 Gateway auth DTO mapping、storage adapter、refresh orchestration、logout/session revoke 组合、unauthorized handler 和 app-agnostic route helper，不直接耦合某个页面或 app shell。

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

根级 `pnpm -C frontend test` 必须运行 workspace test task；根级 `pnpm -C frontend build` 必须同时构建 `@nerv-iip/console` 和 `@nerv-iip/business-console`，避免新增业务 app 后只验证主平台 console。

### 包级配置

- frontend/packages/api-client/package.json：API 生成、类型检查和测试入口。
- frontend/packages/api-client/openapi-ts.config.ts：Hey API 生成配置，输入来自 Gateway OpenAPI 快照。
- frontend/packages/ui/package.json：shadcn-vue 组件库入口，负责类型检查、稳定导出和本地 token/utils；Console 应用不直接引用 `components/ui` 深层文件。
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

Business Console 登录、刷新、退出和 `/me` 可以先复用 PlatformGateway Console Auth facade 的契约，业务数据页只消费 BusinessGateway 生成客户端。首版允许 app-local auth 代码与主平台 console 保持结构一致；当两个应用的会话恢复、刷新编排、退出处理和 unauthorized handler 出现真实复用压力时，再抽取 `frontend/packages/auth`。

业务页面按领域目录组织：

| 路由 | 页面范围 | 数据入口 |
| --- | --- | --- |
| `/master-data/skus` | SKU 列表、创建和基础资源选择。 | BusinessGateway MasterData facade。 |
| `/inventory/availability` | 可用量查询。 | BusinessGateway Inventory facade。 |
| `/inventory/movements` | 库存移动提交。 | BusinessGateway Inventory facade。 |
| `/inventory/counts` | 盘点任务与调整确认。 | BusinessGateway Inventory facade。 |
| `/quality/inspections` | 检验计划列表和检验记录创建。 | BusinessGateway Quality facade。 |
| `/quality/ncrs` | NCR 列表、处置和关闭。 | BusinessGateway Quality facade。 |
| `/mes/work-orders` | 工单列表和急单创建。 | BusinessGateway MES facade。 |
| `/mes/schedules` | 规则排程运行和结果状态；不包含甘特。 | BusinessGateway MES facade。 |

业务控制台的服务端状态统一放入 `src/composables/useBusinessMasterData.ts`、`useBusinessInventory.ts`、`useBusinessQuality.ts` 和 `useBusinessMes.ts`。这些 composable 只消费 `@nerv-iip/api-client` 的 business-console 稳定导出，不深 import generated，不手写业务服务 URL。

Business Console focused verification commands:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

当前 e2e smoke 覆盖桌面与移动视口下的 SKU、库存可用量、Quality NCR 和 MES 工单页面；本地缺少 Playwright managed Chromium 时，可临时设置 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 指向已安装 Chrome/Chromium 后运行。

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
2. console 应用的 main.ts、App.vue、vite.config.ts、router/index.ts、guards、layouts/default.vue、pages/index.vue。
3. packages/ui、packages/app-shell、packages/api-client 初版。
4. packages/layer-base、packages/layer-platform、packages/auth、packages/shared-types 只在出现真实跨包或跨应用复用需求时创建；其中 `packages/auth` 的完整独立包方案按上文 Console Auth 留档边界执行。
