# 第 8 阶段 IAM 管理控制台与设计系统基线设计

## 目的

第 8 阶段把现有 Console Auth + shadcn-vue 基线转化为可用的管理界面。它由两个相互关联的切片组成：

1. **第 8.0 阶段 Console 设计系统基线**：为操作密集型 Console 页面定义当前阶段的设计系统契约，包括蓝色主色主题、shadcn-vue 源组件、语义化 token、密度规则、状态模式、文档和治理。
2. **第 8.1 阶段 IAM 管理控制台与角色权限补全**：完成 2026-05-19 修复后仍不完整的 IAM 管理工作流，包括角色创建、角色权限编辑、用户管理完善，以及从 Console 审查和撤销会话。

面向用户的结果很明确：平台管理员可以登录并打开 IAM 管理页面，通过遵循统一设计系统、连贯一致的 Console UI 管理用户、创建角色、编辑角色权限、检查会话并撤销会话。

## 当前背景

1. 仓库已完成前七个实施阶段以及 Console Auth + shadcn-vue 基线。
2. 2026-05-19 的提交推进了若干事项：IAM 用户 CRUD 持久化、大小写不敏感唯一索引、IAM provider 分支清理、Gateway 标准认证/授权管线、响应 envelope、Ops claim/lease、真实 Docker 重启、CI、E2E 覆盖和领域测试。
3. 当前 IAM 用户 endpoint 可以创建、修补和禁用用户。会话列表和撤销已经存在。角色列表已经存在，但 PostgreSQL 角色创建和角色权限修补仍返回未实现。
4. 当前 Console 前端已经用 `reka-nova` 初始化 shadcn-vue，并配有 Tailwind CSS v4、`lucide` 和稳定的 `@nerv-iip/ui` 导出。
5. 当前 UI 导出覆盖 Button、Card、Field、Input、Alert、Badge、Separator、Skeleton、DropdownMenu、Avatar、Toaster 和 Spinner。
6. 现有 Console 页面仍包含 `--legacy-color-*` 等旧版局部 token；它们继续作为兼容 token，而不是未来产品 API。
7. Console 前端应继续只消费 PlatformGateway OpenAPI，不应直接调用 IAM Web。

## 推荐方案

以设计系统优先的 IAM 管理切片实施第 8 阶段。

第 8.0 阶段先定义 Console 视觉语言和组件使用规则，再添加管理页面。第 8.1 阶段随后仅使用该系统实现 IAM 管理页面。

这样可避免两种失败模式：

1. 使用临时拼凑的表格、对话框、表单、空状态和错误状态标记来添加 IAM 管理页面。
2. 在产品还没有足够多界面支撑其合理性之前，就把设计系统工作扩大成宽泛的品牌系统项目。

选定的设计方向是**平静控制平面（Calm Control Plane）**。

Console 应呈现严肃的控制界面质感：平静、精确、信息密集、低噪声且便于审计。蓝色是主要操作和信息锚点，中性色界面承载大部分布局。成功、警告和危险色专用于状态语义，不得用蓝色替代。

## 考虑过的替代方案

1. **仅强化后端**：完成 IAM 角色变更和 API 测试，推迟 Console 页面。该方案风险较低，但错失 Console Auth + shadcn-vue 基线带来的机会，也不能交付产品可见的管理工作流。
2. **完整扩展 IAM 平台**：包含组织/环境切换、成员资格、外部客户端、OAuth/OIDC、MFA 和 ABAC。该范围对单一阶段而言过大，并会混合数项相互独立的安全与产品决策。
3. **设计系统优先的 IAM 管理切片**：冻结当前 Console 设计契约，再在其中交付 IAM 用户、角色和会话。选择该路径是因为它符合修复后的真实缺口，并能使第 8 阶段保持可交付。

## 范围

### 范围内

