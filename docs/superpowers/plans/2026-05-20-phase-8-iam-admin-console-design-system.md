# Phase 8 IAM 管理控制台和设计系统实施计划

> **状态：Phase 8 已完成。** 该计划保留原始 `- [ ]` 任务清单作为执行记录；最终交付状态、验证结果和环境阻塞项记录在 `docs/architecture/implementation-readiness.md`。

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**交付 Phase 8 蓝色 Calm Control Plane 设计系统基线，以及面向用户、角色、权限和会话的 Console IAM 管理工作流。

**架构：**Console 继续只调用 PlatformGateway。PlatformGateway 暴露 Console IAM 管理门面端点，使用当前主体的组织/环境检查由 IAM 支持的权限，将原始 bearer 令牌转发给 IAM，并以一致方式映射下游失败。IAM 继续作为身份、角色、权限和会话事实的来源；前端通过稳定的 `@nerv-iip/api-client` 导出使用生成的 Gateway OpenAPI 类型，并由聚焦的功能组件组合成轻量 Vue 路由页面。

**技术栈：**.NET 10、FastEndpoints、MediatR、NetCorePal/CleanDDD、Entity Framework Core、xUnit、ASP.NET Core `WebApplicationFactory`、PostgreSQL 配置档测试、Vue 3 `<script setup lang="ts">`、Vue Router 文件路由、Pinia、Pinia Colada、Vite、Vitest、Playwright、Tailwind CSS v4、shadcn-vue `reka-nova`、lucide-vue-next、Hey API OpenAPI TypeScript。

---

## 已批准规格

实施来源：`docs/superpowers/specs/2026-05-20-iam-admin-console-design-system-design.md`。

该规格选择方案 A：**完成 IAM 管理控制台与角色权限**，并先建立当前阶段的设计系统基线。前一日 2026-05-19 的提交已交付持久化用户 CRUD、会话列表/撤销、Gateway 权限强制执行、Console 身份验证和 shadcn-vue 引导。因此，Phase 8 必须聚焦剩余的角色/权限变更、密码重置、Console 管理门面和前端管理界面。

## 当前基线

1. 当前分支为 `codex/phase-8-iam-admin-design-system-spec`。
2. `frontend/components.json` 已存在，并使用 `style: reka-nova`、`font: geist-sans`、Tailwind v4 CSS 文件 `apps/console/src/assets/main.css`、`iconLibrary: lucide`，且别名指向 `packages/ui`。
3. `pnpm dlx shadcn-vue@latest docs table dialog alert-dialog checkbox select pagination empty` 当前从 `frontend` 运行时会报 `Failed to load tsconfig.json` 并失败，因为工作区有 `tsconfig.base.json` 和包级配置，但没有根级 `frontend/tsconfig.json`。
4. 现有 UI 导出包括 Button、Card、Field、Input、Alert、Badge、Separator、Skeleton、DropdownMenu、Avatar、Toaster 和 Spinner。
5. `frontend/apps/console/src/assets/main.css` 仍包含中性的 shadcn 主令牌和旧版兼容令牌。新的 IAM 页面必须使用语义化 shadcn/Tailwind 令牌，而不是 `--legacy-color-*`。
6. 对于 PostgreSQL 的角色创建/权限更新，`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Roles/IamRoleApplicationService.cs` 返回虚拟内存角色 ID 和 `501`。
7. IAM 用户创建/更新/禁用和会话列表/撤销已存在。用户密码重置尚不存在。
8. PlatformGateway 已有 Console 身份验证端点和实例/操作端点，但没有 Console IAM 管理门面。
9. `frontend/packages/api-client` 已从 `frontend/packages/api-client/openapi/platform-gateway.v1.json` 生成 fetch SDK、TypeScript 类型和 Pinia Colada 选项。

## 文件结构图

```text
backend/services/Iam/src/Nerv.IIP.Iam.Domain/
  IamFacts.cs
  AggregatesModel/RoleAggregate/Role.cs
  AggregatesModel/UserAggregate/User.cs
  AggregatesModel/UserSessionAggregate/UserSession.cs

backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/
  ApplicationDbContext.cs
  InMemoryIamStore.cs
  Repositories/IamRepositories.cs
  EntityConfigurations/RoleEntityTypeConfiguration.cs
  EntityConfigurations/UserEntityTypeConfiguration.cs
  EntityConfigurations/UserSessionEntityTypeConfiguration.cs

backend/services/Iam/src/Nerv.IIP.Iam.Web/
  Application/Roles/IamRoleApplicationService.cs
  Application/Users/IamUserApplicationService.cs
  Application/Commands/Users/CreateUserCommand.cs
  Application/Commands/Users/UpdateUserCommand.cs
  Application/Commands/Users/DisableUserCommand.cs
  Application/Commands/Users/ResetUserPasswordCommand.cs
  Application/Permissions/IamPermissionCatalog.cs
  Application/Sessions/IamSessionApplicationService.cs
  Endpoints/Roles/RoleEndpoints.cs
  Endpoints/Users/UserEndpoints.cs
  Endpoints/Sessions/SessionEndpoints.cs

backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/
  IamFoundationTests.cs
  IamPostgresProfileTests.cs
  IamManagementEndpointAuthorizationTests.cs

backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/
  Program.cs
  Application/Auth/ConsoleAuthModels.cs
  Application/Auth/GatewayAuthorization.cs
  Application/Auth/GatewayAuthorizationClient.cs
  Application/IamAdmin/ConsoleIamAdminModels.cs
  Application/IamAdmin/GatewayIamAdminClient.cs
  Endpoints/IamAdmin/ConsoleIamAdminEndpoints.cs

backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/
  GatewayAuthorizationTests.cs
  GatewayConsoleIamAdminTests.cs
  GatewayOpenApiTests.cs

frontend/
  tsconfig.json
  components.json
  package.json
  packages/ui/src/index.ts
  packages/ui/src/design-system.contract.test.ts
  packages/ui/src/components/ui/{table,dialog,alert-dialog,checkbox,select,pagination,empty}/**
  packages/app-shell/src/AppShell.vue
  packages/app-shell/src/AppShell.test.ts
  packages/api-client/openapi/platform-gateway.v1.json
  packages/api-client/src/generated/**
  packages/api-client/src/iam.ts
  packages/api-client/src/index.ts

frontend/apps/console/src/
  assets/main.css
  api/iam.ts
  composables/useIamAdmin.ts
  composables/useIamAdmin.test.ts
  components/iam/IamPageHeader.vue
  components/iam/IamListToolbar.vue
  components/iam/PermissionCodeBadge.vue
  components/iam/UsersTable.vue
  components/iam/UserCreateDialog.vue
  components/iam/UserEditDialog.vue
  components/iam/UserResetPasswordDialog.vue
  components/iam/RolesTable.vue
  components/iam/RoleCreateDialog.vue
  components/iam/RolePermissionEditor.vue
  components/iam/SessionsTable.vue
  components/iam/RevokeSessionDialog.vue
  layouts/DefaultLayout.vue
  layouts/DefaultLayout.test.ts
  pages/iam/users/index.vue
  pages/iam/users/index.test.ts
  pages/iam/roles/index.vue
  pages/iam/roles/index.test.ts
  pages/iam/sessions/index.vue
  pages/iam/sessions/index.test.ts

frontend/apps/console/e2e/
  console.spec.ts
  iam-admin.spec.ts

docs/architecture/
  frontend-design-system-planning.md
  frontend-structure.md
  iam-authentication-baseline.md
  authorization-matrix.md
  api-contract-and-codegen.md
  implementation-readiness.md

README.md
```

## 实施规则

1. 生产行为使用 TDD：编写会失败的测试，运行并确认出现预期失败，然后实施能够通过测试的最小代码。
2. 处理 shadcn-vue 时，从 `frontend` 运行 CLI 命令，审核生成文件，并在应用使用前通过 `@nerv-iip/ui` 导出新组件。
3. Vue 文件使用 Composition API 和 `<script setup lang="ts">`；路由页面保持轻量，功能逻辑放在 `useIamAdmin.ts` 中。
4. 新的 IAM 管理 UI 只能从 `@nerv-iip/ui` 导入 UI 原语；应用代码不得从 `frontend/packages/ui/src/components/ui/*` 深度导入。
5. 新的 IAM 管理 UI 使用 `bg-background`、`text-muted-foreground`、`border-border`、`bg-primary`、`ring-ring` 等语义化 Tailwind/shadcn 令牌和组件变体。不得使用 `--legacy-color-*`。
6. IAM 应用服务传递 `CancellationToken`，业务失败使用 `KnownException`，且不手工调用 `SaveChanges`。
7. Gateway 管理端点必须先调用 IAM 授权，再将管理请求转发给 IAM。

## 任务 1：建立蓝色设计系统基线和 shadcn 组件

**文件：**

