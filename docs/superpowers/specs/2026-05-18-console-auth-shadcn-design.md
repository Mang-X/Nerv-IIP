# Console 认证与 shadcn-vue 设计

## 目的

本 spec 在 IAM 持久化认证基础和 Gateway 全局权限 enforcement 阶段之后重新启动前端产品工作。它增加最小的生产形态 Console 认证闭环，并冻结首个 shadcn-vue design-system 决策，使未来 UI 工作具备唯一组件事实源。

首个面向用户的结果很简单：平台管理员可以打开 Console、通过 Gateway 登录、进入现有实例工作区、使用 bearer token 保持 API 调用已授权、在应用启动时刷新已保存会话，并退出登录。

## 当前背景

1. IAM 已拥有持久化用户/会话事实、seed 管理员凭据、JWT access token、refresh token rotation、退出/会话撤销、`/api/iam/v1/me` 和权限检查。
2. PlatformGateway 已通过向 IAM 转发 bearer token 和权限上下文来保护现有 Console APIs。
3. Console 前端当前具有 Vite/Vue app、Pinia、Pinia Colada、生成的 Gateway api-client、最小 app shell，以及本地 `UiButton`、`UiPanel` 和 `UiBadge` primitives。
4. `docs/architecture/frontend-design-system-planning.md` 阻止新的前端产品工作，直到显式 design-system spec 选定 registry strategy、token model、icon policy、density、accessibility、theme、migration 和 tests。
5. 当前 `shadcn-vue` 项目检查报告 Vite + TypeScript，但没有 `components.json`、Tailwind CSS 文件、Tailwind config 或已初始化的 shadcn-vue config。

## 决策

### 架构

使用 PlatformGateway Console Auth facade。

前端只调用 PlatformGateway：

```text
POST /api/console/v1/auth/login
POST /api/console/v1/auth/refresh
POST /api/console/v1/auth/logout
GET  /api/console/v1/auth/me
```

Gateway 将这些调用转发给 IAM，且不存储或重新解释身份事实。IAM 仍是用户、会话、security stamps、permission versions、organization scope、environment scope 和 permission codes 的事实源。

这使 Console 部署模型保持简单：一份生成的 OpenAPI contract、一个 base URL 和一个面向浏览器的 API surface。

### 设计系统

直接在现有 frontend workspace 中初始化 shadcn-vue。

使用：

1. 官方 shadcn-vue registry。
2. `nova` preset。
3. Vite 模板。
4. Reka 基础组件。
5. shadcn-vue semantic tokens 和 Tailwind integration。
6. 使用 `lucide-vue-next` 作为 icon library，除非 CLI 生成的项目上下文在初始化期间选择了其他 icon library。

registry 源文件归属 `frontend/packages/ui`。Console app pages 和 feature components 通过稳定 package exports 消费 UI，而不是在整个 app 中散布 registry imports。

### Package 边界

本阶段实现不得创建 `frontend/packages/auth`。

Auth 当前仅由一个 app 使用，因此首个实现保留在 `frontend/apps/console/src`：

```text
frontend/apps/console/src/
  api/auth.ts
  components/auth/LoginForm.vue
  composables/useAuthSession.ts
  router/guards/auth.ts
  stores/auth.ts
  pages/login.vue
```

如果后续第二个 app 或 package 需要相同 auth 行为，则将稳定部分提取到 `frontend/packages/auth`。该未来 package 应拥有 auth DTO mapping、storage strategy、token refresh orchestration 和 app-agnostic route helpers。本阶段不得创建该 package。

### 旧 UI Primitives

本地 `UiButton`、`UiPanel` 和 `UiBadge` primitives 是 migration scaffolding，而不是平行 design system。安装 shadcn-vue components 并迁移当前 consumers 后，删除未使用的旧 primitive 文件和 exports。

## 范围

### 范围内