1. 当前 Console 阶段的蓝色主色主题和语义化 token 决策。
2. IAM 管理页面的 shadcn-vue 组件选择和 `@nerv-iip/ui` 导出治理。
3. Console 的页头、工具栏、筛选器、表格、对话框、破坏性操作确认、权限标签、空状态、加载状态、错误状态和权限拒绝状态模式。
4. 位于 IAM 管理 endpoint 之上的 Gateway Console IAM Admin facade。
5. IAM PostgreSQL 角色创建和角色权限更新。
6. 供角色编辑器使用的 IAM 权限目录 endpoint。
7. 管理员为用户重置密码的 endpoint。
8. 用户、角色和会话的 Console 页面。
9. OpenAPI snapshot 和生成的 api-client 更新。
10. 管理工作流的后端、api-client、Vue 组件/单元测试和 E2E 覆盖。
11. 设计系统状态、前端结构、IAM 认证基线、授权矩阵和实施就绪状态的文档更新。

### 范围外

1. OAuth/OIDC、SSO、MFA、WebAuthn、同意页面和第三方应用市场。
2. 完整的 ABAC 规则编写或策略编辑器。
3. Connector Host bearer-token 迁移。header-secret 兼容性仍与本阶段分开。
4. 高风险 Ops 审批、Notification 集成和持久化 Ops outbox。
5. FileStorage 上传/下载 UI。
6. 多租户品牌、暗色模式产品承诺或主题切换 UI。
7. 第 8 阶段页面定向截图之外的完整视觉回归测试基础设施。
8. 抽取 `frontend/packages/auth`；Console 在本阶段仍是唯一前端应用。
9. 删除 Console 中所有旧版页面样式。新的 IAM 页面必须使用新系统；仅当共享 shell 或明显低风险清理触及现有实例页面时，才可迁移这些页面。

## 第 8.0 阶段 Console 设计系统基线

### 设计系统模式

这是一项 **Create（创建）**模式的设计系统工作，刻意将产出收窄为当前阶段的 Console 设计系统蓝图和初始 backlog。

该设计系统被视为连接以下内容的产品界面：

1. `frontend/packages/ui`、`frontend/packages/app-shell` 和 `frontend/apps/console` 中的代码实现。
2. 架构文档和未来 Superpowers 计划中的文档。
3. 无障碍行为、键盘支持和可测试性。
4. 添加 shadcn-vue 组件并通过 `@nerv-iip/ui` 暴露这些组件的治理。

仓库目前没有 Figma 组件库。代码和文档是本阶段的事实来源。

### 产品界面

该基线覆盖以下 Console 界面：

1. 已认证的应用 shell。
2. 实例概览和操作详情页面。
3. IAM 用户页面。
4. IAM 角色和权限编辑器页面。
5. IAM 会话页面。
6. 这些页面所需的共享对话框、警报、菜单、表格和表单控件。

它不覆盖营销页面、公开文档站点或面向客户的租户品牌。

### 用户与团队

主要用户：

1. 管理用户、角色和会话的平台管理员。
2. 检查托管应用实例和低风险操作的运维人员。
3. 扩展 Console 页面的开发者或 AI 编码代理。

系统必须针对重复浏览、安全操作、可追溯性和快速实施进行优化，且不得使用局部一次性组件皮肤。

### 系统原则

1. **平静胜于炫目**：Console 是工作台，不是落地页。
2. **用蓝色表示操作和定位**：蓝色标记主要操作、选中导航、焦点和信息层级；它不替代状态颜色。
3. **密集但清晰**：表格和表单应承载操作数据，同时避免显得拥挤。
4. **显式表达状态**：加载、空、部分失败、权限拒绝和破坏性操作确认都有一等模式。
5. **shadcn-vue 优先**：先从 shadcn-vue 添加源组件，再构建自定义 UI。
6. **仅使用稳定导出**：Console 应用代码通过 `@nerv-iip/ui` 消费 UI，不使用组件深层路径。
7. **默认无障碍**：标签、焦点、键盘顺序、目标尺寸和对比度都是组件契约的一部分。
8. **小型治理闭环**：每个新 UI primitive 都必须有明确理由、导出路径以及测试或用法示例。

### 视觉方向

名称：**平静控制平面（Calm Control Plane）**

基调：

1. 企业蓝、中性工作区、精确边框。
2. 最少动效。
3. 紧凑导航。
4. 清晰利落的表格和表单层级。
5. 不使用装饰性渐变色块、散景或营销式 hero 区域处理。