- 创建：`frontend/tsconfig.json`
- 创建：`frontend/packages/ui/src/design-system.contract.test.ts`
- 修改：`frontend/apps/console/src/assets/main.css`
- 修改：`frontend/packages/ui/src/index.ts`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/table/**`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/dialog/**`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/alert-dialog/**`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/checkbox/**`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/select/**`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/pagination/**`
- 通过 CLI 添加：`frontend/packages/ui/src/components/ui/empty/**`
- 修改：`docs/architecture/frontend-design-system-planning.md`

- [ ] **步骤 1：编写会失败的设计系统契约测试**

创建 `frontend/packages/ui/src/design-system.contract.test.ts`：

```ts
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const frontendRoot = fileURLToPath(new URL('../../..', import.meta.url))
const cssPath = `${frontendRoot}/apps/console/src/assets/main.css`

describe('Console design-system contract', () => {
  const css = readFileSync(cssPath, 'utf8')

  it('uses the Phase 8 blue primary token mapping', () => {
    expect(css).toContain('--primary: oklch(0.49 0.17 255);')
    expect(css).toContain('--primary-foreground: oklch(0.985 0 0);')
    expect(css).toContain('--ring: oklch(0.62 0.15 255);')
    expect(css).toContain('--accent: oklch(0.96 0.03 255);')
    expect(css).toContain('--accent-foreground: oklch(0.28 0.11 255);')
    expect(css).toContain('--sidebar-primary: var(--primary);')
    expect(css).toContain('--chart-1: oklch(0.58 0.16 255);')
    expect(css).toContain('--radius: 0.5rem;')
  })

  it('keeps legacy tokens as compatibility tokens only', () => {
    expect(css).toContain('--legacy-color-page:')
    expect(css).toContain('@theme inline')
  })
})
```

- [ ] **步骤 2：运行设计系统契约测试并确认测试为红**

运行：

```powershell
pnpm -C frontend test packages/ui/src/design-system.contract.test.ts
```

预期：失败，因为 `--primary`、`--ring`、`--accent`、`--sidebar-primary`、`--chart-1` 和 `--radius` 仍使用中性基线值。

- [ ] **步骤 3：为 shadcn-vue CLI 添加根级 TypeScript 配置**

创建 `frontend/tsconfig.json`：

```json
{
  "extends": "./tsconfig.base.json",
  "include": [
    "apps/**/*.ts",
    "apps/**/*.vue",
    "packages/**/*.ts",
    "packages/**/*.vue"
  ]
}
```

- [ ] **步骤 4：验证 shadcn-vue 项目上下文**

运行：

```powershell
pnpm -C frontend dlx shadcn-vue@latest info --json
pnpm -C frontend dlx shadcn-vue@latest docs table dialog alert-dialog checkbox select pagination empty
```

预期：`info --json` 报告 `reka-nova`、Tailwind v4、`lucide`，以及解析到 `packages/ui/src/components/ui` 下的 UI 路径。文档命令输出组件文档 URL。如果文档命令仍然失败，则运行下一步的组件添加命令，并在导出前审核每个生成文件。

- [ ] **步骤 5：添加必需的 shadcn-vue 组件**

运行：

```powershell
pnpm -C frontend dlx shadcn-vue@latest add table dialog alert-dialog checkbox select pagination empty
```

预期：组件源文件添加到 `frontend/packages/ui/src/components/ui` 下。

- [ ] **步骤 6：导出新的 UI 原语**

修改 `frontend/packages/ui/src/index.ts`，在现有组件导出之后添加以下导出：

```ts
export {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableEmpty,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from './components/ui/table'
export {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from './components/ui/dialog'
export {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from './components/ui/alert-dialog'
export { Checkbox } from './components/ui/checkbox'
export {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectScrollDownButton,
  SelectScrollUpButton,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from './components/ui/select'
export {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationFirst,
  PaginationItem,
  PaginationLast,
  PaginationNext,
  PaginationPrevious,
} from './components/ui/pagination'
export {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from './components/ui/empty'
```

如果生成的 `index.ts` 文件暴露了略有不同的组件名称，请使用生成的准确导出，并确保每个生成子组件的公开汇总导出完整。

- [ ] **步骤 7：应用 Phase 8 蓝色令牌**

在 `frontend/apps/console/src/assets/main.css` 中，将 `:root` 里当前的中性 shadcn 令牌值替换为：

```css
  --primary: oklch(0.49 0.17 255);
  --primary-foreground: oklch(0.985 0 0);
  --secondary: oklch(0.97 0 0);
  --secondary-foreground: oklch(0.205 0 0);
  --muted: oklch(0.97 0 0);
  --muted-foreground: oklch(0.556 0 0);
  --accent: oklch(0.96 0.03 255);
  --accent-foreground: oklch(0.28 0.11 255);
  --destructive: oklch(0.577 0.245 27.325);
  --border: oklch(0.922 0 0);
  --input: oklch(0.922 0 0);
  --ring: oklch(0.62 0.15 255);
  --chart-1: oklch(0.58 0.16 255);
  --chart-2: oklch(0.62 0.13 160);
  --chart-3: oklch(0.72 0.16 80);
  --chart-4: oklch(0.64 0.18 35);
  --chart-5: oklch(0.55 0.12 300);
  --radius: 0.5rem;
  --sidebar: oklch(0.985 0 0);
  --sidebar-foreground: oklch(0.145 0 0);
  --sidebar-primary: var(--primary);
  --sidebar-primary-foreground: var(--primary-foreground);
  --sidebar-accent: oklch(0.96 0.03 255);
  --sidebar-accent-foreground: oklch(0.28 0.11 255);
  --sidebar-border: oklch(0.922 0 0);
  --sidebar-ring: var(--ring);
```

保留 `:root` 顶部现有的旧版令牌块，使旧实例页面在 Phase 8 页面迁移到语义化令牌的同时继续正常渲染。

- [ ] **步骤 8：运行契约测试和 UI 包类型检查**

运行：

```powershell
pnpm -C frontend test packages/ui/src/design-system.contract.test.ts
pnpm -C frontend --filter @nerv-iip/ui typecheck
```

预期：通过。

- [ ] **步骤 9：记录设计系统基线**

使用以下具体章节更新 `docs/architecture/frontend-design-system-planning.md`：

```markdown
## Phase 8 Current Baseline

Phase 8 selects Calm Control Plane as the Console design direction. The primary theme is blue, implemented through shadcn semantic tokens in `frontend/apps/console/src/assets/main.css`.

## Token Contract

`--primary`, `--ring`, `--accent`, `--sidebar-primary` and `--chart-1` carry the blue action and orientation language. Success, warning and danger states remain separate from blue and use Badge variants or destructive tokens.

## Component Governance

New shadcn-vue components are added with `pnpm -C frontend dlx shadcn-vue@latest add <component>`, reviewed in `frontend/packages/ui/src/components/ui`, and exported from `frontend/packages/ui/src/index.ts` before Console app usage. Console app code imports from `@nerv-iip/ui` only.

## IAM Admin Patterns

IAM admin pages use unframed page headers, compact toolbars, shadcn Table for dense scanning, Dialog for forms, AlertDialog for destructive confirmation, FieldGroup and Field for forms, Checkbox for permission selection, Select for filters, Pagination for paged lists, Empty for empty states, Alert for failures and Badge for status.
```

- [ ] **步骤 10：提交设计系统基线**

运行：

```powershell
git add frontend/tsconfig.json frontend/apps/console/src/assets/main.css frontend/packages/ui docs/architecture/frontend-design-system-planning.md
git commit -m "feat: establish phase 8 console design system"
```

预期：提交成功。

## 任务 2：完成 IAM 角色变更、权限目录和密码重置

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/InMemoryIamStore.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Roles/IamRoleApplicationService.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Users/IamUserApplicationService.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Commands/Users/ResetUserPasswordCommand.cs`
- 创建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Permissions/IamPermissionCatalog.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs`
- 修改：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamFoundationTests.cs`
- 修改：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`
- 修改：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamManagementEndpointAuthorizationTests.cs`

- [ ] **步骤 1：编写会失败的内存角色和权限测试**

在 `IamFoundationTests.cs` 中添加：

```csharp
[Fact]
public async Task In_memory_role_management_creates_role_updates_permissions_and_lists_catalog()
{
    var catalogResponse = await _client.GetAsync("/api/iam/v1/permissions");
    catalogResponse.EnsureSuccessStatusCode();
    var catalog = await ReadResponseDataAsync<PermissionCatalogResponse>(catalogResponse);
    Assert.Contains(catalog!.Items, item => item.Code == "iam.roles.manage" && item.Domain == "iam");

    var create = await _client.PostAsJsonAsync(
        "/api/iam/v1/roles",
        new { roleName = "Operator", permissionCodes = new[] { "apphub.instances.read", "ops.tasks.read" } });
    Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    var created = await ReadResponseDataAsync<RoleResponse>(create);

    Assert.StartsWith("role-", created!.RoleId, StringComparison.Ordinal);
    Assert.Equal("Operator", created.RoleName);
    Assert.Equal(["apphub.instances.read", "ops.tasks.read"], created.PermissionCodes.Order().ToArray());

    var patch = await _client.PatchAsJsonAsync(
        $"/api/iam/v1/roles/{created.RoleId}/permissions",
        new { permissionCodes = new[] { "iam.users.read" } });
    patch.EnsureSuccessStatusCode();
    var updated = await ReadResponseDataAsync<RoleResponse>(patch);

    Assert.Equal(created.RoleId, updated!.RoleId);
    Assert.Equal(["iam.users.read"], updated.PermissionCodes);
}

[Fact]
public async Task In_memory_role_management_rejects_unknown_permissions_and_duplicate_names()
{
    var create = await _client.PostAsJsonAsync(
        "/api/iam/v1/roles",
        new { roleName = "Auditor", permissionCodes = new[] { "iam.users.read" } });
    Assert.Equal(HttpStatusCode.Created, create.StatusCode);

    var duplicate = await _client.PostAsJsonAsync(
        "/api/iam/v1/roles",
        new { roleName = "auditor", permissionCodes = Array.Empty<string>() });
    Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

    var unknown = await _client.PostAsJsonAsync(
        "/api/iam/v1/roles",
        new { roleName = "BadRole", permissionCodes = new[] { "iam.unknown" } });
    Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
}

private sealed record RoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
private sealed record PermissionCatalogResponse(IReadOnlyList<PermissionCatalogItemResponse> Items);
private sealed record PermissionCatalogItemResponse(string Code, string Domain, string Description, bool Seeded);
```

- [ ] **步骤 2：编写会失败的密码重置测试**

在 `IamFoundationTests.cs` 中添加：

```csharp
[Fact]
public async Task Admin_reset_password_changes_login_secret_and_revokes_sessions()
{
    var create = await _client.PostAsJsonAsync(
        "/api/iam/v1/users",
        new { loginName = "reset-user", email = "reset-user@nerv-iip.local", password = "OldPassword123!" });
    create.EnsureSuccessStatusCode();
    var user = await ReadResponseDataAsync<UserResponse>(create);

    var login = await _client.PostAsJsonAsync(
        "/api/iam/v1/auth/login",
        new { loginName = "reset-user", password = "OldPassword123!" });
    login.EnsureSuccessStatusCode();
    var session = await ReadResponseDataAsync<AuthResponse>(login);

    var reset = await _client.PostAsJsonAsync(
        $"/api/iam/v1/users/{user!.UserId}/reset-password",
        new { newPassword = "NewPassword123!" });
    Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

    _client.DefaultRequestHeaders.Authorization = new("Bearer", session!.AccessToken);
    var staleMe = await _client.GetAsync("/api/iam/v1/me");
    Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);
    _client.DefaultRequestHeaders.Authorization = null;

    var oldLogin = await _client.PostAsJsonAsync(
        "/api/iam/v1/auth/login",
        new { loginName = "reset-user", password = "OldPassword123!" });
    Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

    var newLogin = await _client.PostAsJsonAsync(
        "/api/iam/v1/auth/login",
        new { loginName = "reset-user", password = "NewPassword123!" });
    newLogin.EnsureSuccessStatusCode();
}
```

- [ ] **步骤 3：运行 IAM 测试并确认测试为红**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter "In_memory_role_management_creates_role_updates_permissions_and_lists_catalog|In_memory_role_management_rejects_unknown_permissions_and_duplicate_names|Admin_reset_password_changes_login_secret_and_revokes_sessions"
```

预期：因缺少 `/api/iam/v1/permissions`、当前返回虚拟角色响应或缺少 `/reset-password` 而失败。

- [ ] **步骤 4：添加权限目录模型**

创建 `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Permissions/IamPermissionCatalog.cs`：

```csharp
using Nerv.IIP.Iam.Domain;

namespace Nerv.IIP.Iam.Web.Application.Permissions;

public sealed record PermissionCatalogResponse(IReadOnlyList<PermissionCatalogItemResponse> Items);
public sealed record PermissionCatalogItemResponse(string Code, string Domain, string Description, bool Seeded);

public static class IamPermissionCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["iam.users.read"] = "Read IAM users.",
        ["iam.users.manage"] = "Create, update, disable and reset IAM users.",
        ["iam.roles.read"] = "Read IAM roles and permission catalog.",
        ["iam.roles.manage"] = "Create IAM roles and update role permissions.",
        ["iam.sessions.read"] = "Read IAM user sessions.",
        ["iam.sessions.revoke"] = "Revoke IAM user sessions.",
        ["connectors.registrations.write"] = "Register connector hosts.",
        ["connectors.heartbeats.write"] = "Write connector host heartbeats.",
        ["connectors.state-snapshots.write"] = "Write connector host state snapshots.",
        ["apphub.instances.read"] = "Read AppHub application instances.",
        ["files.upload"] = "Upload files.",
        ["files.read"] = "Read file metadata.",
        ["files.download-grants.create"] = "Create file download grants.",
        ["files.archive"] = "Archive files.",
        ["ops.tasks.create"] = "Create operation tasks.",
        ["ops.tasks.read"] = "Read operation tasks.",
        ["ops.results.write"] = "Write operation results.",
        ["ops.audit.read"] = "Read operation audit records."
    };

    public static IReadOnlySet<string> SeededCodes { get; } =
        NervIipSeedPermissions.All.ToHashSet(StringComparer.Ordinal);

    public static PermissionCatalogResponse List()
    {
        var items = NervIipSeedPermissions.All
            .Order(StringComparer.Ordinal)
            .Select(code => new PermissionCatalogItemResponse(
                code,
                GetDomain(code),
                Descriptions[code],
                true))
            .ToArray();

        return new PermissionCatalogResponse(items);
    }

    public static void EnsureSeeded(IEnumerable<string> permissionCodes)
    {
        var unknown = permissionCodes
            .Where(code => !SeededCodes.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (unknown.Length > 0)
        {
            throw new KnownException($"Unknown permission code '{unknown[0]}'.");
        }
    }

    private static string GetDomain(string code)
    {
        var separator = code.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 ? code[..separator] : "platform";
    }
}
```

- [ ] **步骤 5：扩展内存存储以支持角色和密码重置**

在 `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/InMemoryIamStore.cs` 中添加以下方法：

```csharp
public RoleFact CreateRole(string roleName, IEnumerable<string> permissionCodes)
{
    lock (_gate)
    {
        EnsureRoleNameIsUnique(null, roleName);
        var role = new RoleFact(
            $"role-{Guid.NewGuid():N}",
            roleName.Trim(),
            permissionCodes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal));
        _roles.Add(role);
        return role;
    }
}

public RoleFact ReplaceRolePermissions(string roleId, IEnumerable<string> permissionCodes)
{
    lock (_gate)
    {
        var role = _roles.SingleOrDefault(x => x.RoleId == roleId)
            ?? throw new InvalidOperationException($"Role '{roleId}' was not found.");
        var updated = role with
        {
            PermissionCodes = permissionCodes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal)
        };
        _roles[_roles.IndexOf(role)] = updated;
        return updated;
    }
}

public void ResetPassword(string userId, string password)
{
    lock (_gate)
    {
        var user = _users.SingleOrDefault(x => x.UserId == userId)
            ?? throw new InvalidOperationException($"User '{userId}' was not found.");
        _users[_users.IndexOf(user)] = user with
        {
            PasswordHash = Hash(password),
            SecurityStamp = Guid.NewGuid().ToString("n"),
            PermissionVersion = user.PermissionVersion + 1
        };

        foreach (var session in _sessions.Where(x => x.UserId == userId && x.RevokedAtUtc is null).ToArray())
        {
            _sessions[_sessions.IndexOf(session)] = session with { RevokedAtUtc = DateTimeOffset.UtcNow };
        }
    }
}

private void EnsureRoleNameIsUnique(string? currentRoleId, string roleName)
{
    if (_roles.Any(x => x.RoleId != currentRoleId && string.Equals(x.RoleName, roleName, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException($"Role name '{roleName}' is already used.");
    }
}
```

- [ ] **步骤 6：扩展角色仓储**

在 `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs` 中扩展 `IRoleRepository`：

```csharp
Task<Role?> GetByIdAsync(RoleId roleId, CancellationToken cancellationToken = default);
Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default);
```

在 `RoleRepository` 中添加实现：

```csharp
public async Task<Role?> GetByIdAsync(RoleId roleId, CancellationToken cancellationToken = default)
{
    return await DbContext.Roles
        .Include(x => x.Permissions)
        .SingleOrDefaultAsync(x => x.Id == roleId && x.Deleted == NotDeleted, cancellationToken);
}

public async Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default)
{
    var normalizedRoleName = roleName.ToLower();
    return await DbContext.Roles
        .Include(x => x.Permissions)
        .SingleOrDefaultAsync(
            x => x.RoleName.ToLower() == normalizedRoleName && x.Deleted == NotDeleted,
            cancellationToken);
}
```

扩展 `IUserSessionRepository`：

```csharp
Task<IReadOnlyList<UserSession>> ListActiveByUserIdAsync(UserId userId, DateTimeOffset now, CancellationToken cancellationToken = default);
```

添加实现：

```csharp
public async Task<IReadOnlyList<UserSession>> ListActiveByUserIdAsync(
    UserId userId,
    DateTimeOffset now,
    CancellationToken cancellationToken = default)
{
    return await DbContext.UserSessions
        .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now)
        .OrderByDescending(x => x.IssuedAtUtc)
        .ToListAsync(cancellationToken);
}
```

- [ ] **步骤 7：替换角色变更服务契约**

在 `IamRoleApplicationService.cs` 中，将变更记录类型和接口方法替换为：

```csharp
public sealed record RoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record CreateRoleRequest(string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record PatchRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

public interface IIamRoleApplicationService
{
    Task<PagedListResponse<RoleResponse>> ListRolesAsync(IamListQueryOptions options, CancellationToken cancellationToken);
    Task<RoleResponse> CreateRoleAsync(string roleName, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken);
    Task<RoleResponse> PatchRolePermissionsAsync(string roleId, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken);
}
```

在 `InMemoryIamRoleApplicationService` 中实施：

```csharp
public Task<RoleResponse> CreateRoleAsync(string roleName, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken)
{
    IamPermissionCatalog.EnsureSeeded(permissionCodes);
    return Task.FromResult(ToResponse(store.CreateRole(roleName, permissionCodes)));
}

public Task<RoleResponse> PatchRolePermissionsAsync(string roleId, IReadOnlyList<string> permissionCodes, CancellationToken cancellationToken)
{
    IamPermissionCatalog.EnsureSeeded(permissionCodes);
    return Task.FromResult(ToResponse(store.ReplaceRolePermissions(roleId, permissionCodes)));
}

private static RoleResponse ToResponse(RoleFact role)
{
    return new RoleResponse(role.RoleId, role.RoleName, role.PermissionCodes.Order(StringComparer.Ordinal).ToArray());
}
```

在 `PostgreSqlIamRoleApplicationService` 中实施：

```csharp
public async Task<RoleResponse> CreateRoleAsync(
    string roleName,
    IReadOnlyList<string> permissionCodes,
    CancellationToken cancellationToken)
{
    var trimmedRoleName = roleName.Trim();
    if (string.IsNullOrWhiteSpace(trimmedRoleName))
    {
        throw new KnownException("Role name is required.");
    }

    IamPermissionCatalog.EnsureSeeded(permissionCodes);
    if (await repository.GetByNameAsync(trimmedRoleName, cancellationToken) is not null)
    {
        throw new KnownException($"Role name '{trimmedRoleName}' is already used.");
    }

    var role = new Role(
        new RoleId($"role-{Guid.CreateVersion7():N}"),
        trimmedRoleName,
        permissionCodes);
    await repository.AddAsync(role, cancellationToken);
    return ToResponse(role);
}

public async Task<RoleResponse> PatchRolePermissionsAsync(
    string roleId,
    IReadOnlyList<string> permissionCodes,
    CancellationToken cancellationToken)
{
    IamPermissionCatalog.EnsureSeeded(permissionCodes);
    var role = await repository.GetByIdAsync(new RoleId(roleId), cancellationToken)
        ?? throw new KnownException($"Role '{roleId}' was not found.");

    role.ReplacePermissions(permissionCodes);
    return ToResponse(role);
}

private static RoleResponse ToResponse(Role role)
{
    return new RoleResponse(
        role.Id.Id,
        role.RoleName,
        role.Permissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToArray());
}
```

- [ ] **步骤 8：添加用户密码重置命令和服务方法**

扩展 `IIamUserApplicationService`（位于 `IamUserApplicationService.cs`）：

```csharp
Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken);
```

在 `InMemoryIamUserApplicationService` 中添加：

```csharp
public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken)
{
    store.ResetPassword(userId, newPassword);
    return Task.CompletedTask;
}
```

修改 `PostgreSqlIamUserApplicationService` 构造函数，使其包含 `IUserSessionRepository sessionRepository`，然后添加：

```csharp
public async Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(newPassword))
    {
        throw new KnownException("New password is required.");
    }

    var typedUserId = new UserId(userId);
    var user = await repository.GetByIdAsync(typedUserId, cancellationToken)
        ?? throw new KnownException($"User '{userId}' was not found.");

    user.UpdatePasswordHash(passwordService.Hash(newPassword));

    var now = DateTimeOffset.UtcNow;
    var sessions = await sessionRepository.ListActiveByUserIdAsync(typedUserId, now, cancellationToken);
    foreach (var session in sessions)
    {
        session.Revoke(now, "admin-password-reset");
    }
}
```

创建 `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Commands/Users/ResetUserPasswordCommand.cs`：

```csharp
using Nerv.IIP.Iam.Web.Application.Users;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Commands.Users;