1. 用于 login、refresh、logout 和 me 的 Gateway Console Auth facade。
2. 针对新 Gateway auth endpoints 更新生成的 OpenAPI/api-client。
3. 在 `frontend/packages/ui` 中初始化 shadcn-vue 并加入首批 component set。
4. Login route 和 login form。
5. 具备 session persistence、principal state、pending/error state 和 logout cleanup 的 Pinia auth store。
6. 为生成的 Gateway requests 注入 API transport bearer。
7. 应用启动时使用保存的 refresh token 和 `/me` 确认恢复会话。
8. 用于已认证和 guest-only routes 的 Router guards。
9. 将现有 Console 实例和 operation pages 置于认证门禁之后。
10. 具备 principal display 和 sign-out command 的 App shell user menu。
11. 认证闭环的前端和后端测试。
12. 更新 frontend design-system 状态和当前 implementation readiness 文档。
13. 清理状态审计时发现的过时文档：README worktree 措辞和 schema catalog Gateway 权限状态。

### 范围外

1. OAuth2/OIDC、SSO、MFA、WebAuthn、enterprise IdP federation、consent pages 和第三方应用市场。
2. 用户、角色、会话和权限管理 UI。
3. ABAC rule authoring 或 runtime policy editor。
4. 高风险 Ops approval flows 或 notification integration。
5. Cookie-based browser auth、CSRF、DPoP、token binding 或 mTLS。
6. 多租户品牌化。
7. 创建 `frontend/packages/auth`。
8. 超出认证门禁和一致性所需 shadcn-vue component migration 的现有实例与 operation workflows 重做。

## 后端设计

### Gateway 门面

增加调用 IAM 的 Gateway auth client：

```text
Console browser -> PlatformGateway /api/console/v1/auth/* -> IAM /api/iam/v1/*
```

Gateway facade 使用稳定的 Console operation IDs 转发请求和响应 payload：

1. `loginConsoleUser`
2. `refreshConsoleSession`
3. `logoutConsoleSession`
4. `getConsolePrincipal`

facade 映射 IAM status codes，但不隐藏重要 auth 语义：

1. login 或 refresh token 无效时返回 `401`。
2. 会话已撤销或过期时返回 `401`。
3. IAM 不可用时，Gateway 返回带小型 problem response 的 `503`。
4. 意外 IAM error 返回 `502`。
5. 如果 IAM 撤销会话，Logout 返回成功；如果 logout request 失败，前端仍清除本地状态。

Gateway 不引用 IAM Domain 或 Infrastructure。它只使用 HTTP 和共享 public DTOs。

### 契约形态

Console auth responses 只公开 SPA 所需内容：

```text
accessToken
refreshToken
sessionId
expiresAtUtc
principal
```

principal 包含：

```text
principalId
principalType
loginName
organizationId
environmentId
permissionVersion
```

该 contract 可从现有 IAM responses 映射；不需要新的 IAM persistence model。

## 前端设计

### shadcn-vue 初始化

在 `frontend` 中使用 `pnpm dlx shadcn-vue@latest` 初始化 shadcn-vue。

implementation plan 必须检查生成的 `components.json` 并记录：

1. `aliases`
2. `tailwindVersion`
3. `tailwindCssFile`
4. `style`
5. `base`
6. `iconLibrary`
7. `resolvedPaths`

初始组件：

1. `button`
2. `card`
3. `field`
4. `input`
5. `alert`
6. `badge`
7. `separator`
8. `skeleton`
9. `dropdown-menu`
10. `avatar`
11. `sonner`
12. `spinner`
13. `sidebar`：如果 `AppShell` migration 使用 shadcn-vue sidebar primitive；否则将首个 app shell 保持为基于 shadcn tokens 的聚焦本地组合。

使用 shadcn-vue 规则：

1. 表单使用 `FieldGroup` 和 `Field`。
2. Card layouts 使用 `CardHeader`、`CardTitle`、`CardDescription`、`CardContent` 和 `CardFooter`。
3. Loading button 将 `Spinner` 与 `disabled` 组合；不使用虚假的 `isLoading` prop。
4. Status chips 使用 `Badge`；不使用自定义 status spans。
5. Alerts 使用 `Alert`；不使用自定义 callout markup。
6. button 中的 icon 使用 `data-icon`；不在 shadcn components 内手写 icon size classes。
7. 布局使用 `gap-*`；不使用 `space-x-*` 或 `space-y-*`。
8. Component styling 使用 semantic tokens 和 variants，而不是原始 Tailwind color overrides。

### 视觉方向

Console 是重运营的平台界面，不是营销页面。视觉方向是克制的工业清晰度：