视觉识别应让产品呈现为面向托管 AI 应用基础设施的可靠运维控制台。

### Token 架构

使用 design-system-steward（设计系统维护者）的分层模型：

1. Primitive 值可以存在于 CSS 中，但应用代码应消费语义化的 shadcn/Tailwind token。
2. 语义化 token 是产品契约。
3. 仅当组件需要稳定的局部覆盖时才添加组件 token。

当前交付形式是 `frontend/apps/console/src/assets/main.css` 中的 CSS 自定义属性，并通过 `@theme inline` 暴露给 Tailwind v4。

#### 蓝色主题 Token 方向

第 8 阶段应将 shadcn 语义化 token 设置为蓝色主色调色板：

| Token | 意图 | 用法 |
| --- | --- | --- |
| `--primary` | 控制蓝主要操作 | 主要按钮、选中导航、活动 tab、主要链接强调。 |
| `--primary-foreground` | 主蓝色上的前景色 | 主要操作上的文字和图标。 |
| `--ring` | 焦点蓝 | focus-visible 轮廓和交互强调。 |
| `--accent` | 柔和的蓝色调界面 | 选中表格行、柔和信息强调，以及适用时的活动导航背景。 |
| `--accent-foreground` | 柔和 accent 上的前景色 | accent 界面上的文字。 |
| `--sidebar-primary` | 侧边栏选中标记 | 品牌标识和当前分区锚点。 |
| `--chart-1` | 主要指标蓝 | 未来图表和 sparkline 的主要序列。 |

建议实施采用以下 OKLCH 方向：

```css
:root {
  --primary: oklch(0.49 0.17 255);
  --primary-foreground: oklch(0.985 0 0);
  --ring: oklch(0.62 0.15 255);
  --accent: oklch(0.96 0.03 255);
  --accent-foreground: oklch(0.28 0.11 255);
  --sidebar-primary: var(--primary);
  --sidebar-primary-foreground: var(--primary-foreground);
  --chart-1: oklch(0.58 0.16 255);
}
```

可以在浏览器截图后于实施阶段微调精确值，但角色映射由本规格固定。

#### 状态 Token

状态颜色保持相互独立的语义：

| 状态 | Token 来源 | 含义 |
| --- | --- | --- |
| 成功 | 通过 Badge variant 或未来 token 提供的语义绿色 | 已启用、健康、已完成。 |
| 警告 | 通过 Badge variant 或未来 token 提供的语义琥珀色 | 待处理、即将过期、已降级。 |
| 危险 | `--destructive` 和 destructive variant | 已禁用、已撤销、失败、破坏性操作。 |
| 信息 | primary/accent 蓝 | 信息指引和选中状态。 |
| 中性 | background、card、muted、border | 默认工作区、被动元数据和表格结构。 |

不得用蓝色表示破坏性或成功状态。

#### 圆角、间距和密度

第 8 阶段应使控件和卡片保持克制的圆角：

1. 当前 Console 的 `--radius` 应解析为 0.5rem，除非 shadcn-vue 上游组件行为需要不同的基值。
2. 表格行使用紧凑的垂直内边距，但控件仍保持无障碍目标尺寸。
3. 页面分区采用无框布局，不使用卡片套卡片。
4. 卡片仅用于单个有边界的模块、表单、对话框和重复项。
5. 管理列表页面仅在表单需要时限制内容宽度；表格可以使用完整工作区宽度。

#### 排版

如果本地 package 设置可用，则采用已配置的 shadcn-vue `geist-sans` 方向。本阶段不得添加外部字体加载。如果没有本地字体设置，则保留系统无衬线字体栈。

排版规则：

1. 页面标题保持紧凑，不使用 hero 级尺寸。
2. 表格文字优先保证易于浏览。
3. 标签使用句式大小写。
4. 按钮标签使用简短动词。
5. 错误消息说明发生了什么以及下一步安全操作。

#### 动效

动效服务于功能：

1. 已有 shadcn/reka 组件 transition 时沿用它们。
2. 不添加页面加载编排动效。
3. 遵循减少动效偏好。
4. 加载状态使用 Skeleton 或 Spinner，不使用动画装饰效果。