public sealed record ResetUserPasswordCommand(string UserId, string NewPassword) : ICommand;

public sealed class ResetUserPasswordCommandHandler(IIamUserApplicationService users)
    : ICommandHandler<ResetUserPasswordCommand>
{
    public async Task Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        await users.ResetPasswordAsync(request.UserId, request.NewPassword, cancellationToken);
    }
}
```

- [ ] **步骤 9：接入角色、权限和密码重置端点**

在 `RoleEndpoints.cs` 中，修改创建端点，使其读取 `CreateRoleRequest`，调用 `CreateRoleAsync(req.RoleName, req.PermissionCodes, ct)`，并返回 `201` 和 `RoleResponse`。

```csharp
var req = await HttpContext.Request.ReadFromJsonAsync<CreateRoleRequest>(ct)
    ?? throw new BadHttpRequestException("Request body is required.");
var response = await roles.CreateRoleAsync(req.RoleName, req.PermissionCodes, ct);
await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
```

修改更新端点，使其读取 `PatchRolePermissionsRequest`，调用 `PatchRolePermissionsAsync(Route<string>("roleId")!, req.PermissionCodes, ct)`，并返回 `200`。

在 `RoleEndpoints.cs` 中添加权限目录端点：

```csharp
[HttpGet("/api/iam/v1/permissions")]
[AllowAnonymous]
public sealed class ListPermissionsEndpoint(IIamPermissionAuthorizer authorizer)
    : EndpointWithoutRequest<ResponseData<PermissionCatalogResponse>>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.roles.read", ct))
        {
            return;
        }

        await Send.OkAsync(IamPermissionCatalog.List().AsResponseData(), ct);
    }
}
```

在 `UserEndpoints.cs` 中添加：

```csharp
public sealed record ResetUserPasswordRequest(string NewPassword);

[HttpPost("/api/iam/v1/users/{userId}/reset-password")]
[AllowAnonymous]
public sealed class ResetUserPasswordEndpoint(IIamPermissionAuthorizer authorizer, IMediator mediator)
    : EndpointWithoutRequest
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!await authorizer.RequirePermissionAsync(HttpContext, "iam.users.manage", ct))
        {
            return;
        }

        var req = await HttpContext.Request.ReadFromJsonAsync<ResetUserPasswordRequest>(ct)
            ?? throw new BadHttpRequestException("Request body is required.");
        await mediator.Send(new ResetUserPasswordCommand(Route<string>("userId") ?? string.Empty, req.NewPassword), ct);
        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}
```

- [ ] **步骤 10：更新匿名授权覆盖**

在 `IamManagementEndpointAuthorizationTests.cs` 中添加以下内联数据行：

```csharp
[InlineData("POST", "/api/iam/v1/users/user-admin/reset-password")]
[InlineData("GET", "/api/iam/v1/permissions")]
```

- [ ] **步骤 11：运行 IAM 内存测试并确认测试为绿**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter "In_memory_role_management_creates_role_updates_permissions_and_lists_catalog|In_memory_role_management_rejects_unknown_permissions_and_duplicate_names|Admin_reset_password_changes_login_secret_and_revokes_sessions|Postgres_management_endpoints_reject_anonymous_callers_before_touching_persistence"
```

预期：通过。

- [ ] **步骤 12：添加 PostgreSQL 配置档测试**

在 `IamPostgresProfileTests.cs` 中添加名为 `Postgres_profile_persists_role_mutation_permission_catalog_and_password_reset` 的测试，使用与 `Postgres_profile_persists_user_create_update_and_disable_commands` 相同的环境设置模式。该测试必须：

```csharp
var catalog = await client.GetAsync("/api/iam/v1/permissions");
catalog.EnsureSuccessStatusCode();

var createRole = await client.PostAsJsonAsync(
    "/api/iam/v1/roles",
    new { roleName = "Postgres Operator", permissionCodes = new[] { "apphub.instances.read" } });
Assert.Equal(HttpStatusCode.Created, createRole.StatusCode);
var role = await createRole.Content.ReadFromJsonAsync<RoleResponse>();

var patchRole = await client.PatchAsJsonAsync(
    $"/api/iam/v1/roles/{role!.RoleId}/permissions",
    new { permissionCodes = new[] { "iam.users.read", "ops.tasks.read" } });
patchRole.EnsureSuccessStatusCode();

var createUser = await client.PostAsJsonAsync(
    "/api/iam/v1/users",
    new { loginName = "reset-pg", email = "reset-pg@nerv-iip.local", password = "OldPassword123!" });
createUser.EnsureSuccessStatusCode();
var user = await createUser.Content.ReadFromJsonAsync<UserResponse>();

var reset = await client.PostAsJsonAsync(
    $"/api/iam/v1/users/{user!.UserId}/reset-password",
    new { newPassword = "NewPassword123!" });
Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
```

然后通过 `ApplicationDbContext` 断言该角色恰好拥有 `iam.users.read` 和 `ops.tasks.read`，且重置后的用户密码哈希不包含任一明文密码。

- [ ] **步骤 13：配置测试数据库时运行 PostgreSQL 配置档**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter Postgres_profile_persists_role_mutation_permission_catalog_and_password_reset
```

预期：设置 `NERV_IIP_TEST_POSTGRES` 时通过。未设置时，测试按照现有配置档测试约定退出且不执行断言。

- [ ] **步骤 14：提交 IAM 后端完成项**

运行：

```powershell
git add backend/services/Iam
git commit -m "feat: complete iam admin mutations"
```

预期：提交成功。

## 任务 3：添加 PlatformGateway Console IAM 管理门面

**文件：**

- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorization.cs`
- 创建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/IamAdmin/ConsoleIamAdminModels.cs`
- 创建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/IamAdmin/GatewayIamAdminClient.cs`
- 创建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/IamAdmin/ConsoleIamAdminEndpoints.cs`
- 创建：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayConsoleIamAdminTests.cs`
- 修改：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOpenApiTests.cs`

- [ ] **步骤 1：编写会失败的 Gateway 门面测试**

创建 `GatewayConsoleIamAdminTests.cs`：

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleIamAdminTests
{
    [Fact]
    public async Task Console_iam_users_requires_auth_and_does_not_forward()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var iamAuth = FakeGatewayIamAuthClient.Principal();
        var admin = new FakeGatewayIamAdminClient();
        await using var factory = CreateFactory(auth, iamAuth, admin);

        var response = await factory.CreateClient().GetAsync("/api/console/v1/iam/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(auth.LastRequirement);
        Assert.Equal(0, admin.ListUsersCallCount);
    }

    [Fact]
    public async Task Console_iam_users_checks_iam_permission_before_forwarding()
    {
        var auth = FakeGatewayAuthorizationClient.Forbidden();
        var iamAuth = FakeGatewayIamAuthClient.Principal();
        var admin = new FakeGatewayIamAdminClient();
        await using var factory = CreateFactory(auth, iamAuth, admin);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/iam/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("iam.users.read", auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
        Assert.Equal(0, admin.ListUsersCallCount);
    }

    [Fact]
    public async Task Console_iam_users_forwards_after_permission_check()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        var iamAuth = FakeGatewayIamAuthClient.Principal();
        var admin = new FakeGatewayIamAdminClient();
        await using var factory = CreateFactory(auth, iamAuth, admin);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", GatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/console/v1/iam/users?pageIndex=1&pageSize=20");

        response.EnsureSuccessStatusCode();
        Assert.Equal("iam.users.read", auth.LastRequirement!.PermissionCode);
        Assert.Equal(1, admin.ListUsersCallCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        FakeGatewayAuthorizationClient auth,
        FakeGatewayIamAuthClient iamAuth,
        FakeGatewayIamAdminClient admin) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayAuthorizationClient>();
            services.AddSingleton<IGatewayAuthorizationClient>(auth);
            services.RemoveAll<IGatewayIamAuthClient>();
            services.AddSingleton<IGatewayIamAuthClient>(iamAuth);
            services.RemoveAll<IGatewayIamAdminClient>();
            services.AddSingleton<IGatewayIamAdminClient>(admin);
        }));
}
```

在同一文件中添加模拟类：

```csharp
internal sealed class FakeGatewayIamAuthClient(ConsolePrincipalResponse principal) : IGatewayIamAuthClient
{
    public static FakeGatewayIamAuthClient Principal() => new(new ConsolePrincipalResponse(
        "user-admin",
        "user",
        "admin",
        "admin@nerv-iip.local",
        "org-001",
        "env-dev",
        1));

    public Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken) => Task.FromResult(principal);
}

internal sealed class FakeGatewayIamAdminClient : IGatewayIamAdminClient
{
    public int ListUsersCallCount { get; private set; }

    public Task<PagedListResponse<ConsoleIamUserResponse>> ListUsersAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken)
    {
        ListUsersCallCount++;
        return Task.FromResult(new PagedListResponse<ConsoleIamUserResponse>(1, 20, 0, []));
    }

    public Task<ConsoleIamUserResponse> CreateUserAsync(string bearerToken, ConsoleCreateIamUserRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConsoleIamUserResponse> UpdateUserAsync(string bearerToken, string userId, ConsoleUpdateIamUserRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task DisableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task ResetUserPasswordAsync(string bearerToken, string userId, ConsoleResetIamUserPasswordRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedListResponse<ConsoleIamRoleResponse>> ListRolesAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConsoleIamRoleResponse> CreateRoleAsync(string bearerToken, ConsoleCreateIamRoleRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConsoleIamRoleResponse> UpdateRolePermissionsAsync(string bearerToken, string roleId, ConsoleUpdateIamRolePermissionsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<ConsoleIamPermissionCatalogResponse> ListPermissionsAsync(string bearerToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedListResponse<ConsoleIamSessionResponse>> ListSessionsAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task RevokeSessionAsync(string bearerToken, string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
```