1. 紧凑但可读的工作区。
2. 中性表面与高对比度 action states。
3. 小圆角、可预测间距和清晰 focus rings。
4. 最少 motion，并尊重 reduced-motion preferences。
5. 登录界面使用真实产品身份和运营上下文，但避免超大 hero marketing composition。

登录页面应让人感到它是操作员进入 control plane 的入口：直接、平静且可信。不得引入装饰性插图、gradient blobs 或大型品牌叙事。

### 认证 Store

`stores/auth.ts` 拥有 client auth state：

1. `accessToken`
2. `refreshToken`
3. `sessionId`
4. `expiresAtUtc`
5. `principal`
6. `restoreStatus`
7. `authError`

派生状态：

1. `isAuthenticated`
2. `isRestoring`
3. `displayName`

动作：

1. `login(loginName, password)`
2. `restoreSession()`
3. `refreshSession()`
4. `loadPrincipal()`
5. `logout()`
6. `clearSession(reason)`

该 store 是 bearer token state 的唯一事实源。组件不直接读取 local storage。

### 存储策略

使用浏览器 local storage 保存 `refreshToken`、`sessionId` 和最新 principal snapshot，使浏览器刷新可以恢复 SPA。将 `accessToken` 保存在 Pinia state 中，并在启动时刷新。

这是显式的 SPA bearer-token 权衡。未来的 cookie-based auth 设计必须独立覆盖 CSRF、same-site settings、refresh token rotation semantics 和 deployment topology。

### API 传输

`frontend/packages/api-client/src/transport/client-config.ts` 应接受 dynamic auth token provider，而不是只接受 static headers。

生成的 Gateway requests 附加：

```text
Authorization: Bearer <accessToken>
```

当 auth store 具有 access token 时附加上述内容。没有 access token 的请求保持匿名，使 login 和 health endpoints 仍可用。

受保护 Console APIs 返回 `401` 时，前端清除本地 auth state 并重定向到 `/login?redirect=<current path>`。本阶段处理 startup restore 和 request-time unauthorized cleanup，不处理后台 silent refresh timers。

### 路由

Routes 使用 meta：

```text
requiresAuth: true
guestOnly: true
title: string
```

规则：

1. `/login` 仅限 guest。
2. 现有 instance list 和 operation detail routes 需要 auth。
3. 未知 route 可以保持公开，或只在 auth 后使用 app shell；implementation plan 应选择一种行为并测试。
4. 如果用户在未认证时打开受保护 route，则携带预期路径重定向到 login。
5. 如果已认证用户打开 login，则重定向到已保存的 redirect target 或 `/`。

### 组件

`pages/login.vue` 保持为轻量 route composition surface。

`components/auth/LoginForm.vue` 拥有表单呈现：

1. 登录名输入框
2. 密码输入框
3. 提交按钮
4. 行内错误提示
5. 等待状态
6. 可访问标签
7. 提交期间的 disabled state

`DefaultLayout.vue` 和 `AppShell.vue` 展示已认证上下文：

1. 品牌
2. 导航
3. principal 展示
4. 退出菜单

route page 协调成功登录后的导航。表单发出 typed events，并通过 props 接收状态。

## 错误处理

1. 凭据无效时显示 inline form error。
2. 凭据缺失时使用 client validation 和 `aria-invalid`。
3. Gateway/IAM 不可用时在表单内显示连接错误。
4. Session restore 失败时清除本地会话，并让用户停留在 login。
5. Logout 失败时仍清除本地会话并显示 toast。
6. 受保护 API 返回 `401` 时清除本地会话并重定向到 login。
7. 受保护 API 返回 `403` 时在当前页面保留为权限错误，且不清除 auth state。

## 可访问性

1. Login form fields 具有显式 labels。
2. 无效字段使用 `data-invalid`：用于 `Field`；并在 controls 上使用 `aria-invalid`。
3. Submit button 可通过键盘到达，并在 pending 时禁用。
4. 登录失败后，focus 以可预测方式移动。
5. Navigation 和 user menu 具有可访问名称。
6. Toasts 仅作补充；关键错误继续在 inline 保持可见。
7. Color contrast 遵循 shadcn-vue semantic token 默认值，并通过截图验证。
8. Motion 保持最少，并尊重 reduced-motion preferences。

## 测试策略

### 后端

增加 Gateway tests，覆盖：