### 组件路线图

| 组件或模式 | 优先级 | 来源 | 理由 | 依赖项 |
| --- | --- | --- | --- | --- |
| Table | P0 | shadcn-vue | IAM 用户、角色和会话需要密集浏览。 | `@nerv-iip/ui` 导出和表格用法文档。 |
| Dialog | P0 | shadcn-vue | 创建用户、创建角色、编辑用户和重置密码表单。 | 无障碍标题、焦点陷阱、表单模式。 |
| AlertDialog | P0 | shadcn-vue | 禁用用户和撤销会话的确认。 | 破坏性操作模式。 |
| Checkbox | P0 | shadcn-vue | 在角色编辑器中选择权限。 | Field 和列表分组模式。 |
| Select | P1 | shadcn-vue | 已启用/已撤销状态等筛选器。 | 列表工具栏模式。 |
| Pagination | P1 | shadcn-vue 或局部组合 | 分页 IAM 列表。 | API 分页模型。 |
| Tooltip | P2 | shadcn-vue | 标签变得密集时用于权限代码说明。 | 图标/帮助模式。 |
| Tabs | P2 | shadcn-vue | 仅当 IAM 管理页面共用一个路由时使用；独立路由不需要。 | 导航决策。 |

实施计划必须在添加新组件前运行 shadcn-vue 文档命令，并在导出生成文件前审查这些文件。

### 核心模式

#### 页头

使用包含以下内容的无框页头：

1. 标题。
2. 简短说明。
3. 右侧可选的主要操作。
4. 需要时在下方提供可选的紧凑元数据。

不要用卡片包裹页头。

#### 工具栏

列表页面在表格上方使用工具栏：

1. 搜索输入框。
2. 有用时提供状态筛选器。
3. 主要操作。
4. 桌面端不超过一行，除非筛选器溢出。
5. 移动端使用 `gap-*` 堆叠控件。

#### 数据表格

管理表格使用：

1. 稳定的列。
2. 清晰的空状态。
3. 加载时显示 Skeleton 行。
4. 行内 Badge 状态。
5. 操作超过两个时，把行操作放入 DropdownMenu。
6. 破坏性操作仅可在确认后执行。
7. 移动端仅在没有其他办法时使用水平溢出。

#### 表单

表单使用：

1. `FieldGroup` 和 `Field`。
2. `FieldLabel`、`FieldDescription` 和 `FieldError`。
3. Field 上使用 `data-invalid`，控件上使用 `aria-invalid`。
4. 对话框页脚操作使用主要和次要按钮。
5. 提交后，密码字段绝不回显已生成或已提交的 secret 值。

#### 权限编辑器

角色权限编辑使用：

1. 按领域前缀划分权限组：`iam`、`apphub`、`ops`、`connectors`、`files`。
2. 带代码和说明的 Checkbox 行。
3. 权限代码搜索/筛选。
4. 已选数量摘要。
5. 从管理员角色中移除权限时给出警告。

不得使用自由文本编辑权限。

#### 空、错误和权限状态

使用一等状态：

1. 空列表：中性 Card 或无框 Empty 风格组合，并提供明确的下一步操作。
2. 权限拒绝：包含权限代码和安全说明的 Alert。
3. 加载失败：包含重试操作的 Alert。
4. 部分失败：保持已加载数据可见，并显示非阻断 Alert。
5. 破坏性操作确认：使用明确对象名称的 AlertDialog。

### 无障碍基线

第 8 阶段必须覆盖：

1. 通过导航、工具栏、表格操作和对话框进行键盘导航。
2. 每个对话框都有标题和说明。
3. 破坏性操作确认的 AlertDialog 有标题。
4. 所有搜索和筛选控件都有无障碍标签。
5. 蓝色 ring 上的焦点可见。
6. 不得仅通过颜色传达状态。
7. 表格操作有无障碍名称。
8. 错误消息在行内保持可见；toast 仅作补充。
9. 按钮在移动端保持足够的点击区域。
10. 屏幕阅读器输出避免泄露密码或生成的 secret。

### 文档模型

更新 `docs/architecture/frontend-design-system-planning.md`，加入：