- [ ] **步骤 2：运行 Gateway 测试并确认测试为红**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter GatewayConsoleIamAdminTests
```

预期：失败，因为 `IGatewayIamAdminClient`、模型和端点尚不存在。

- [ ] **步骤 3：添加 Console IAM 管理模型**

创建 `Application/IamAdmin/ConsoleIamAdminModels.cs`：

```csharp
namespace Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;

public sealed record PagedListResponse<T>(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<T> Items);

public sealed record ConsoleIamListRequest(
    int? PageIndex,
    int? PageSize,
    string? SortBy,
    string? SortOrder,
    string? FilterSearch,
    bool? FilterEnabled,
    bool? FilterRevoked);

public sealed record ConsoleIamUserResponse(string UserId, string LoginName, string Email, bool Enabled);
public sealed record ConsoleCreateIamUserRequest(string LoginName, string Email, string Password);
public sealed record ConsoleUpdateIamUserRequest(string LoginName, string Email, bool Enabled);
public sealed record ConsoleResetIamUserPasswordRequest(string NewPassword);

public sealed record ConsoleIamRoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record ConsoleCreateIamRoleRequest(string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record ConsoleUpdateIamRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

public sealed record ConsoleIamPermissionCatalogResponse(IReadOnlyList<ConsoleIamPermissionResponse> Items);
public sealed record ConsoleIamPermissionResponse(string Code, string Domain, string Description, bool Seeded);

public sealed record ConsoleIamSessionResponse(
    string SessionId,
    string UserId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    int PermissionVersion);
```

- [ ] **步骤 4：添加 Gateway IAM 管理客户端**

创建 `Application/IamAdmin/GatewayIamAdminClient.cs`：

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;

public interface IGatewayIamAdminClient
{
    Task<PagedListResponse<ConsoleIamUserResponse>> ListUsersAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken);
    Task<ConsoleIamUserResponse> CreateUserAsync(string bearerToken, ConsoleCreateIamUserRequest request, CancellationToken cancellationToken);
    Task<ConsoleIamUserResponse> UpdateUserAsync(string bearerToken, string userId, ConsoleUpdateIamUserRequest request, CancellationToken cancellationToken);
    Task DisableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken);
    Task ResetUserPasswordAsync(string bearerToken, string userId, ConsoleResetIamUserPasswordRequest request, CancellationToken cancellationToken);
    Task<PagedListResponse<ConsoleIamRoleResponse>> ListRolesAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken);
    Task<ConsoleIamRoleResponse> CreateRoleAsync(string bearerToken, ConsoleCreateIamRoleRequest request, CancellationToken cancellationToken);
    Task<ConsoleIamRoleResponse> UpdateRolePermissionsAsync(string bearerToken, string roleId, ConsoleUpdateIamRolePermissionsRequest request, CancellationToken cancellationToken);
    Task<ConsoleIamPermissionCatalogResponse> ListPermissionsAsync(string bearerToken, CancellationToken cancellationToken);
    Task<PagedListResponse<ConsoleIamSessionResponse>> ListSessionsAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken);
    Task RevokeSessionAsync(string bearerToken, string sessionId, CancellationToken cancellationToken);
}

public sealed class HttpGatewayIamAdminClient(HttpClient httpClient) : IGatewayIamAdminClient
{
    public Task<PagedListResponse<ConsoleIamUserResponse>> ListUsersAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<PagedListResponse<ConsoleIamUserResponse>>(HttpMethod.Get, WithQuery("/api/iam/v1/users", request), bearerToken, null, cancellationToken);

    public Task<ConsoleIamUserResponse> CreateUserAsync(string bearerToken, ConsoleCreateIamUserRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<ConsoleIamUserResponse>(HttpMethod.Post, "/api/iam/v1/users", bearerToken, JsonContent.Create(request), cancellationToken);

    public Task<ConsoleIamUserResponse> UpdateUserAsync(string bearerToken, string userId, ConsoleUpdateIamUserRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<ConsoleIamUserResponse>(HttpMethod.Patch, $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}", bearerToken, JsonContent.Create(request), cancellationToken);

    public Task DisableUserAsync(string bearerToken, string userId, CancellationToken cancellationToken) =>
        SendNoContentAsync(HttpMethod.Post, $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}/disable", bearerToken, null, cancellationToken);

    public Task ResetUserPasswordAsync(string bearerToken, string userId, ConsoleResetIamUserPasswordRequest request, CancellationToken cancellationToken) =>
        SendNoContentAsync(HttpMethod.Post, $"/api/iam/v1/users/{Uri.EscapeDataString(userId)}/reset-password", bearerToken, JsonContent.Create(request), cancellationToken);

    public Task<PagedListResponse<ConsoleIamRoleResponse>> ListRolesAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<PagedListResponse<ConsoleIamRoleResponse>>(HttpMethod.Get, WithQuery("/api/iam/v1/roles", request), bearerToken, null, cancellationToken);

    public Task<ConsoleIamRoleResponse> CreateRoleAsync(string bearerToken, ConsoleCreateIamRoleRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<ConsoleIamRoleResponse>(HttpMethod.Post, "/api/iam/v1/roles", bearerToken, JsonContent.Create(request), cancellationToken);

    public Task<ConsoleIamRoleResponse> UpdateRolePermissionsAsync(string bearerToken, string roleId, ConsoleUpdateIamRolePermissionsRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<ConsoleIamRoleResponse>(HttpMethod.Patch, $"/api/iam/v1/roles/{Uri.EscapeDataString(roleId)}/permissions", bearerToken, JsonContent.Create(request), cancellationToken);

    public Task<ConsoleIamPermissionCatalogResponse> ListPermissionsAsync(string bearerToken, CancellationToken cancellationToken) =>
        SendForDataAsync<ConsoleIamPermissionCatalogResponse>(HttpMethod.Get, "/api/iam/v1/permissions", bearerToken, null, cancellationToken);

    public Task<PagedListResponse<ConsoleIamSessionResponse>> ListSessionsAsync(string bearerToken, ConsoleIamListRequest request, CancellationToken cancellationToken) =>
        SendForDataAsync<PagedListResponse<ConsoleIamSessionResponse>>(HttpMethod.Get, WithQuery("/api/iam/v1/sessions", request), bearerToken, null, cancellationToken);

    public Task RevokeSessionAsync(string bearerToken, string sessionId, CancellationToken cancellationToken) =>
        SendNoContentAsync(HttpMethod.Post, $"/api/iam/v1/sessions/{Uri.EscapeDataString(sessionId)}/revoke", bearerToken, null, cancellationToken);

    private async Task<T> SendForDataAsync<T>(HttpMethod method, string requestUri, string bearerToken, HttpContent? content, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, requestUri, bearerToken, content, cancellationToken);
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<T>>(cancellationToken);
            if (envelope is null || !envelope.Success || envelope.Data is null)
            {
                throw GatewayAuthException.BadGateway(envelope?.Message ?? "iam-empty-response");
            }

            return envelope.Data;
        }
        catch (JsonException)
        {
            throw GatewayAuthException.BadGateway("iam-invalid-response");
        }
    }

    private async Task SendNoContentAsync(HttpMethod method, string requestUri, string bearerToken, HttpContent? content, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(method, requestUri, bearerToken, content, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, string bearerToken, HttpContent? content, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            request.Content = content;

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var statusCode = response.StatusCode;
            response.Dispose();
            throw ToGatewayException(statusCode);
        }
        catch (GatewayAuthException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw GatewayAuthException.Unavailable("iam-unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw GatewayAuthException.Unavailable("iam-unavailable");
        }
    }

    private static GatewayAuthException ToGatewayException(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => GatewayAuthException.Unauthorized("iam-unauthorized"),
            HttpStatusCode.Forbidden => new GatewayAuthException(HttpStatusCode.Forbidden, "iam-forbidden"),
            HttpStatusCode.BadRequest => new GatewayAuthException(HttpStatusCode.BadRequest, "iam-bad-request"),
            HttpStatusCode.NotFound => new GatewayAuthException(HttpStatusCode.NotFound, "iam-not-found"),
            HttpStatusCode.Conflict => new GatewayAuthException(HttpStatusCode.Conflict, "iam-conflict"),
            _ when (int)statusCode >= 500 => GatewayAuthException.Unavailable("iam-unavailable"),
            _ => GatewayAuthException.BadGateway($"iam-unexpected-status-{(int)statusCode}")
        };
    }

    private static string WithQuery(string path, ConsoleIamListRequest request)
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["pageIndex"] = request.PageIndex?.ToString(),
            ["pageSize"] = request.PageSize?.ToString(),
            ["sortBy"] = request.SortBy,
            ["sortOrder"] = request.SortOrder,
            ["filterSearch"] = request.FilterSearch,
            ["filterEnabled"] = request.FilterEnabled?.ToString().ToLowerInvariant(),
            ["filterRevoked"] = request.FilterRevoked?.ToString().ToLowerInvariant()
        };

        var pairs = query
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}")
            .ToArray();

        return pairs.Length == 0 ? path : $"{path}?{string.Join('&', pairs)}";
    }

    private sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
}
```

- [ ] **步骤 5：为当前 Console 主体添加授权辅助方法**

在 `GatewayAuthorization.cs` 中添加：

```csharp
public static async Task<(string BearerToken, ConsolePrincipalResponse Principal)?> RequireCurrentPrincipalPermissionAsync(
    HttpContext context,
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    string permissionCode,
    CancellationToken cancellationToken)
{
    var bearerToken = await context.GetTokenAsync("access_token");
    if (string.IsNullOrWhiteSpace(bearerToken))
    {
        await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.", cancellationToken);
        return null;
    }

    ConsolePrincipalResponse principal;
    try
    {
        principal = await iam.GetMeAsync(bearerToken, cancellationToken);
    }
    catch (GatewayAuthException ex)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(context, (int)ex.StatusCode, ex.Reason, cancellationToken);
        return null;
    }

    var result = await auth.CheckAsync(
        bearerToken,
        new GatewayPermissionRequirement(permissionCode, principal.OrganizationId, principal.EnvironmentId, null, null),
        cancellationToken);

    if (!result.IsAllowed)
    {
        await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status403Forbidden, "Forbidden.", cancellationToken);
        return null;
    }

    context.Items[PrincipalItemKey] = result;
    return (bearerToken, principal);
}
```

- [ ] **步骤 6：添加门面端点**

创建 `Endpoints/IamAdmin/ConsoleIamAdminEndpoints.cs`，每条路由对应一个端点类。每个端点使用以下模式：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.IamAdmin;

[HttpGet("/api/console/v1/iam/users")]
[Authorize(Policy = GatewayPolicies.ConsoleAuthenticated)]
public sealed class ListConsoleIamUsersEndpoint(
    IGatewayIamAuthClient iam,
    IGatewayAuthorizationClient auth,
    IGatewayIamAdminClient admin) : Endpoint<ConsoleIamListRequest, ResponseData<PagedListResponse<ConsoleIamUserResponse>>>
{
    public override async Task HandleAsync(ConsoleIamListRequest req, CancellationToken ct)
    {
        var authorized = await GatewayAuthorization.RequireCurrentPrincipalPermissionAsync(HttpContext, iam, auth, "iam.users.read", ct);
        if (authorized is null)
        {
            return;
        }

        try
        {
            var response = await admin.ListUsersAsync(authorized.Value.BearerToken, req, ct);
            await Send.OkAsync(response.AsResponseData(), ct);
        }
        catch (GatewayAuthException ex)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(HttpContext, (int)ex.StatusCode, ex.Reason, ct);
        }
    }
}
```

按照以下权限映射和响应状态规则添加其余端点类：

```text
CreateConsoleIamUserEndpoint                 POST   /api/console/v1/iam/users                         iam.users.manage     201 data
UpdateConsoleIamUserEndpoint                 PATCH  /api/console/v1/iam/users/{userId}                iam.users.manage     200 data
DisableConsoleIamUserEndpoint                POST   /api/console/v1/iam/users/{userId}/disable        iam.users.manage     204
ResetConsoleIamUserPasswordEndpoint          POST   /api/console/v1/iam/users/{userId}/reset-password iam.users.manage     204
ListConsoleIamRolesEndpoint                  GET    /api/console/v1/iam/roles                         iam.roles.read       200 data
CreateConsoleIamRoleEndpoint                 POST   /api/console/v1/iam/roles                         iam.roles.manage     201 data
UpdateConsoleIamRolePermissionsEndpoint      PATCH  /api/console/v1/iam/roles/{roleId}/permissions   iam.roles.manage     200 data
ListConsoleIamPermissionsEndpoint            GET    /api/console/v1/iam/permissions                   iam.roles.read       200 data
ListConsoleIamSessionsEndpoint               GET    /api/console/v1/iam/sessions                      iam.sessions.read    200 data
RevokeConsoleIamSessionEndpoint              POST   /api/console/v1/iam/sessions/{sessionId}/revoke   iam.sessions.revoke  204
```

- [ ] **步骤 7：注册 Gateway IAM 管理客户端**

在 `Program.cs` 中添加：

```csharp
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Endpoints.IamAdmin;
```

注册 HTTP 客户端：

```csharp
builder.Services.AddHttpClient<IGatewayIamAdminClient, HttpGatewayIamAdminClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
```

- [ ] **步骤 8：添加稳定的 operation ID**

在 `Program.cs` 中扩展端点名称生成器的分支表达式：

```csharp
nameof(ListConsoleIamUsersEndpoint) => "listConsoleIamUsers",
nameof(CreateConsoleIamUserEndpoint) => "createConsoleIamUser",
nameof(UpdateConsoleIamUserEndpoint) => "updateConsoleIamUser",
nameof(DisableConsoleIamUserEndpoint) => "disableConsoleIamUser",
nameof(ResetConsoleIamUserPasswordEndpoint) => "resetConsoleIamUserPassword",
nameof(ListConsoleIamRolesEndpoint) => "listConsoleIamRoles",
nameof(CreateConsoleIamRoleEndpoint) => "createConsoleIamRole",
nameof(UpdateConsoleIamRolePermissionsEndpoint) => "updateConsoleIamRolePermissions",
nameof(ListConsoleIamPermissionsEndpoint) => "listConsoleIamPermissions",
nameof(ListConsoleIamSessionsEndpoint) => "listConsoleIamSessions",
nameof(RevokeConsoleIamSessionEndpoint) => "revokeConsoleIamSession",
```

- [ ] **步骤 9：更新 OpenAPI 测试**

在 `GatewayOpenApiTests.cs` 中添加断言，确保 `/swagger/v1/swagger.json` 包含步骤 8 中的每个操作 ID。

- [ ] **步骤 10：运行 Gateway 测试并确认测试为绿**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter "GatewayConsoleIamAdminTests|Gateway_exports_console_openapi_document_with_stable_operation_ids"
```