1. login 转发到 IAM 并返回 auth payload。
2. 无效 login 返回 `401`。
3. refresh 转发 refresh token 并返回 rotated tokens。
4. logout 转发 bearer/session 并返回 no content。
5. me 转发 bearer 并返回 principal。
6. IAM 不可用映射为 `503`。
7. OpenAPI 公开稳定 operation IDs。

### API Client 客户端

增加或更新测试，覆盖：

1. 生成的 auth operations 通过稳定 package entry points 导出。
2. client transport 从已配置 provider 注入 bearer token。
3. logout 后匿名请求不包含 stale auth headers。

### 前端单元与组件

增加测试，覆盖：

1. auth store login 成功。
2. auth store login 失败。
3. 使用有效 refresh token 恢复 session。
4. session restore 失败会清除 storage。
5. router guard 重定向未认证用户。
6. router guard 将已认证用户从 login 重定向离开。
7. LoginForm 的禁用/等待/错误状态。
8. AppShell sign-out command 调用 logout。

### 前端质量门禁

运行：

```powershell
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

### 视觉验证

启动 Console dev server 并使用浏览器验证：

1. 桌面端登录页
2. 移动端登录页
3. 已认证的 app shell
4. 退出登录菜单
5. 受保护 route 的重定向

截图必须证明没有文字重叠、没有被截断的 button labels、focus states 可见，且 shadcn-vue styles 非空白。

## 文档更新

更新：

1. `docs/architecture/frontend-design-system-planning.md`：记录 shadcn-vue official registry + `nova` preset 是本阶段所选基线。
2. `docs/architecture/frontend-structure.md`：记录 auth store、guards 和 shadcn-vue UI package 归属。
3. `docs/architecture/iam-authentication-baseline.md`：说明 Console login 使用构建于 IAM 之上的 Gateway facade。
4. `docs/architecture/implementation-readiness.md`：仅在验证通过后将 Console login UI 标记为已完成。
5. `README.md`：删除过时的 current-worktree 措辞。
6. `docs/architecture/database-schema-catalog.md`：删除声称 Gateway 全局 permission enforcement 尚未连接的过时表述。

## 落地与迁移

1. 先实现 Gateway facade，使生成的 OpenAPI 成为唯一前端 contract。
2. 在构建 LoginForm 之前初始化 shadcn-vue 并迁移 UI package exports。
3. 在保护 routes 前增加 transport bearer injection，使现有页面在登录后继续加载。
4. 在 store restoration 工作后增加 route guards。
5. 将当前可见的本地 primitives 迁移到 shadcn-vue components。
6. 一旦 `rg "UiButton|UiPanel|UiBadge"` 显示没有 consumers，就删除旧 UI primitive 文件和 exports。
7. 将 `packages/auth` 仅保留为未来提取说明。

## 验收标准

1. 已 seed 的 admin 可以通过 Console UI 登录。
2. 浏览器刷新会恢复会话，并保持受保护页面可访问。
3. 现有 instance list、instance detail、restart action 和 operation detail 请求包含 bearer tokens。
4. auth 缺失或无效时重定向到 login。
5. Logout 清除本地会话并返回 login。
6. shadcn-vue 已初始化，并用于 login form 和已迁移的可见 UI components。
7. 未使用的旧本地 UI primitives 已删除。
8. Backend 和 frontend tests 通过。
9. Frontend quality gate 通过。
10. 浏览器截图验证 desktop 和 mobile login/app shell states。

## 未来 `packages/auth` 提取说明

只有当 auth 行为需要由多个 frontend app 或 package 消费时，才创建 `frontend/packages/auth`。该 package 应拥有可复用的 auth client adapters、storage abstractions、token lifecycle helpers 和 app-agnostic route contracts。App-specific pages、layouts 和 navigation decisions 应保留在消费 app 中。

## 自我审查

Placeholder 扫描：没有剩余 placeholder sections。

内部一致性：Gateway 是唯一面向浏览器的 API surface；IAM 仍是 auth 事实所有者；shadcn-vue 是所选 UI baseline；`packages/auth` 被显式限定为未来内容。

范围检查：这是一个 implementation plan，因为它交付一条用户工作流：认证后的 Console 入口。OAuth、admin management UI、高风险 Ops approval、notifications 和 FileStorage 仍在范围外。

歧义检查：已显式定义 storage、route guards、component ownership、cleanup behavior 和 verification gates。