1. 平静控制平面方向。
2. 蓝色主色主题决策。
3. Token 角色映射。
4. shadcn-vue 组件添加规则。
5. 管理列表/表单/对话框/表格模式。
6. 旧版 token 弃用说明。
7. 新 UI 界面的审查门禁。

本规格本身继续作为第 8 阶段的设计产物。本阶段不引入 Storybook。

### 治理

负责人：

1. 前端实施负责 `frontend/packages/ui` 和 Console 应用用法。
2. 架构文档负责设计系统决策和未来迁移说明。
3. 无障碍检查是验证的一部分，不是单独的可选审查。

贡献规则：

1. 新的 shadcn-vue 组件必须通过 CLI 添加。
2. 新组件必须先通过 `@nerv-iip/ui` 导出，再供应用使用。
3. 应用代码不得从 `packages/ui/src/components/ui/*` 深层路径导入。
4. 新的原始 CSS 变量必须具有语义并写入文档。
5. 新 IAM 管理页面不得使用旧版 `--legacy-color-*` token。
6. UI diff 必须为核心状态提供组件测试或 E2E 覆盖。

## 第 8.1 阶段 IAM 管理控制台

### 后端设计

#### IAM 服务补全

补全当前尚不完整的 IAM 管理后端：

1. PostgreSQL 模式下的持久化角色创建。
2. PostgreSQL 模式下的持久化角色权限修补。
3. InMemory 角色变更行为与 PostgreSQL 行为保持一致，不使用硬编码角色 ID。
4. 基于 `NervIipSeedPermissions.All` 和 `docs/architecture/authorization-matrix.md` 说明的权限目录查询。
5. 管理员重置密码命令和 endpoint。

应保留并强化当前用户创建/更新/禁用和会话撤销行为，而不是重写它们。

#### IAM API 形状

IAM endpoint 应暴露：

```text
GET  /api/iam/v1/users
POST /api/iam/v1/users
PATCH /api/iam/v1/users/{userId}
POST /api/iam/v1/users/{userId}/disable
POST /api/iam/v1/users/{userId}/reset-password

GET  /api/iam/v1/roles
POST /api/iam/v1/roles
PATCH /api/iam/v1/roles/{roleId}/permissions
GET  /api/iam/v1/permissions

GET  /api/iam/v1/sessions
POST /api/iam/v1/sessions/{sessionId}/revoke
```

写入 endpoint 需要现有 IAM 权限：

1. 用户创建、更新、禁用和重置密码需要 `iam.users.manage`。
2. 角色创建和权限修补需要 `iam.roles.manage`。
3. 会话撤销需要 `iam.sessions.revoke`。

读取 endpoint 需要：

1. `iam.users.read`
2. `iam.roles.read`
3. `iam.sessions.read`

读取权限目录需要 `iam.roles.read`，因为它用于检查可分配的角色权限。

#### 请求与响应决策

用户重置密码：

```text
request:  { newPassword: string }
response: 204 No Content
```

角色创建：

```text
request:  { roleName: string, permissionCodes: string[] }
response: RoleResponse
```

角色权限修补：

```text
request:  { permissionCodes: string[] }
response: RoleResponse
```

权限目录：

```text
response: {
  items: [
    {
      code: string,
      domain: string,
      description: string,
      seeded: true
    }
  ]
}
```

第 8 阶段的权限目录不应虚构未播种的权限。未来服务权限继续记录在文档中，但在播种之前不可分配。

#### 领域与持久化规则

1. 角色名称为必填项，应去除首尾空白，并在 IAM 服务范围内保持大小写不敏感的唯一性。
2. 权限代码必须属于 `NervIipSeedPermissions.All`。
3. 角色权限修补以原子方式替换角色权限集。
4. 允许更改管理员角色，但测试必须覆盖从唯一平台管理员移除 `iam.roles.manage` 会锁死后续角色编辑的情形。第 8 阶段应在 UI 中发出警告，但不需要复杂的紧急访问模型。
5. 重置密码会更新密码哈希和 security stamp，按需递增权限或安全版本，并撤销该用户的活动会话。
6. 已禁用用户不能登录或刷新。
7. 用户更新的唯一性继续保持大小写不敏感。