预期：通过。

- [ ] **步骤 11：提交 Gateway 门面**

运行：

```powershell
git add backend/gateway/PlatformGateway
git commit -m "feat: add console iam admin facade"
```

预期：提交成功。

## 任务 4：导出 Gateway OpenAPI 并添加稳定的 IAM api-client 导出

**文件：**

- 修改生成文件：`frontend/packages/api-client/openapi/platform-gateway.v1.json`
- 修改生成文件：`frontend/packages/api-client/src/generated/**`
- 创建：`frontend/packages/api-client/src/iam.ts`
- 修改：`frontend/packages/api-client/src/index.ts`
- 修改：`frontend/packages/api-client/src/generated-contract.test.ts`

- [ ] **步骤 1：导出 Gateway OpenAPI**

运行：

```powershell
pwsh scripts/export-gateway-openapi.ps1
```

预期：`frontend/packages/api-client/openapi/platform-gateway.v1.json` 包含十一个 `listConsoleIam...` 和变更操作 ID。

- [ ] **步骤 2：重新生成 api-client**

运行：

```powershell
pnpm -C frontend generate:api
```

预期：`frontend/packages/api-client/src/generated` 下的生成文件包含 Console IAM 管理操作的 SDK 函数和 Pinia Colada 选项。

- [ ] **步骤 3：添加稳定的 IAM 导出**

创建 `frontend/packages/api-client/src/iam.ts`：

```ts
export {
  createConsoleIamRoleMutationOptions,
  createConsoleIamUserMutationOptions,
  disableConsoleIamUserMutationOptions,
  listConsoleIamPermissionsQueryOptions,
  listConsoleIamRolesQueryOptions,
  listConsoleIamSessionsQueryOptions,
  listConsoleIamUsersQueryOptions,
  resetConsoleIamUserPasswordMutationOptions,
  revokeConsoleIamSessionMutationOptions,
  updateConsoleIamRolePermissionsMutationOptions,
  updateConsoleIamUserMutationOptions,
} from './generated/@pinia/colada.gen'

export {
  createConsoleIamRole,
  createConsoleIamUser,
  disableConsoleIamUser,
  listConsoleIamPermissions,
  listConsoleIamRoles,
  listConsoleIamSessions,
  listConsoleIamUsers,
  resetConsoleIamUserPassword,
  revokeConsoleIamSession,
  updateConsoleIamRolePermissions,
  updateConsoleIamUser,
} from './generated/sdk.gen'

import type {
  NervIipPlatformGatewayWebApplicationIamAdminConsoleCreateIamRoleRequest,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleCreateIamUserRequest,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamPermissionCatalogResponse,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamPermissionResponse,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamRoleResponse,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamSessionResponse,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamUserResponse,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleResetIamUserPasswordRequest,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleUpdateIamRolePermissionsRequest,
  NervIipPlatformGatewayWebApplicationIamAdminConsoleUpdateIamUserRequest,
  NervIipPlatformGatewayWebApplicationIamAdminPagedListResponseOfConsoleIamRoleResponse,
  NervIipPlatformGatewayWebApplicationIamAdminPagedListResponseOfConsoleIamSessionResponse,
  NervIipPlatformGatewayWebApplicationIamAdminPagedListResponseOfConsoleIamUserResponse,
  NetCorePalExtensionsDtoResponseDataOfConsoleIamPermissionCatalogResponse,
  NetCorePalExtensionsDtoResponseDataOfConsoleIamRoleResponse,
  NetCorePalExtensionsDtoResponseDataOfConsoleIamUserResponse,
  NetCorePalExtensionsDtoResponseDataOfPagedListResponseOfConsoleIamRoleResponse,
  NetCorePalExtensionsDtoResponseDataOfPagedListResponseOfConsoleIamSessionResponse,
  NetCorePalExtensionsDtoResponseDataOfPagedListResponseOfConsoleIamUserResponse,
} from './generated/types.gen'

export type ConsoleIamUserResponse =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamUserResponse
export type ConsoleCreateIamUserRequest =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleCreateIamUserRequest
export type ConsoleUpdateIamUserRequest =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleUpdateIamUserRequest
export type ConsoleResetIamUserPasswordRequest =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleResetIamUserPasswordRequest
export type ConsoleIamRoleResponse =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamRoleResponse
export type ConsoleCreateIamRoleRequest =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleCreateIamRoleRequest
export type ConsoleUpdateIamRolePermissionsRequest =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleUpdateIamRolePermissionsRequest
export type ConsoleIamPermissionCatalogResponse =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamPermissionCatalogResponse
export type ConsoleIamPermissionResponse =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamPermissionResponse
export type ConsoleIamSessionResponse =
  NervIipPlatformGatewayWebApplicationIamAdminConsoleIamSessionResponse
export type ConsoleIamUsersPage =
  NervIipPlatformGatewayWebApplicationIamAdminPagedListResponseOfConsoleIamUserResponse
export type ConsoleIamRolesPage =
  NervIipPlatformGatewayWebApplicationIamAdminPagedListResponseOfConsoleIamRoleResponse
export type ConsoleIamSessionsPage =
  NervIipPlatformGatewayWebApplicationIamAdminPagedListResponseOfConsoleIamSessionResponse
export type ConsoleIamUserEnvelope = NetCorePalExtensionsDtoResponseDataOfConsoleIamUserResponse
export type ConsoleIamRoleEnvelope = NetCorePalExtensionsDtoResponseDataOfConsoleIamRoleResponse
export type ConsoleIamUsersEnvelope =
  NetCorePalExtensionsDtoResponseDataOfPagedListResponseOfConsoleIamUserResponse
export type ConsoleIamRolesEnvelope =
  NetCorePalExtensionsDtoResponseDataOfPagedListResponseOfConsoleIamRoleResponse
export type ConsoleIamSessionsEnvelope =
  NetCorePalExtensionsDtoResponseDataOfPagedListResponseOfConsoleIamSessionResponse
export type ConsoleIamPermissionsEnvelope =
  NetCorePalExtensionsDtoResponseDataOfConsoleIamPermissionCatalogResponse
```

如果生成类型名称仅因命名空间扁平化而不同，则将导入替换为 `types.gen.ts` 中生成的名称，并保持公开别名与左侧所示名称完全一致。

- [ ] **步骤 4：从包根目录导出 IAM 汇总模块**

修改 `frontend/packages/api-client/src/index.ts`：

```ts
export { configureApiClient } from './transport/client-config'
export type { ConfigureApiClientOptions } from './transport/client-config'
export * from './auth'
export * from './console'
export * from './iam'
```

- [ ] **步骤 5：添加生成契约覆盖**

在 `frontend/packages/api-client/src/generated-contract.test.ts` 中添加：

```ts
import {
  createConsoleIamRoleMutationOptions,
  createConsoleIamUserMutationOptions,
  listConsoleIamPermissionsQueryOptions,
  listConsoleIamRolesQueryOptions,
  listConsoleIamSessionsQueryOptions,
  listConsoleIamUsersQueryOptions,
  resetConsoleIamUserPasswordMutationOptions,
  revokeConsoleIamSessionMutationOptions,
  updateConsoleIamRolePermissionsMutationOptions,
  updateConsoleIamUserMutationOptions,
} from './iam'

it('exports Console IAM Admin generated operations through stable api-client entry points', () => {
  expect(listConsoleIamUsersQueryOptions).toBeTypeOf('function')
  expect(createConsoleIamUserMutationOptions).toBeTypeOf('function')
  expect(updateConsoleIamUserMutationOptions).toBeTypeOf('function')
  expect(resetConsoleIamUserPasswordMutationOptions).toBeTypeOf('function')
  expect(listConsoleIamRolesQueryOptions).toBeTypeOf('function')
  expect(createConsoleIamRoleMutationOptions).toBeTypeOf('function')
  expect(updateConsoleIamRolePermissionsMutationOptions).toBeTypeOf('function')
  expect(listConsoleIamPermissionsQueryOptions).toBeTypeOf('function')
  expect(listConsoleIamSessionsQueryOptions).toBeTypeOf('function')
  expect(revokeConsoleIamSessionMutationOptions).toBeTypeOf('function')
})
```

- [ ] **步骤 6：运行 api-client 测试和类型检查**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

预期：通过。

- [ ] **步骤 7：提交生成契约**

运行：

```powershell
git add frontend/packages/api-client
git commit -m "feat: expose iam admin api client"
```

预期：提交成功。

## 任务 5：添加 IAM 导航和共享管理组合式函数

**文件：**

- 修改：`frontend/packages/app-shell/src/AppShell.vue`
- 修改：`frontend/packages/app-shell/src/AppShell.test.ts`
- 修改：`frontend/apps/console/src/layouts/DefaultLayout.vue`
- 修改：`frontend/apps/console/src/layouts/DefaultLayout.test.ts`
- 创建：`frontend/apps/console/src/api/iam.ts`
- 创建：`frontend/apps/console/src/composables/useIamAdmin.ts`
- 创建：`frontend/apps/console/src/composables/useIamAdmin.test.ts`

- [ ] **步骤 1：编写会失败的导航测试**

在 `AppShell.test.ts` 中添加：

```ts
it('renders grouped navigation children', async () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { component: { template: '<div />' }, name: 'home', path: '/' },
      { component: { template: '<div />' }, name: '/iam/users/', path: '/iam/users' },
      { component: { template: '<div />' }, name: '/iam/roles/', path: '/iam/roles' },
    ],
  })

  router.push('/')
  await router.isReady()

  const wrapper = mount(AppShell, {
    global: { plugins: [router] },
    props: {
      title: 'Nerv-IIP',
      navItems: [
        { label: 'Instances', to: { name: 'home' } },
        {
          label: 'IAM',
          children: [
            { label: 'Users', to: { name: '/iam/users/' } },
            { label: 'Roles', to: { name: '/iam/roles/' } },
          ],
        },
      ],
    },
  })

  expect(wrapper.get('[aria-label="Primary navigation"]').text()).toContain('IAM')
  expect(wrapper.findAllComponents(RouterLink).map((link) => link.props('to'))).toContainEqual({
    name: '/iam/users/',
  })
})
```

在 `DefaultLayout.test.ts` 中，将预期导航改为：

```ts
expect(wrapper.getComponent(AppShellStub).props('navItems')).toEqual([
  { label: 'Instances', to: { name: '/' } },
  {
    label: 'IAM',
    children: [
      { label: 'Users', to: { name: '/iam/users/' } },
      { label: 'Roles', to: { name: '/iam/roles/' } },
      { label: 'Sessions', to: { name: '/iam/sessions/' } },
    ],
  },
])
```

- [ ] **步骤 2：运行导航测试并确认测试为红**

运行：

```powershell
pnpm -C frontend test packages/app-shell/src/AppShell.test.ts apps/console/src/layouts/DefaultLayout.test.ts
```

预期：失败，因为 `NavItem` 不支持 `children`，且 DefaultLayout 仍只暴露实例入口。

- [ ] **步骤 3：添加分组导航支持**

在 `AppShell.vue` 中更新 `NavItem`：

```ts
interface NavItem {
  label: string
  to?: RouteLocationRaw
  children?: NavItem[]
}
```

将导航模板替换为：

```vue
<nav class="app-shell__nav" aria-label="Primary navigation">
  <template v-for="item in navItems" :key="item.label">
    <RouterLink v-if="item.to" class="app-shell__nav-link" :to="item.to">
      {{ item.label }}
    </RouterLink>
    <div v-else class="app-shell__nav-group">
      <p class="app-shell__nav-group-label">{{ item.label }}</p>
      <RouterLink
        v-for="child in item.children ?? []"
        :key="child.label"
        class="app-shell__nav-link app-shell__nav-link--child"
        :to="child.to!"
      >
        {{ child.label }}
      </RouterLink>
    </div>
  </template>
</nav>
```

添加 CSS：

```css
.app-shell__nav-group {
  display: grid;
  gap: 0.25rem;
}

.app-shell__nav-group-label {
  color: var(--muted-foreground);
  font-size: 0.72rem;
  font-weight: 750;
  letter-spacing: 0;
  margin: 0.65rem 0 0.15rem;
  text-transform: uppercase;
}

.app-shell__nav-link--child {
  padding-left: 1rem;
}
```