#### Gateway Console IAM 管理 Facade

Console 前端继续仅调用 PlatformGateway。

添加 Gateway endpoint：

```text
GET  /api/console/v1/iam/users
POST /api/console/v1/iam/users
PATCH /api/console/v1/iam/users/{userId}
POST /api/console/v1/iam/users/{userId}/disable
POST /api/console/v1/iam/users/{userId}/reset-password

GET  /api/console/v1/iam/roles
POST /api/console/v1/iam/roles
PATCH /api/console/v1/iam/roles/{roleId}/permissions
GET  /api/console/v1/iam/permissions

GET  /api/console/v1/iam/sessions
POST /api/console/v1/iam/sessions/{sessionId}/revoke
```

Gateway 职责：

1. 要求经过认证的 Console bearer token。
2. 转发前使用现有 IAM 支持的授权检查。
3. 将原始 bearer token 转发给 IAM。
4. 保持响应 envelope 和状态码。
5. 将 IAM 不可用映射为 `503`，将意外 IAM 失败映射为 `502`。
6. 避免引用 IAM Domain 或 Infrastructure。

稳定的 operation ID：

```text
listConsoleIamUsers
createConsoleIamUser
updateConsoleIamUser
disableConsoleIamUser
resetConsoleIamUserPassword
listConsoleIamRoles
createConsoleIamRole
updateConsoleIamRolePermissions
listConsoleIamPermissions
listConsoleIamSessions
revokeConsoleIamSession
```

### 前端信息架构

导航从一个条目扩展为管理分组：

```text
实例
IAM
  用户
  角色
  会话
```

路由：

```text
frontend/apps/console/src/pages/iam/users/index.vue
frontend/apps/console/src/pages/iam/roles/index.vue
frontend/apps/console/src/pages/iam/sessions/index.vue
```

所有 IAM 管理路由都需要认证。

第 8 阶段不实现组织或环境切换器。当前 principal 上下文仍是活动组织/环境。

### 前端数据流

1. 后端 endpoint 就绪后导出 Gateway OpenAPI。
2. `frontend/packages/api-client` 重新生成类型、SDK 和 Pinia Colada helper。
3. `frontend/apps/console/src/api/iam.ts` 仅可为参数塑形包装生成的 operation。
4. `frontend/apps/console/src/composables/useIamAdmin.ts` 负责 query/mutation 组合、失效处理和通用错误映射。
5. 页面保持精简并组合功能组件。
6. 组件接收数据并发出类型化事件；它们不直接调用生成的 SDK 函数。

任何页面或组件都不得手写 fetch URL。

### 前端组件

创建职责集中的 IAM 组件：

```text
frontend/apps/console/src/components/iam/IamPageHeader.vue
frontend/apps/console/src/components/iam/IamListToolbar.vue
frontend/apps/console/src/components/iam/UsersTable.vue
frontend/apps/console/src/components/iam/UserCreateDialog.vue
frontend/apps/console/src/components/iam/UserEditDialog.vue
frontend/apps/console/src/components/iam/UserResetPasswordDialog.vue
frontend/apps/console/src/components/iam/RolesTable.vue
frontend/apps/console/src/components/iam/RoleCreateDialog.vue
frontend/apps/console/src/components/iam/RolePermissionEditor.vue
frontend/apps/console/src/components/iam/SessionsTable.vue
frontend/apps/console/src/components/iam/RevokeSessionDialog.vue
frontend/apps/console/src/components/iam/PermissionCodeBadge.vue
```

如果实施时发现某个组件只有三行胶水代码，则将其保留在页面内部，不必创建文件。重要边界是表格、对话框和权限编辑器保持职责集中且可测试。

### 用户页面

能力：

1. 分页列出用户。
2. 按登录名、电子邮件或用户 ID 搜索。
3. 筛选已启用/已禁用状态。
4. 创建用户。
5. 编辑登录名、电子邮件和启用状态。
6. 禁用用户。
7. 重置密码。

状态：

1. 加载 skeleton。
2. 空结果。
3. 验证错误。
4. 权限拒绝。
5. 带重试操作的加载失败。
6. mutation 成功 toast。