- [ ] **步骤 4：在 DefaultLayout 中添加 IAM 导航**

更新 `navItems`（位于 `DefaultLayout.vue`）：

```ts
const navItems = [
  { label: 'Instances', to: { name: '/' } },
  {
    label: 'IAM',
    children: [
      { label: 'Users', to: { name: '/iam/users/' } },
      { label: 'Roles', to: { name: '/iam/roles/' } },
      { label: 'Sessions', to: { name: '/iam/sessions/' } },
    ],
  },
] satisfies {
  label: string
  to?: RouteLocationRaw
  children?: { label: string; to: RouteLocationRaw }[]
}[]
```

- [ ] **步骤 5：运行导航测试并确认测试为绿**

运行：

```powershell
pnpm -C frontend test packages/app-shell/src/AppShell.test.ts apps/console/src/layouts/DefaultLayout.test.ts
```

预期：通过。

- [ ] **步骤 6：添加 IAM API 错误辅助方法**

创建 `frontend/apps/console/src/api/iam.ts`：

```ts
export class ConsoleIamError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
  }
}

export function toConsoleIamError(error: unknown, fallback: string): ConsoleIamError {
  if (error instanceof ConsoleIamError) {
    return error
  }

  if (error instanceof Error) {
    return new ConsoleIamError(error.message || fallback)
  }

  return new ConsoleIamError(fallback)
}
```

- [ ] **步骤 7：编写会失败的组合式函数测试**

创建 `frontend/apps/console/src/composables/useIamAdmin.test.ts`：

```ts
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useIamUsers } from './useIamAdmin'

const apiState = vi.hoisted(() => ({
  listUsersCalls: 0,
}))

vi.mock('@nerv-iip/api-client', () => ({
  listConsoleIamUsersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listConsoleIamUsers' }],
    query: vi.fn(async () => {
      apiState.listUsersCalls += 1
      return {
        success: true,
        data: {
          pageIndex: 1,
          pageSize: 20,
          totalCount: 1,
          items: [{ userId: 'user-admin', loginName: 'admin', email: 'admin@nerv-iip.local', enabled: true }],
        },
      }
    }),
  })),
  createConsoleIamUserMutationOptions: vi.fn(() => ({ mutation: vi.fn() })),
  updateConsoleIamUserMutationOptions: vi.fn(() => ({ mutation: vi.fn() })),
  disableConsoleIamUserMutationOptions: vi.fn(() => ({ mutation: vi.fn() })),
  resetConsoleIamUserPasswordMutationOptions: vi.fn(() => ({ mutation: vi.fn() })),
}))

describe('useIamUsers', () => {
  beforeEach(() => {
    apiState.listUsersCalls = 0
  })

  it('loads user list data through generated query options', async () => {
    const Probe = defineComponent({
      setup() {
        return useIamUsers()
      },
      template: '<span>{{ users.length }} {{ totalCount }}</span>',
    })

    const wrapper = mount(Probe, {
      global: {
        plugins: [createPinia(), [PiniaColada, { queryOptions: { gcTime: 300_000 } }]],
      },
    })

    await flushPromises()

    expect(wrapper.text()).toBe('1 1')
    expect(apiState.listUsersCalls).toBe(1)
  })
})
```

- [ ] **步骤 8：运行组合式函数测试并确认测试为红**

运行：

```powershell
pnpm -C frontend test apps/console/src/composables/useIamAdmin.test.ts
```

预期：失败，因为 `useIamAdmin.ts` 尚不存在。

- [ ] **步骤 9：实施共享 IAM 组合式函数**

创建 `frontend/apps/console/src/composables/useIamAdmin.ts`，其中包含：

```ts
import {
  createConsoleIamRoleMutationOptions,
  createConsoleIamUserMutationOptions,
  disableConsoleIamUserMutationOptions,
  listConsoleIamPermissionsQueryOptions,
  listConsoleIamRolesQueryOptions,
  listConsoleIamSessionsQueryOptions,
  listConsoleIamUsersQueryOptions,
  resetConsoleIamUserPasswordMutationOptions,
  revokeConsoleIamSessionMutationOptions,
  updateConsoleIamRolePermissionsMutationOptions,
  updateConsoleIamUserMutationOptions,
  type ConsoleIamPermissionResponse,
  type ConsoleIamRoleResponse,
  type ConsoleIamSessionResponse,
  type ConsoleIamUserResponse,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

export interface IamListFilters {
  pageIndex: number
  pageSize: number
  sortBy?: string
  sortOrder?: 'asc' | 'desc'
  filterSearch?: string
  filterEnabled?: boolean
  filterRevoked?: boolean
}

const defaultFilters = (): IamListFilters => ({
  pageIndex: 1,
  pageSize: 20,
})

function unwrapResponseData<T>(envelope: { data?: T | null; success?: boolean } | undefined): T | undefined {
  return envelope?.success ? envelope.data ?? undefined : undefined
}

function isQueryEntry(entry: UseQueryEntry, id: string) {
  const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
  return keyParts.some((part) => typeof part === 'object' && part !== null && '_id' in part && part._id === id)
}

export function useIamUsers() {
  const filters = reactive(defaultFilters())
  const cache = useQueryCache()
  const query = useQuery(() =>
    listConsoleIamUsersQueryOptions({
      query: filters,
    } as Parameters<typeof listConsoleIamUsersQueryOptions>[0]),
  )
  const createMutation = useMutation(createConsoleIamUserMutationOptions())
  const updateMutation = useMutation(updateConsoleIamUserMutationOptions())
  const disableMutation = useMutation(disableConsoleIamUserMutationOptions())
  const resetPasswordMutation = useMutation(resetConsoleIamUserPasswordMutationOptions())

  async function refreshUsers() {
    await cache.invalidateQueries({ predicate: (entry) => isQueryEntry(entry, 'listConsoleIamUsers') })
  }

  return {
    createUser: createMutation.mutateAsync,
    createUserPending: createMutation.isLoading,
    disableUser: disableMutation.mutateAsync,
    disableUserPending: disableMutation.isLoading,
    filters,
    listError: query.error,
    listPending: query.isLoading,
    refreshUsers,
    resetPassword: resetPasswordMutation.mutateAsync,
    resetPasswordPending: resetPasswordMutation.isLoading,
    totalCount: computed(() => unwrapResponseData(query.data.value)?.totalCount ?? 0),
    updateUser: updateMutation.mutateAsync,
    updateUserPending: updateMutation.isLoading,
    users: computed<ConsoleIamUserResponse[]>(() => unwrapResponseData(query.data.value)?.items ?? []),
  }
}

export function useIamRoles() {
  const filters = reactive(defaultFilters())
  const cache = useQueryCache()
  const rolesQuery = useQuery(() =>
    listConsoleIamRolesQueryOptions({ query: filters } as Parameters<typeof listConsoleIamRolesQueryOptions>[0]),
  )
  const permissionsQuery = useQuery(() => listConsoleIamPermissionsQueryOptions())
  const createMutation = useMutation(createConsoleIamRoleMutationOptions())
  const updatePermissionsMutation = useMutation(updateConsoleIamRolePermissionsMutationOptions())

  async function refreshRoles() {
    await cache.invalidateQueries({ predicate: (entry) => isQueryEntry(entry, 'listConsoleIamRoles') })
  }

  return {
    createRole: createMutation.mutateAsync,
    createRolePending: createMutation.isLoading,
    filters,
    listError: rolesQuery.error,
    listPending: rolesQuery.isLoading,
    permissionError: permissionsQuery.error,
    permissionPending: permissionsQuery.isLoading,
    permissions: computed<ConsoleIamPermissionResponse[]>(() => unwrapResponseData(permissionsQuery.data.value)?.items ?? []),
    refreshRoles,
    roles: computed<ConsoleIamRoleResponse[]>(() => unwrapResponseData(rolesQuery.data.value)?.items ?? []),
    totalCount: computed(() => unwrapResponseData(rolesQuery.data.value)?.totalCount ?? 0),
    updateRolePermissions: updatePermissionsMutation.mutateAsync,
    updateRolePermissionsPending: updatePermissionsMutation.isLoading,
  }
}

export function useIamSessions() {
  const filters = reactive({ ...defaultFilters(), filterRevoked: false as boolean | undefined })
  const cache = useQueryCache()
  const query = useQuery(() =>
    listConsoleIamSessionsQueryOptions({
      query: filters,
    } as Parameters<typeof listConsoleIamSessionsQueryOptions>[0]),
  )
  const revokeMutation = useMutation(revokeConsoleIamSessionMutationOptions())

  async function refreshSessions() {
    await cache.invalidateQueries({ predicate: (entry) => isQueryEntry(entry, 'listConsoleIamSessions') })
  }

  return {
    filters,
    listError: query.error,
    listPending: query.isLoading,
    refreshSessions,
    revokeSession: revokeMutation.mutateAsync,
    revokeSessionPending: revokeMutation.isLoading,
    sessions: computed<ConsoleIamSessionResponse[]>(() => unwrapResponseData(query.data.value)?.items ?? []),
    totalCount: computed(() => unwrapResponseData(query.data.value)?.totalCount ?? 0),
  }
}
```

- [ ] **步骤 10：运行组合式函数和导航测试**

运行：

```powershell
pnpm -C frontend test apps/console/src/composables/useIamAdmin.test.ts packages/app-shell/src/AppShell.test.ts apps/console/src/layouts/DefaultLayout.test.ts
```

预期：通过。

- [ ] **步骤 11：提交导航和组合式函数基础**

运行：

```powershell
git add frontend/packages/app-shell frontend/apps/console/src/layouts frontend/apps/console/src/api/iam.ts frontend/apps/console/src/composables/useIamAdmin.ts frontend/apps/console/src/composables/useIamAdmin.test.ts
git commit -m "feat: add iam admin navigation foundation"
```

预期：提交成功。

## 任务 6：构建 IAM 用户页面

**文件：**

- 创建：`frontend/apps/console/src/components/iam/IamPageHeader.vue`
- 创建：`frontend/apps/console/src/components/iam/IamListToolbar.vue`
- 创建：`frontend/apps/console/src/components/iam/UsersTable.vue`
- 创建：`frontend/apps/console/src/components/iam/UserCreateDialog.vue`
- 创建：`frontend/apps/console/src/components/iam/UserEditDialog.vue`
- 创建：`frontend/apps/console/src/components/iam/UserResetPasswordDialog.vue`
- 创建：`frontend/apps/console/src/pages/iam/users/index.vue`
- 创建：`frontend/apps/console/src/pages/iam/users/index.test.ts`

- [ ] **步骤 1：编写会失败的用户页面测试**

创建 `pages/iam/users/index.test.ts`，并模拟 `@/composables/useIamAdmin`：

```ts
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import UsersPage from './index.vue'

vi.mock('@/composables/useIamAdmin', () => ({
  useIamUsers: () => ({
    createUser: vi.fn(),
    createUserPending: { value: false },
    disableUser: vi.fn(),
    disableUserPending: { value: false },
    filters: { pageIndex: 1, pageSize: 20, filterSearch: '', filterEnabled: undefined },
    listError: { value: undefined },
    listPending: { value: false },
    refreshUsers: vi.fn(),
    resetPassword: vi.fn(),
    resetPasswordPending: { value: false },
    totalCount: { value: 1 },
    updateUser: vi.fn(),
    updateUserPending: { value: false },
    users: {
      value: [
        { userId: 'user-admin', loginName: 'admin', email: 'admin@nerv-iip.local', enabled: true },
      ],
    },
  }),
}))

describe('IAM users page', () => {
  it('renders user data and primary action', () => {
    const wrapper = mount(UsersPage, {
      global: {
        stubs: {
          Teleport: true,
        },
      },
    })

    expect(wrapper.get('h1').text()).toBe('Users')
    expect(wrapper.text()).toContain('admin@nerv-iip.local')
    expect(wrapper.text()).toContain('Create user')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })
})
```

- [ ] **步骤 2：运行用户页面测试并确认测试为红**

运行：

```powershell
pnpm -C frontend test apps/console/src/pages/iam/users/index.test.ts
```

预期：失败，因为页面和组件尚不存在。

- [ ] **步骤 3：添加共享 IAM 页面标题栏**

创建 `components/iam/IamPageHeader.vue`：

```vue
<script setup lang="ts">
defineProps<{
  description: string
  title: string
}>()
</script>

<template>
  <header class="flex flex-col gap-1">
    <h1 class="text-2xl font-semibold tracking-normal text-foreground">{{ title }}</h1>
    <p class="max-w-3xl text-sm text-muted-foreground">{{ description }}</p>
  </header>
</template>
```

- [ ] **步骤 4：添加共享 IAM 列表工具栏**

创建 `components/iam/IamListToolbar.vue`：

```vue
<script setup lang="ts">
import { Button, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@nerv-iip/ui'
import { SearchIcon } from 'lucide-vue-next'

const search = defineModel<string>('search', { default: '' })
const status = defineModel<string | undefined>('status')

defineProps<{
  actionLabel: string
  searchLabel: string
  searchPlaceholder: string
  statusOptions?: { label: string; value: string }[]
}>()

const emit = defineEmits<{
  action: []
}>()
</script>

<template>
  <div class="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
    <label class="relative min-w-0 md:w-80">
      <span class="sr-only">{{ searchLabel }}</span>
      <SearchIcon data-icon class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground" />
      <Input v-model="search" class="pl-9" :placeholder="searchPlaceholder" />
    </label>

    <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
      <Select v-if="statusOptions?.length" v-model="status">
        <SelectTrigger class="sm:w-44" aria-label="Status filter">
          <SelectValue placeholder="All statuses" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="">All statuses</SelectItem>
          <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">
            {{ option.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <Button type="button" @click="emit('action')">{{ actionLabel }}</Button>
    </div>
  </div>
</template>
```