表格应显示：

1. 登录名。
2. 电子邮件。
3. 用户 ID。
4. 状态 badge。
5. 操作。

### 角色页面

能力：

1. 分页列出角色。
2. 按角色名称、角色 ID 或权限代码搜索。
3. 使用选定权限创建角色。
4. 编辑现有角色的权限。
5. 检查权限代码分组。

表格应显示：

1. 角色名称。
2. 角色 ID。
3. 权限数量。
4. 关键权限 badge。
5. 操作。

权限编辑器应显示分组权限、搜索和已选数量。它必须阻止未知权限代码。

### 会话页面

能力：

1. 分页列出会话。
2. 按会话 ID 或用户 ID 搜索。
3. 筛选活动/已撤销状态。
4. 撤销活动会话。

表格应显示：

1. 会话 ID。
2. 用户 ID。
3. 签发时间。
4. 到期时间。
5. 撤销时间或活动状态。
6. 权限版本。
7. 操作。

撤销当前用户的当前会话时，应警告用户可能会被登出。

### 错误处理

后端：

1. 验证错误通过现有响应 envelope/problem 形状返回 400。
2. 未认证返回 401。
3. 权限拒绝返回 403。
4. 未知用户、角色或会话返回 404。
5. 重复角色/用户冲突返回 409。
6. Gateway 发现 IAM 不可用时返回 503。
7. Gateway 遇到意外下游失败时返回 502。

前端：

1. `401` 清除认证并重定向到登录页。
2. `403` 呈现权限拒绝状态，但不清除认证。
3. `409` 呈现字段级或对话框级冲突消息。
4. `404` 使列表 query 失效并显示 mutation 专用消息。
5. 网络失败时，如有 stale 数据则保持其可见，并显示重试操作。
6. 破坏性操作失败后，如果用户可以重试，则保持对话框打开。

### 安全与隐私

1. 绝不记录密码值。
2. 重置密码对话框在关闭或提交后清除本地状态。
3. 本阶段不引入生成密码。
4. 角色编辑器不暴露尚未播种的未来权限。
5. Gateway 不得绕过管理 endpoint 的 IAM 授权检查。
6. 用户和角色 mutation 应创建便于审计的日志，包含 correlation ID、操作和目标 ID，但不包含 secret。

### 测试策略

后端 IAM 测试：

1. PostgreSQL 角色创建会持久化角色和权限。
2. PostgreSQL 角色权限修补会原子替换权限。
3. 拒绝未知权限代码。
4. 以大小写不敏感方式拒绝重复角色名称。
5. 重置密码会更改密码、撤销旧会话，并允许使用新密码登录。
6. 用户 CRUD 和会话撤销测试继续通过。

Gateway 测试：

1. 每个 Console IAM endpoint 都需要 bearer 认证。
2. 每个 endpoint 都映射到正确的 IAM 权限代码。
3. 授权被拒后不调用下游 IAM。
4. Gateway 保持响应 envelope 和状态码。
5. Gateway OpenAPI 暴露稳定的 operation ID。

API client 测试：

1. 新生成的 operation 通过稳定的 package 入口点导出。
2. Bearer 注入适用于 IAM 管理 operation。
3. 错误响应仍可由应用 wrapper 消费。

Vue 单元/组件测试：

1. 用户页面呈现加载、空、数据、错误和权限拒绝状态。
2. 用户对话框验证必填字段并发出类型化提交事件。
3. 角色页面加载权限目录并显示分组权限 checkbox。
4. 角色权限编辑器筛选权限并跟踪已选数量。
5. 会话页面在确认后撤销会话。
6. 导航包含供已认证用户使用的 IAM 管理路由。

E2E：

1. 已播种管理员登录。
2. 管理员打开用户页面，创建、编辑并禁用用户。
3. 管理员打开角色页面，创建角色并更新权限。
4. 管理员打开会话页面并撤销非当前会话；如果不存在可撤销会话，则验证撤销 affordance（操作提示）。
5. 权限拒绝 fixture 显示安全的 403 状态。

视觉/浏览器验证：