- [ ] **步骤 5：添加用户表格**

创建带有 props（属性）和 emits（事件）的 `components/iam/UsersTable.vue`：

```vue
<script setup lang="ts">
import {
  Badge,
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuTrigger,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import type { ConsoleIamUserResponse } from '@nerv-iip/api-client'
import { MoreHorizontalIcon } from 'lucide-vue-next'

defineProps<{
  pending?: boolean
  users: ConsoleIamUserResponse[]
}>()

const emit = defineEmits<{
  disable: [user: ConsoleIamUserResponse]
  edit: [user: ConsoleIamUserResponse]
  resetPassword: [user: ConsoleIamUserResponse]
}>()
</script>

<template>
  <div class="overflow-hidden rounded-lg border bg-card">
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Login name</TableHead>
          <TableHead>Email</TableHead>
          <TableHead>User ID</TableHead>
          <TableHead>Status</TableHead>
          <TableHead class="text-right">Actions</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <template v-if="pending">
          <TableRow v-for="index in 4" :key="index">
            <TableCell colspan="5"><Skeleton class="h-7 w-full" /></TableCell>
          </TableRow>
        </template>
        <TableRow v-else-if="users.length === 0">
          <TableCell colspan="5" class="py-10 text-center text-sm text-muted-foreground">No users match the current filters.</TableCell>
        </TableRow>
        <template v-else>
          <TableRow v-for="user in users" :key="user.userId">
            <TableCell class="font-medium">{{ user.loginName }}</TableCell>
            <TableCell>{{ user.email }}</TableCell>
            <TableCell class="font-mono text-xs text-muted-foreground">{{ user.userId }}</TableCell>
            <TableCell>
              <Badge :variant="user.enabled ? 'secondary' : 'destructive'">
                {{ user.enabled ? 'Enabled' : 'Disabled' }}
              </Badge>
            </TableCell>
            <TableCell class="text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button aria-label="Open user actions" size="icon" variant="ghost">
                    <MoreHorizontalIcon data-icon />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuGroup>
                    <DropdownMenuItem @select="emit('edit', user)">Edit</DropdownMenuItem>
                    <DropdownMenuItem @select="emit('resetPassword', user)">Reset password</DropdownMenuItem>
                    <DropdownMenuItem :disabled="!user.enabled" @select="emit('disable', user)">Disable</DropdownMenuItem>
                  </DropdownMenuGroup>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </template>
      </TableBody>
    </Table>
  </div>
</template>
```

- [ ] **步骤 6：添加用户对话框**

创建 `UserCreateDialog.vue`、`UserEditDialog.vue` 和 `UserResetPasswordDialog.vue`，使用 shadcn `Dialog`、`FieldGroup`、`Field`、`FieldLabel`、`FieldError`、`Input` 和 `Button`。每个对话框必须：

```ts
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{
  submit: [payload: { loginName: string; email: string; password?: string; enabled?: boolean }]
}>()
```

在每个提交处理器中使用以下校验模式：

```ts
const error = ref<string>()

function submit() {
  error.value = undefined
  if (loginName.value.trim().length === 0) {
    error.value = 'Login name is required.'
    return
  }
  if (email.value.trim().length === 0) {
    error.value = 'Email is required.'
    return
  }
  emit('submit', {
    loginName: loginName.value.trim(),
    email: email.value.trim(),
    password: password.value,
    enabled: enabled.value,
  })
}
```

对于 `UserResetPasswordDialog.vue`，只提交 `{ newPassword: string }`；清空 `newPassword` 的时机是 `open` 变为 false 时；触发事件后绝不渲染已提交的密码。

- [ ] **步骤 7：添加用户路由页面**

创建 `pages/iam/users/index.vue`：

```vue
<script setup lang="ts">
import IamListToolbar from '@/components/iam/IamListToolbar.vue'
import IamPageHeader from '@/components/iam/IamPageHeader.vue'
import UsersTable from '@/components/iam/UsersTable.vue'
import UserCreateDialog from '@/components/iam/UserCreateDialog.vue'
import UserEditDialog from '@/components/iam/UserEditDialog.vue'
import UserResetPasswordDialog from '@/components/iam/UserResetPasswordDialog.vue'
import { useIamUsers } from '@/composables/useIamAdmin'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription, AlertTitle } from '@nerv-iip/ui'
import type { ConsoleIamUserResponse } from '@nerv-iip/api-client'
import { ref, watch } from 'vue'
import { toast } from 'vue-sonner'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Users',
  },
})

const users = useIamUsers()
const createOpen = ref(false)
const editOpen = ref(false)
const resetOpen = ref(false)
const selectedUser = ref<ConsoleIamUserResponse>()
const statusFilter = ref('')

watch(statusFilter, (value) => {
  users.filters.filterEnabled =
    value === 'enabled' ? true : value === 'disabled' ? false : undefined
})

async function createUser(payload: { loginName: string; email: string; password?: string }) {
  await users.createUser({ body: { loginName: payload.loginName, email: payload.email, password: payload.password ?? '' } })
  createOpen.value = false
  await users.refreshUsers()
  toast.success('User created')
}

function editUser(user: ConsoleIamUserResponse) {
  selectedUser.value = user
  editOpen.value = true
}

async function submitEdit(payload: { loginName: string; email: string; enabled?: boolean }) {
  if (!selectedUser.value) return
  await users.updateUser({
    path: { userId: selectedUser.value.userId },
    body: { loginName: payload.loginName, email: payload.email, enabled: payload.enabled ?? selectedUser.value.enabled },
  })
  editOpen.value = false
  await users.refreshUsers()
  toast.success('User updated')
}

function resetPassword(user: ConsoleIamUserResponse) {
  selectedUser.value = user
  resetOpen.value = true
}

async function submitReset(payload: { newPassword: string }) {
  if (!selectedUser.value) return
  await users.resetPassword({ path: { userId: selectedUser.value.userId }, body: payload })
  resetOpen.value = false
  toast.success('Password reset')
}

async function disableUser(user: ConsoleIamUserResponse) {
  await users.disableUser({ path: { userId: user.userId } })
  await users.refreshUsers()
  toast.success('User disabled')
}
</script>

<template>
  <DefaultLayout>
    <section class="flex flex-col gap-6">
      <IamPageHeader title="Users" description="Manage platform administrators and operators." />
      <IamListToolbar
        v-model:search="users.filters.filterSearch"
        action-label="Create user"
        search-label="Search users"
        search-placeholder="Search login, email or user ID"
        v-model:status="statusFilter"
        :status-options="[
          { label: 'Enabled', value: 'enabled' },
          { label: 'Disabled', value: 'disabled' },
        ]"
        @action="createOpen = true"
      />
      <Alert v-if="users.listError.value" variant="destructive">
        <AlertTitle>Unable to load users</AlertTitle>
        <AlertDescription>{{ users.listError.value.message }}</AlertDescription>
      </Alert>
      <UsersTable
        :pending="users.listPending.value"
        :users="users.users.value"
        @disable="disableUser"
        @edit="editUser"
        @reset-password="resetPassword"
      />
      <UserCreateDialog v-model:open="createOpen" :pending="users.createUserPending.value" @submit="createUser" />
      <UserEditDialog v-model:open="editOpen" :pending="users.updateUserPending.value" :user="selectedUser" @submit="submitEdit" />
      <UserResetPasswordDialog v-model:open="resetOpen" :pending="users.resetPasswordPending.value" :user="selectedUser" @submit="submitReset" />
    </section>
  </DefaultLayout>
</template>
```

- [ ] **步骤 8：运行用户页面测试**

运行：

```powershell
pnpm -C frontend test apps/console/src/pages/iam/users/index.test.ts
```

预期：通过。

- [ ] **步骤 9：提交用户页面**

运行：

```powershell
git add frontend/apps/console/src/components/iam frontend/apps/console/src/pages/iam/users
git commit -m "feat: add iam users console page"
```

预期：提交成功。

## 任务 7：构建 IAM 角色页面和权限编辑器

**文件：**

- 创建：`frontend/apps/console/src/components/iam/PermissionCodeBadge.vue`
- 创建：`frontend/apps/console/src/components/iam/RolesTable.vue`
- 创建：`frontend/apps/console/src/components/iam/RoleCreateDialog.vue`
- 创建：`frontend/apps/console/src/components/iam/RolePermissionEditor.vue`
- 创建：`frontend/apps/console/src/pages/iam/roles/index.vue`
- 创建：`frontend/apps/console/src/pages/iam/roles/index.test.ts`

- [ ] **步骤 1：编写会失败的角色页面测试**

创建 `pages/iam/roles/index.test.ts`：

```ts
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import RolesPage from './index.vue'

vi.mock('@/composables/useIamAdmin', () => ({
  useIamRoles: () => ({
    createRole: vi.fn(),
    createRolePending: { value: false },
    filters: { pageIndex: 1, pageSize: 20, filterSearch: '' },
    listError: { value: undefined },
    listPending: { value: false },
    permissionError: { value: undefined },
    permissionPending: { value: false },
    permissions: {
      value: [
        { code: 'iam.users.read', domain: 'iam', description: 'Read IAM users.', seeded: true },
        { code: 'ops.tasks.read', domain: 'ops', description: 'Read operation tasks.', seeded: true },
      ],
    },
    refreshRoles: vi.fn(),
    roles: {
      value: [
        { roleId: 'role-platform-admin', roleName: 'Platform Administrator', permissionCodes: ['iam.users.read'] },
      ],
    },
    totalCount: { value: 1 },
    updateRolePermissions: vi.fn(),
    updateRolePermissionsPending: { value: false },
  }),
}))

describe('IAM roles page', () => {
  it('renders roles and permission catalog', () => {
    const wrapper = mount(RolesPage, { global: { stubs: { Teleport: true } } })

    expect(wrapper.get('h1').text()).toBe('Roles')
    expect(wrapper.text()).toContain('Platform Administrator')
    expect(wrapper.text()).toContain('iam.users.read')
    expect(wrapper.text()).toContain('Create role')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })
})
```

- [ ] **步骤 2：运行角色页面测试并确认测试为红**

运行：

```powershell
pnpm -C frontend test apps/console/src/pages/iam/roles/index.test.ts
```

预期：失败，因为角色组件和页面尚不存在。

- [ ] **步骤 3：添加 PermissionCodeBadge**

创建 `PermissionCodeBadge.vue`：

```vue
<script setup lang="ts">
import { Badge } from '@nerv-iip/ui'

defineProps<{
  code: string
}>()
</script>

<template>
  <Badge class="font-mono" variant="secondary">{{ code }}</Badge>
</template>
```

- [ ] **步骤 4：添加角色表格**

创建 `RolesTable.vue`，包含以下列：角色名称、角色 ID、权限数量、关键权限、操作。使用 `Table`、`Badge`、`DropdownMenu`、`Button`、`MoreHorizontalIcon`，并触发 `editPermissions` 事件。

行操作必须触发：

```ts
const emit = defineEmits<{
  editPermissions: [role: ConsoleIamRoleResponse]
}>()
```

- [ ] **步骤 5：添加权限编辑器**

创建 `RolePermissionEditor.vue`：

```vue
<script setup lang="ts">
import { Checkbox, Field, FieldGroup, FieldLabel, Input } from '@nerv-iip/ui'
import type { ConsoleIamPermissionResponse } from '@nerv-iip/api-client'
import { computed, ref } from 'vue'

const model = defineModel<string[]>({ default: [] })

const props = defineProps<{
  permissions: ConsoleIamPermissionResponse[]
}>()

const search = ref('')

const groupedPermissions = computed(() => {
  const normalizedSearch = search.value.trim().toLowerCase()
  const filtered = props.permissions.filter((permission) => {
    return (
      normalizedSearch.length === 0 ||
      permission.code.toLowerCase().includes(normalizedSearch) ||
      permission.description.toLowerCase().includes(normalizedSearch)
    )
  })

  return filtered.reduce<Record<string, ConsoleIamPermissionResponse[]>>((groups, permission) => {
    const domain = permission.domain || permission.code.split('.')[0] || 'platform'
    groups[domain] = [...(groups[domain] ?? []), permission]
    return groups
  }, {})
})

function toggle(code: string, checked: boolean | 'indeterminate') {
  const selected = new Set(model.value)
  if (checked === true) {
    selected.add(code)
  } else {
    selected.delete(code)
  }
  model.value = [...selected].sort()
}
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel for="permission-search">Search permissions</FieldLabel>
      <Input id="permission-search" v-model="search" placeholder="Search code or description" />
    </Field>
    <p class="text-sm text-muted-foreground">{{ model.length }} selected</p>
    <section v-for="(items, domain) in groupedPermissions" :key="domain" class="flex flex-col gap-3 rounded-lg border p-3">
      <h3 class="text-sm font-semibold uppercase text-muted-foreground">{{ domain }}</h3>
      <label v-for="permission in items" :key="permission.code" class="flex items-start gap-3">
        <Checkbox
          :checked="model.includes(permission.code)"
          :aria-label="`Select ${permission.code}`"
          @update:checked="toggle(permission.code, $event)"
        />
        <span class="grid gap-1">
          <span class="font-mono text-sm">{{ permission.code }}</span>
          <span class="text-sm text-muted-foreground">{{ permission.description }}</span>
        </span>
      </label>
    </section>
  </FieldGroup>
</template>
```

- [ ] **步骤 6：添加角色对话框**

创建 `RoleCreateDialog.vue`，使用 `Dialog`、`FieldGroup`、`Field`、`Input`、`RolePermissionEditor` 和 `Button`。它会触发：

```ts
const emit = defineEmits<{
  submit: [payload: { roleName: string; permissionCodes: string[] }]
}>()
```

编辑现有角色权限时，角色页面可以将 `Dialog` 与 `RolePermissionEditor` 和所选角色状态直接复用。除非模板长度超出路由页面可清晰容纳的范围，否则不要创建第二个文件。

- [ ] **步骤 7：添加角色路由页面**

创建 `pages/iam/roles/index.vue`，使用 `IamPageHeader`、`IamListToolbar`、`RolesTable`、`RoleCreateDialog`、`RolePermissionEditor`、`Dialog`、`Alert` 和 `toast`。该页面必须：

```ts
definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Roles',
  },
})
```

使用以下提交处理器：

```ts
async function createRole(payload: { roleName: string; permissionCodes: string[] }) {
  await roles.createRole({ body: payload })
  createOpen.value = false
  await roles.refreshRoles()
  toast.success('Role created')
}

async function savePermissions() {
  if (!selectedRole.value) return
  await roles.updateRolePermissions({
    path: { roleId: selectedRole.value.roleId },
    body: { permissionCodes: selectedPermissionCodes.value },
  })
  editOpen.value = false
  await roles.refreshRoles()
  toast.success('Role permissions updated')
}
```

当 `selectedRole?.roleId === 'role-platform-admin'` 时，在权限编辑器对话框中渲染 Alert：

```vue
<Alert>
  <AlertTitle>Administrator role</AlertTitle>
  <AlertDescription>Removing IAM management permissions from this role can block future role edits.</AlertDescription>
</Alert>
```

- [ ] **步骤 8：运行角色页面测试**

运行：

```powershell
pnpm -C frontend test apps/console/src/pages/iam/roles/index.test.ts
```

预期：通过。

- [ ] **步骤 9：提交角色页面**

运行：

```powershell
git add frontend/apps/console/src/components/iam frontend/apps/console/src/pages/iam/roles
git commit -m "feat: add iam roles console page"
```

预期：提交成功。

## 任务 8：构建 IAM 会话页面

**文件：**

- 创建：`frontend/apps/console/src/components/iam/SessionsTable.vue`
- 创建：`frontend/apps/console/src/components/iam/RevokeSessionDialog.vue`
- 创建：`frontend/apps/console/src/pages/iam/sessions/index.vue`
- 创建：`frontend/apps/console/src/pages/iam/sessions/index.test.ts`

- [ ] **步骤 1：编写会失败的会话页面测试**

创建 `pages/iam/sessions/index.test.ts`：

```ts
import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import SessionsPage from './index.vue'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ sessionId: 'session-current' }),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamSessions: () => ({
    filters: { pageIndex: 1, pageSize: 20, filterSearch: '', filterRevoked: false },
    listError: { value: undefined },
    listPending: { value: false },
    refreshSessions: vi.fn(),
    revokeSession: vi.fn(),
    revokeSessionPending: { value: false },
    sessions: {
      value: [
        {
          sessionId: 'session-1',
          userId: 'user-admin',
          issuedAtUtc: '2026-05-20T08:00:00Z',
          expiresAtUtc: '2026-05-21T08:00:00Z',
          revokedAtUtc: null,
          permissionVersion: 1,
        },
      ],
    },
    totalCount: { value: 1 },
  }),
}))

describe('IAM sessions page', () => {
  it('renders active sessions and revoke action', () => {
    const wrapper = mount(SessionsPage, { global: { stubs: { Teleport: true } } })

    expect(wrapper.get('h1').text()).toBe('Sessions')
    expect(wrapper.text()).toContain('session-1')
    expect(wrapper.text()).toContain('Revoke')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })
})
```

- [ ] **步骤 2：运行会话页面测试并确认测试为红**

运行：

```powershell
pnpm -C frontend test apps/console/src/pages/iam/sessions/index.test.ts
```

预期：失败，因为会话组件和页面尚不存在。

- [ ] **步骤 3：添加会话表格**

创建 `SessionsTable.vue`，使用 shadcn `Table`、`Badge`、`Button` 和 `Skeleton`。列如下：

```text
Session ID
User ID
Issued at
Expires at
State
Permission version
Actions
```

Props（属性）和触发事件：

```ts
defineProps<{
  currentSessionId?: string
  pending?: boolean
  sessions: ConsoleIamSessionResponse[]
}>()

const emit = defineEmits<{
  revoke: [session: ConsoleIamSessionResponse]
}>()
```

徽章变体：

```ts
function sessionState(session: ConsoleIamSessionResponse) {
  return session.revokedAtUtc ? 'Revoked' : 'Active'
}
```

存在 `session.revokedAtUtc` 时禁用撤销按钮。

- [ ] **步骤 4：添加撤销对话框**

创建 `RevokeSessionDialog.vue`，使用 `AlertDialog`。当 `session.sessionId === currentSessionId` 时，它必须显示警告：

```vue
<AlertDialogDescription>
  Revoking {{ session?.sessionId }} ends the refresh path for this session.
  <span v-if="session?.sessionId === currentSessionId">This is your current session and you may be signed out.</span>
</AlertDialogDescription>
```

触发事件：

```ts
const emit = defineEmits<{
  confirm: [sessionId: string]
}>()
```

- [ ] **步骤 5：添加会话路由页面**

创建 `pages/iam/sessions/index.vue`，其中包含：

```ts
definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Sessions',
  },
})
```

使用 `useAuthStore()` 获取当前会话 ID，使用 `useIamSessions()` 获取数据，使用 `IamListToolbar` 搜索/筛选，使用 `SessionsTable` 展示列表，使用 `RevokeSessionDialog` 确认，并在变更后调用 `toast.success('Session revoked')`。

撤销处理器：

```ts
async function confirmRevoke(sessionId: string) {
  await sessions.revokeSession({ path: { sessionId } })
  revokeOpen.value = false
  await sessions.refreshSessions()
  toast.success('Session revoked')
}
```

- [ ] **步骤 6：运行会话页面测试**

运行：

```powershell
pnpm -C frontend test apps/console/src/pages/iam/sessions/index.test.ts
```

预期：通过。

- [ ] **步骤 7：提交会话页面**

运行：

```powershell
git add frontend/apps/console/src/components/iam frontend/apps/console/src/pages/iam/sessions
git commit -m "feat: add iam sessions console page"
```

预期：提交成功。

## 任务 9：添加 E2E 覆盖、文档和最终验证

**文件：**

- 创建：`frontend/apps/console/e2e/iam-admin.spec.ts`
- 修改：`frontend/apps/console/e2e/console.spec.ts`
- 修改：`docs/architecture/frontend-structure.md`
- 修改：`docs/architecture/iam-authentication-baseline.md`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：添加 IAM 管理 E2E 路由夹具**

创建 `frontend/apps/console/e2e/iam-admin.spec.ts`：

```ts
import { expect, test, type Route } from '@playwright/test'

const principal = {
  principalId: 'user-admin',
  principalType: 'user',
  loginName: 'admin',
  email: 'admin@nerv-iip.local',
  organizationId: 'org-1',
  environmentId: 'env-1',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-current',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
})

test('admin manages users roles and sessions', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Login name').fill('admin')
  await page.getByLabel('Password').fill('Admin123!')
  await page.getByRole('button', { name: 'Sign in' }).click()

  await page.getByRole('link', { name: 'Users' }).click()
  await expect(page.getByRole('heading', { name: 'Users' })).toBeVisible()
  await expect(page.getByText('admin@nerv-iip.local')).toBeVisible()

  await page.getByRole('link', { name: 'Roles' }).click()
  await expect(page.getByRole('heading', { name: 'Roles' })).toBeVisible()
  await expect(page.getByText('Platform Administrator')).toBeVisible()
  await expect(page.getByText('iam.users.read')).toBeVisible()

  await page.getByRole('link', { name: 'Sessions' }).click()
  await expect(page.getByRole('heading', { name: 'Sessions' })).toBeVisible()
  await expect(page.getByText('session-current')).toBeVisible()
})

async function routeConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  const { pathname } = url

  if (pathname === '/api/console/v1/auth/login' || pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }

  if (pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }

  if (pathname === '/api/console/v1/iam/users') {
    return fulfillJson(route, envelope({ pageIndex: 1, pageSize: 20, totalCount: 1, items: [{ userId: 'user-admin', loginName: 'admin', email: 'admin@nerv-iip.local', enabled: true }] }))
  }

  if (pathname === '/api/console/v1/iam/roles') {
    return fulfillJson(route, envelope({ pageIndex: 1, pageSize: 20, totalCount: 1, items: [{ roleId: 'role-platform-admin', roleName: 'Platform Administrator', permissionCodes: ['iam.users.read'] }] }))
  }

  if (pathname === '/api/console/v1/iam/permissions') {
    return fulfillJson(route, envelope({ items: [{ code: 'iam.users.read', domain: 'iam', description: 'Read IAM users.', seeded: true }] }))
  }

  if (pathname === '/api/console/v1/iam/sessions') {
    return fulfillJson(route, envelope({ pageIndex: 1, pageSize: 20, totalCount: 1, items: [{ sessionId: 'session-current', userId: 'user-admin', issuedAtUtc: '2026-05-20T08:00:00Z', expiresAtUtc: '2099-01-01T00:00:00Z', revokedAtUtc: null, permissionVersion: 1 }] }))
  }

  return route.fallback()
}

function envelope<T>(data: T) {
  return { success: true, data }
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}
```

- [ ] **步骤 2：运行 IAM E2E 并确认测试为绿**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/console e2e -- iam-admin.spec.ts
```

预期：通过。

- [ ] **步骤 3：浏览器验证**

启动 Console 开发服务器：

```powershell
pnpm -C frontend --filter @nerv-iip/console dev
```

打开所提供的 URL 并验证：

```text
/iam/users
/iam/roles
/iam/sessions
```

预期：桌面端和移动端宽度下渲染均无文本重叠；主要操作、焦点环和所选导航使用蓝色；状态徽章不使用蓝色表达危险或成功语义；对话框具有无障碍标题。

- [ ] **步骤 4：更新架构文档**

应用以下具体更新：

```text
docs/architecture/frontend-structure.md
  Add IAM admin routes, `src/composables/useIamAdmin.ts`, and the rule that IAM pages consume generated Gateway api-client exports only.

docs/architecture/iam-authentication-baseline.md
  Mark role create, role permission patch, permission catalog, user reset password and Console admin facade as Phase 8 delivered.

docs/architecture/authorization-matrix.md
  Add Console facade route mappings for iam.users.read/manage, iam.roles.read/manage, iam.sessions.read/revoke.

docs/architecture/api-contract-and-codegen.md
  Add the eleven Console IAM operation IDs and note that OpenAPI/api-client regeneration is required after Gateway facade changes.

docs/architecture/implementation-readiness.md
  Move Phase 8 IAM Admin Console from planned to implemented after verification passes.

README.md
  Update current progress to mention the blue design-system baseline and IAM admin workflow.
```

- [ ] **步骤 5：运行聚焦的前后端检查**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
pnpm -C frontend test
pnpm -C frontend typecheck
pnpm -C frontend build
```

预期：通过。

- [ ] **步骤 6：运行完整验证门禁**

运行：

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

预期：通过。如果运行 `verify-third-slice-console.ps1` 时 OpenAPI/api-client 生成流程更改了文件，请检查差异；只有 Gateway 契约作为 Phase 8 的一部分发生变更时，才暂存生成文件。

- [ ] **步骤 7：提交最终文档和 E2E**

运行：

```powershell
git add frontend/apps/console/e2e docs/architecture README.md
git commit -m "docs: finalize phase 8 iam admin readiness"
```

预期：提交成功。

## 规格覆盖清单

1. 蓝色 Calm Control Plane 令牌基线：任务 1。
2. shadcn-vue 组件治理和 `@nerv-iip/ui` 导出：任务 1。
3. 新的 IAM 页面均不使用旧版令牌：任务 6、7 和 8 的测试。
4. PostgreSQL 角色创建和权限更新：任务 2。
5. 权限目录只来自初始权限：任务 2。
6. 通过 Console 创建/编辑/禁用用户和重置密码：任务 2、3、4、5 和 6。
7. 通过 Console 创建角色/编辑权限：任务 2、3、4、5 和 7。
8. 通过 Console 查看/撤销会话：任务 2、3、4、5 和 8。
9. Gateway 在转发前检查 IAM 权限：任务 3。
10. 稳定的 OpenAPI 操作 ID 和 api-client 重新生成：任务 3 和 4。
11. 单元、集成、前端和 E2E 覆盖：任务 2 至 9。
12. 桌面端/移动端/对话框/焦点/无重叠的浏览器验证：任务 9。

## 最终验证清单

在创建 PR 或合并此分支前，验证：

```powershell
git status --short
git log --oneline -n 12
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

预期最终状态：所有检查均通过，生成的 OpenAPI/api-client 文件与 Gateway 操作 ID 一致，且仅剩属于 Phase 8 实施分支的未处理差异。

## 自审

规格覆盖：范围内的每项后端、Gateway、api-client、设计系统、前端页面、E2E、浏览器验证和文档要求，都至少映射到上述一个任务。

危险信号扫描：本计划避免开放式缺口，并明确列出准确文件、命令、请求形态、响应形态、操作 ID 和组件边界。

类型一致性：Console IAM 模型名称在 Gateway、生成的 api-client 别名、组合式函数和 Vue 组件中统一使用 `ConsoleIam...` 前缀。后端 IAM 角色/用户/会话响应名称继续限定在服务内部，Gateway 不引用 IAM Domain 或 Infrastructure 类型。