1. 桌面端 IAM 用户页面。
2. 桌面端角色权限编辑器。
3. 桌面端会话页面。
4. 移动端用户页面。
5. 对话框焦点和破坏性操作确认。
6. 蓝色主题体现为主要操作/焦点/选择，而不是单调的全页颜色。

### 验证命令

实施的预期门禁：

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
pwsh scripts/verify-iam-persistent-auth-foundation.ps1
pwsh scripts/verify-third-slice-console.ps1
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

如果实施修改脚本，则运行：

```powershell
pwsh scripts/check-script-governance.ps1
```

## 文档更新

更新：

1. `docs/architecture/frontend-design-system-planning.md`：选定的蓝色平静控制平面基线、token 角色映射、shadcn 组件治理和当前模式。
2. `docs/architecture/frontend-structure.md`：IAM 管理路由、composable 边界、生成客户端用法和设计系统消费规则。
3. `docs/architecture/iam-authentication-baseline.md`：角色变更、用户重置密码和管理 Console 状态。
4. `docs/architecture/authorization-matrix.md`：权限目录状态和 IAM 管理 endpoint 强制执行状态。
5. `docs/architecture/api-contract-and-codegen.md`：Console IAM Admin facade operation ID。
6. `docs/architecture/implementation-readiness.md`：验证通过后的第 8 阶段完成状态。
7. `README.md`：第 8 阶段实施完成后的下一阶段状态。

## 推出顺序

1. 更新设计系统规划文档和 token 决策。
2. 添加所需 shadcn-vue 组件，并通过 `@nerv-iip/ui` 导出。
3. 补全 IAM 角色变更和权限目录。
4. 添加用户重置密码 endpoint。
5. 添加 Gateway Console IAM Admin facade 和 OpenAPI 测试。
6. 导出 OpenAPI 并重新生成 api-client。
7. 构建 `useIamAdmin` composable 和 IAM 管理页面。
8. 添加 E2E 和浏览器验证。
9. 仅在验证通过后更新 readiness 文档。

## 验收标准

1. Console 具有写入文档的蓝色平静控制平面设计系统基线。
2. 新 IAM 管理 UI 通过 `@nerv-iip/ui` 使用 shadcn-vue 组件。
3. 任何新 IAM 管理页面都不使用 `--legacy-color-*` token。
4. 已实现 PostgreSQL 角色创建和角色权限修补。
5. 权限目录仅暴露已播种权限。
6. 管理员可以通过 Console 创建、编辑、禁用用户并重置其密码。
7. 管理员可以通过 Console 创建角色并编辑权限。
8. 管理员可以通过 Console 查看会话并撤销会话。
9. Gateway 在转发管理 facade 调用前强制执行 IAM 权限。
10. OpenAPI snapshot 和生成的 api-client 包含稳定的 IAM 管理 operation。
11. 单元、集成、前端和 E2E 测试覆盖主要工作流。
12. 浏览器验证确认桌面端/移动端布局、对话框焦点且无文字重叠。

## 未来工作

1. 组织和环境切换。
2. 成员资格管理。
3. 外部客户端和 AuthorizationGrant 管理。
4. OAuth/OIDC 和 SSO。
5. MFA 和 WebAuthn。
6. Connector Host bearer-token 迁移。
7. Notification 和高风险 Ops 审批集成。
8. 视觉回归测试以及 Storybook 或同类组件文档。
9. 暗色模式或租户品牌。

## 自我审查

完整性检查：不存在未完成章节。

内部一致性：Console 继续仅调用 Gateway；IAM 仍是身份和权限事实所有者；shadcn-vue 仍是组件来源；蓝色主色 token 具有语义，而不是原始样式指令。

范围检查：这属于同一阶段，因为第 8.0 阶段是第 8.1 阶段的设计系统前置条件，两者共同交付一个连贯的产品界面：Console 中的 IAM 管理。OAuth、ABAC、Connector Host bearer 迁移、FileStorage、Notification 和发布安装器均明确在范围外。

歧义检查：本规格定义了 token 角色、组件治理、后端 endpoint、Gateway facade operation ID、前端路由、错误处理、测试和验收标准。
