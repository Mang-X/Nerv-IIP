# Phase 8 IAM Admin Console And Design System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the Phase 8 blue Calm Control Plane design-system baseline and the Console IAM admin workflow for users, roles, permissions and sessions.

**Architecture:** The Console continues to call PlatformGateway only. PlatformGateway exposes Console IAM Admin facade endpoints, checks IAM-backed permissions using the current principal organization/environment, forwards the original bearer token to IAM, and maps downstream failures consistently. IAM remains the source of identity, role, permission and session facts; the frontend consumes generated Gateway OpenAPI types through stable `@nerv-iip/api-client` exports and composes thin Vue route pages from focused feature components.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, NetCorePal/CleanDDD, Entity Framework Core, xUnit, ASP.NET Core `WebApplicationFactory`, PostgreSQL profile tests, Vue 3 `<script setup lang="ts">`, Vue Router file routes, Pinia, Pinia Colada, Vite, Vitest, Playwright, Tailwind CSS v4, shadcn-vue `reka-nova`, lucide-vue-next, Hey API OpenAPI TypeScript.

---

## Approved Spec

Implementation source: `docs/superpowers/specs/2026-05-20-iam-admin-console-design-system-design.md`.

The spec selected approach A: **IAM Admin Console & Role Permission Completion**, preceded by a current-stage design-system baseline. Yesterday's 2026-05-19 commits already delivered persisted user CRUD, session listing/revoke, Gateway permission enforcement, Console auth and shadcn-vue bootstrap. Phase 8 must therefore focus on the remaining role/permission mutations, password reset, Console admin facade and frontend admin surfaces.

## Current Baseline

1. Current branch is `codex/phase-8-iam-admin-design-system-spec`.
2. `frontend/components.json` exists and uses `style: reka-nova`, `font: geist-sans`, Tailwind v4 CSS file `apps/console/src/assets/main.css`, `iconLibrary: lucide`, and aliases pointing at `packages/ui`.
3. `pnpm dlx shadcn-vue@latest docs table dialog alert-dialog checkbox select pagination empty` currently fails from `frontend` with `Failed to load tsconfig.json` because the workspace has `tsconfig.base.json` and package-level configs, but no root `frontend/tsconfig.json`.
4. Existing UI exports are Button, Card, Field, Input, Alert, Badge, Separator, Skeleton, DropdownMenu, Avatar, Toaster and Spinner.
5. `frontend/apps/console/src/assets/main.css` still has neutral shadcn primary tokens and legacy compatibility tokens. New IAM pages must use semantic shadcn/Tailwind tokens rather than `--legacy-color-*`.
6. `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Roles/IamRoleApplicationService.cs` returns a dummy in-memory role id and `501` for PostgreSQL role create/permission patch.
7. IAM user create/update/disable and session list/revoke are present. User reset password is not present.
8. PlatformGateway has Console auth endpoints and instance/operation endpoints, but no Console IAM Admin facade.
9. `frontend/packages/api-client` already generates fetch SDK, TypeScript types and Pinia Colada options from `frontend/packages/api-client/openapi/platform-gateway.v1.json`.

## File Structure Map

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

## Implementation Rules

1. Use TDD for production behavior: write the failing test, run it and confirm the expected failure, then implement the smallest passing code.
2. For shadcn-vue work, run CLI commands from `frontend`, review generated files, and export new components through `@nerv-iip/ui` before app usage.
3. Vue files use Composition API with `<script setup lang="ts">`; route pages stay thin and feature logic lives in `useIamAdmin.ts`.
4. New IAM admin UI imports UI primitives only from `@nerv-iip/ui`; app code must not deep import from `frontend/packages/ui/src/components/ui/*`.
5. New IAM admin UI uses semantic Tailwind/shadcn tokens such as `bg-background`, `text-muted-foreground`, `border-border`, `bg-primary`, `ring-ring` and component variants. It must not use `--legacy-color-*`.
6. IAM application services pass `CancellationToken`, use `KnownException` for business failures, and do not call `SaveChanges` manually.
7. Gateway admin endpoints must call IAM authorization before forwarding the admin request to IAM.

## Task 1: Establish Blue Design System Baseline And shadcn Components

**Files:**

- Create: `frontend/tsconfig.json`
- Create: `frontend/packages/ui/src/design-system.contract.test.ts`
- Modify: `frontend/apps/console/src/assets/main.css`
- Modify: `frontend/packages/ui/src/index.ts`
- Add through CLI: `frontend/packages/ui/src/components/ui/table/**`
- Add through CLI: `frontend/packages/ui/src/components/ui/dialog/**`
- Add through CLI: `frontend/packages/ui/src/components/ui/alert-dialog/**`
- Add through CLI: `frontend/packages/ui/src/components/ui/checkbox/**`
- Add through CLI: `frontend/packages/ui/src/components/ui/select/**`
- Add through CLI: `frontend/packages/ui/src/components/ui/pagination/**`
- Add through CLI: `frontend/packages/ui/src/components/ui/empty/**`
- Modify: `docs/architecture/frontend-design-system-planning.md`

- [ ] **Step 1: Write failing design-system contract test**

Create `frontend/packages/ui/src/design-system.contract.test.ts`:

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

- [ ] **Step 2: Run the design-system contract test and confirm RED**

Run:

```powershell
pnpm -C frontend test packages/ui/src/design-system.contract.test.ts
```

Expected: FAIL because `--primary`, `--ring`, `--accent`, `--sidebar-primary`, `--chart-1` and `--radius` still use neutral baseline values.

- [ ] **Step 3: Add a root TypeScript config for shadcn-vue CLI**

Create `frontend/tsconfig.json`:

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

- [ ] **Step 4: Verify shadcn-vue project context**

Run:

```powershell
pnpm -C frontend dlx shadcn-vue@latest info --json
pnpm -C frontend dlx shadcn-vue@latest docs table dialog alert-dialog checkbox select pagination empty
```

Expected: `info --json` reports `reka-nova`, Tailwind v4, `lucide`, and resolved UI paths under `packages/ui/src/components/ui`. The docs command prints component documentation URLs. If the docs command still fails, run the component add command in the next step and review every generated file before export.

- [ ] **Step 5: Add required shadcn-vue components**

Run:

```powershell
pnpm -C frontend dlx shadcn-vue@latest add table dialog alert-dialog checkbox select pagination empty
```

Expected: component source files are added under `frontend/packages/ui/src/components/ui`.

- [ ] **Step 6: Export the new UI primitives**

Modify `frontend/packages/ui/src/index.ts` by adding these exports after the existing component exports:

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

If generated `index.ts` files expose a slightly different component name, use the exact generated export and keep the public barrel export complete for every generated subcomponent.

- [ ] **Step 7: Apply Phase 8 blue tokens**

In `frontend/apps/console/src/assets/main.css`, replace the current neutral shadcn token values in `:root` with:

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

Keep the existing legacy token block at the top of `:root` so old instance pages keep rendering while Phase 8 pages move to semantic tokens.

- [ ] **Step 8: Run contract test and UI package typecheck**

Run:

```powershell
pnpm -C frontend test packages/ui/src/design-system.contract.test.ts
pnpm -C frontend --filter @nerv-iip/ui typecheck
```

Expected: PASS.

- [ ] **Step 9: Document the design-system baseline**

Update `docs/architecture/frontend-design-system-planning.md` with these concrete sections:

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

- [ ] **Step 10: Commit design-system baseline**

Run:

```powershell
git add frontend/tsconfig.json frontend/apps/console/src/assets/main.css frontend/packages/ui docs/architecture/frontend-design-system-planning.md
git commit -m "feat: establish phase 8 console design system"
```

Expected: commit succeeds.

## Task 2: Complete IAM Role Mutation, Permission Catalog And Password Reset

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Domain/IamFacts.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/InMemoryIamStore.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Roles/IamRoleApplicationService.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Users/IamUserApplicationService.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Commands/Users/ResetUserPasswordCommand.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Permissions/IamPermissionCatalog.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs`
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamFoundationTests.cs`
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamManagementEndpointAuthorizationTests.cs`

- [ ] **Step 1: Write failing in-memory role and permission tests**

In `IamFoundationTests.cs`, add:

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

- [ ] **Step 2: Write failing reset-password test**

In `IamFoundationTests.cs`, add:

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

- [ ] **Step 3: Run IAM tests and confirm RED**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter "In_memory_role_management_creates_role_updates_permissions_and_lists_catalog|In_memory_role_management_rejects_unknown_permissions_and_duplicate_names|Admin_reset_password_changes_login_secret_and_revokes_sessions"
```

Expected: FAIL with missing `/api/iam/v1/permissions`, current dummy role response or missing `/reset-password`.

- [ ] **Step 4: Add permission catalog model**

Create `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Permissions/IamPermissionCatalog.cs`:

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

- [ ] **Step 5: Extend in-memory store for roles and reset password**

In `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/InMemoryIamStore.cs`, add these methods:

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

- [ ] **Step 6: Extend role repository**

In `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Repositories/IamRepositories.cs`, extend `IRoleRepository`:

```csharp
Task<Role?> GetByIdAsync(RoleId roleId, CancellationToken cancellationToken = default);
Task<Role?> GetByNameAsync(string roleName, CancellationToken cancellationToken = default);
```

Add implementations to `RoleRepository`:

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

Extend `IUserSessionRepository`:

```csharp
Task<IReadOnlyList<UserSession>> ListActiveByUserIdAsync(UserId userId, DateTimeOffset now, CancellationToken cancellationToken = default);
```

Add implementation:

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

- [ ] **Step 7: Replace role mutation service contract**

In `IamRoleApplicationService.cs`, replace the mutation records and interface methods with:

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

In `InMemoryIamRoleApplicationService`, implement:

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

In `PostgreSqlIamRoleApplicationService`, implement:

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

- [ ] **Step 8: Add user reset password command and service method**

Extend `IIamUserApplicationService` in `IamUserApplicationService.cs`:

```csharp
Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken);
```

Add to `InMemoryIamUserApplicationService`:

```csharp
public Task ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken)
{
    store.ResetPassword(userId, newPassword);
    return Task.CompletedTask;
}
```

Change `PostgreSqlIamUserApplicationService` constructor to include `IUserSessionRepository sessionRepository`, then add:

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

Create `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Commands/Users/ResetUserPasswordCommand.cs`:

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

- [ ] **Step 9: Wire role, permission and reset-password endpoints**

In `RoleEndpoints.cs`, change create endpoint to read `CreateRoleRequest`, call `CreateRoleAsync(req.RoleName, req.PermissionCodes, ct)` and return `201` with `RoleResponse`.

```csharp
var req = await HttpContext.Request.ReadFromJsonAsync<CreateRoleRequest>(ct)
    ?? throw new BadHttpRequestException("Request body is required.");
var response = await roles.CreateRoleAsync(req.RoleName, req.PermissionCodes, ct);
await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCodes.Status201Created, response, ct);
```

Change patch endpoint to read `PatchRolePermissionsRequest`, call `PatchRolePermissionsAsync(Route<string>("roleId")!, req.PermissionCodes, ct)` and return `200`.

Add permission catalog endpoint to `RoleEndpoints.cs`:

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

In `UserEndpoints.cs`, add:

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

- [ ] **Step 10: Update anonymous authorization coverage**

In `IamManagementEndpointAuthorizationTests.cs`, add these inline data rows:

```csharp
[InlineData("POST", "/api/iam/v1/users/user-admin/reset-password")]
[InlineData("GET", "/api/iam/v1/permissions")]
```

- [ ] **Step 11: Run IAM in-memory tests and confirm GREEN**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter "In_memory_role_management_creates_role_updates_permissions_and_lists_catalog|In_memory_role_management_rejects_unknown_permissions_and_duplicate_names|Admin_reset_password_changes_login_secret_and_revokes_sessions|Postgres_management_endpoints_reject_anonymous_callers_before_touching_persistence"
```

Expected: PASS.

- [ ] **Step 12: Add PostgreSQL profile tests**

In `IamPostgresProfileTests.cs`, add a test named `Postgres_profile_persists_role_mutation_permission_catalog_and_password_reset` using the same environment setup pattern as `Postgres_profile_persists_user_create_update_and_disable_commands`. The test must:

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

Then assert through `ApplicationDbContext` that the role has exactly `iam.users.read` and `ops.tasks.read`, and the reset user's password hash does not contain either cleartext password.

- [ ] **Step 13: Run PostgreSQL profile when test database is configured**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter Postgres_profile_persists_role_mutation_permission_catalog_and_password_reset
```

Expected: PASS when `NERV_IIP_TEST_POSTGRES` is set. If it is not set, the test exits without assertions by existing profile-test convention.

- [ ] **Step 14: Commit IAM backend completion**

Run:

```powershell
git add backend/services/Iam
git commit -m "feat: complete iam admin mutations"
```

Expected: commit succeeds.

## Task 3: Add PlatformGateway Console IAM Admin Facade

**Files:**

- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorization.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/IamAdmin/ConsoleIamAdminModels.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/IamAdmin/GatewayIamAdminClient.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/IamAdmin/ConsoleIamAdminEndpoints.cs`
- Create: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayConsoleIamAdminTests.cs`
- Modify: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOpenApiTests.cs`

- [ ] **Step 1: Write failing Gateway facade tests**

Create `GatewayConsoleIamAdminTests.cs`:

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

Add fake classes in the same file:

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

- [ ] **Step 2: Run Gateway tests and confirm RED**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter GatewayConsoleIamAdminTests
```

Expected: FAIL because `IGatewayIamAdminClient`, models and endpoints do not exist.

- [ ] **Step 3: Add Console IAM admin models**

Create `Application/IamAdmin/ConsoleIamAdminModels.cs`:

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

- [ ] **Step 4: Add Gateway IAM admin client**

Create `Application/IamAdmin/GatewayIamAdminClient.cs`:

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

- [ ] **Step 5: Add authorization helper for current Console principal**

In `GatewayAuthorization.cs`, add:

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

- [ ] **Step 6: Add facade endpoints**

Create `Endpoints/IamAdmin/ConsoleIamAdminEndpoints.cs` with one endpoint class per route. Use this pattern for each endpoint:

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

Add the remaining endpoint classes with these permission mappings and response status rules:

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

- [ ] **Step 7: Register Gateway IAM admin client**

In `Program.cs`, add:

```csharp
using Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;
using Nerv.IIP.PlatformGateway.Web.Endpoints.IamAdmin;
```

Register the HTTP client:

```csharp
builder.Services.AddHttpClient<IGatewayIamAdminClient, HttpGatewayIamAdminClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
```

- [ ] **Step 8: Add stable operation IDs**

In `Program.cs`, extend the endpoint name generator switch:

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

- [ ] **Step 9: Update OpenAPI test**

In `GatewayOpenApiTests.cs`, add assertions that `/swagger/v1/swagger.json` contains each operation ID from Step 8.

- [ ] **Step 10: Run Gateway tests and confirm GREEN**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter "GatewayConsoleIamAdminTests|Gateway_exports_console_openapi_document_with_stable_operation_ids"
```

Expected: PASS.

- [ ] **Step 11: Commit Gateway facade**

Run:

```powershell
git add backend/gateway/PlatformGateway
git commit -m "feat: add console iam admin facade"
```

Expected: commit succeeds.

## Task 4: Export Gateway OpenAPI And Add Stable IAM api-client Exports

**Files:**

- Modify generated: `frontend/packages/api-client/openapi/platform-gateway.v1.json`
- Modify generated: `frontend/packages/api-client/src/generated/**`
- Create: `frontend/packages/api-client/src/iam.ts`
- Modify: `frontend/packages/api-client/src/index.ts`
- Modify: `frontend/packages/api-client/src/generated-contract.test.ts`

- [ ] **Step 1: Export Gateway OpenAPI**

Run:

```powershell
pwsh scripts/export-gateway-openapi.ps1
```

Expected: `frontend/packages/api-client/openapi/platform-gateway.v1.json` contains the eleven `listConsoleIam...` and mutation operation IDs.

- [ ] **Step 2: Regenerate api-client**

Run:

```powershell
pnpm -C frontend generate:api
```

Expected: generated files under `frontend/packages/api-client/src/generated` include SDK functions and Pinia Colada options for Console IAM Admin operations.

- [ ] **Step 3: Add stable IAM exports**

Create `frontend/packages/api-client/src/iam.ts`:

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

If a generated type name differs only by namespace flattening, replace the import with the generated name in `types.gen.ts` and keep the public alias name exactly as shown on the left side.

- [ ] **Step 4: Export IAM barrel from package root**

Modify `frontend/packages/api-client/src/index.ts`:

```ts
export { configureApiClient } from './transport/client-config'
export type { ConfigureApiClientOptions } from './transport/client-config'
export * from './auth'
export * from './console'
export * from './iam'
```

- [ ] **Step 5: Add generated contract coverage**

In `frontend/packages/api-client/src/generated-contract.test.ts`, add:

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

- [ ] **Step 6: Run api-client tests and typecheck**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

Expected: PASS.

- [ ] **Step 7: Commit generated contract**

Run:

```powershell
git add frontend/packages/api-client
git commit -m "feat: expose iam admin api client"
```

Expected: commit succeeds.

## Task 5: Add IAM Navigation And Shared Admin Composable

**Files:**

- Modify: `frontend/packages/app-shell/src/AppShell.vue`
- Modify: `frontend/packages/app-shell/src/AppShell.test.ts`
- Modify: `frontend/apps/console/src/layouts/DefaultLayout.vue`
- Modify: `frontend/apps/console/src/layouts/DefaultLayout.test.ts`
- Create: `frontend/apps/console/src/api/iam.ts`
- Create: `frontend/apps/console/src/composables/useIamAdmin.ts`
- Create: `frontend/apps/console/src/composables/useIamAdmin.test.ts`

- [ ] **Step 1: Write failing navigation tests**

In `AppShell.test.ts`, add:

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

In `DefaultLayout.test.ts`, change expected nav to:

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

- [ ] **Step 2: Run navigation tests and confirm RED**

Run:

```powershell
pnpm -C frontend test packages/app-shell/src/AppShell.test.ts apps/console/src/layouts/DefaultLayout.test.ts
```

Expected: FAIL because `NavItem` has no `children` support and DefaultLayout still exposes only Instances.

- [ ] **Step 3: Add grouped navigation support**

In `AppShell.vue`, update `NavItem`:

```ts
interface NavItem {
  label: string
  to?: RouteLocationRaw
  children?: NavItem[]
}
```

Replace the nav template with:

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

Add CSS:

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

- [ ] **Step 4: Add IAM navigation in DefaultLayout**

Update `navItems` in `DefaultLayout.vue`:

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

- [ ] **Step 5: Run navigation tests and confirm GREEN**

Run:

```powershell
pnpm -C frontend test packages/app-shell/src/AppShell.test.ts apps/console/src/layouts/DefaultLayout.test.ts
```

Expected: PASS.

- [ ] **Step 6: Add IAM API error helper**

Create `frontend/apps/console/src/api/iam.ts`:

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

- [ ] **Step 7: Write failing composable test**

Create `frontend/apps/console/src/composables/useIamAdmin.test.ts`:

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

- [ ] **Step 8: Run composable test and confirm RED**

Run:

```powershell
pnpm -C frontend test apps/console/src/composables/useIamAdmin.test.ts
```

Expected: FAIL because `useIamAdmin.ts` does not exist.

- [ ] **Step 9: Implement shared IAM composable**

Create `frontend/apps/console/src/composables/useIamAdmin.ts` with:

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

- [ ] **Step 10: Run composable and navigation tests**

Run:

```powershell
pnpm -C frontend test apps/console/src/composables/useIamAdmin.test.ts packages/app-shell/src/AppShell.test.ts apps/console/src/layouts/DefaultLayout.test.ts
```

Expected: PASS.

- [ ] **Step 11: Commit navigation and composable foundation**

Run:

```powershell
git add frontend/packages/app-shell frontend/apps/console/src/layouts frontend/apps/console/src/api/iam.ts frontend/apps/console/src/composables/useIamAdmin.ts frontend/apps/console/src/composables/useIamAdmin.test.ts
git commit -m "feat: add iam admin navigation foundation"
```

Expected: commit succeeds.

## Task 6: Build IAM Users Page

**Files:**

- Create: `frontend/apps/console/src/components/iam/IamPageHeader.vue`
- Create: `frontend/apps/console/src/components/iam/IamListToolbar.vue`
- Create: `frontend/apps/console/src/components/iam/UsersTable.vue`
- Create: `frontend/apps/console/src/components/iam/UserCreateDialog.vue`
- Create: `frontend/apps/console/src/components/iam/UserEditDialog.vue`
- Create: `frontend/apps/console/src/components/iam/UserResetPasswordDialog.vue`
- Create: `frontend/apps/console/src/pages/iam/users/index.vue`
- Create: `frontend/apps/console/src/pages/iam/users/index.test.ts`

- [ ] **Step 1: Write failing users page test**

Create `pages/iam/users/index.test.ts` with a mocked `@/composables/useIamAdmin`:

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

- [ ] **Step 2: Run users page test and confirm RED**

Run:

```powershell
pnpm -C frontend test apps/console/src/pages/iam/users/index.test.ts
```

Expected: FAIL because the page and components do not exist.

- [ ] **Step 3: Add shared IAM page header**

Create `components/iam/IamPageHeader.vue`:

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

- [ ] **Step 4: Add shared IAM list toolbar**

Create `components/iam/IamListToolbar.vue`:

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

- [ ] **Step 5: Add users table**

Create `components/iam/UsersTable.vue` with props and emits:

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

- [ ] **Step 6: Add user dialogs**

Create `UserCreateDialog.vue`, `UserEditDialog.vue` and `UserResetPasswordDialog.vue` using shadcn `Dialog`, `FieldGroup`, `Field`, `FieldLabel`, `FieldError`, `Input`, and `Button`. Each dialog must:

```ts
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{
  submit: [payload: { loginName: string; email: string; password?: string; enabled?: boolean }]
}>()
```

Use this validation pattern in each submit handler:

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

For `UserResetPasswordDialog.vue`, submit only `{ newPassword: string }`, clear `newPassword` when `open` becomes false, and never render the submitted password after emit.

- [ ] **Step 7: Add users route page**

Create `pages/iam/users/index.vue`:

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

- [ ] **Step 8: Run users page tests**

Run:

```powershell
pnpm -C frontend test apps/console/src/pages/iam/users/index.test.ts
```

Expected: PASS.

- [ ] **Step 9: Commit users page**

Run:

```powershell
git add frontend/apps/console/src/components/iam frontend/apps/console/src/pages/iam/users
git commit -m "feat: add iam users console page"
```

Expected: commit succeeds.

## Task 7: Build IAM Roles Page And Permission Editor

**Files:**

- Create: `frontend/apps/console/src/components/iam/PermissionCodeBadge.vue`
- Create: `frontend/apps/console/src/components/iam/RolesTable.vue`
- Create: `frontend/apps/console/src/components/iam/RoleCreateDialog.vue`
- Create: `frontend/apps/console/src/components/iam/RolePermissionEditor.vue`
- Create: `frontend/apps/console/src/pages/iam/roles/index.vue`
- Create: `frontend/apps/console/src/pages/iam/roles/index.test.ts`

- [ ] **Step 1: Write failing roles page test**

Create `pages/iam/roles/index.test.ts`:

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

- [ ] **Step 2: Run roles page test and confirm RED**

Run:

```powershell
pnpm -C frontend test apps/console/src/pages/iam/roles/index.test.ts
```

Expected: FAIL because roles components and page do not exist.

- [ ] **Step 3: Add PermissionCodeBadge**

Create `PermissionCodeBadge.vue`:

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

- [ ] **Step 4: Add roles table**

Create `RolesTable.vue` with columns: Role name, Role ID, Permission count, Key permissions, Actions. Use `Table`, `Badge`, `DropdownMenu`, `Button`, `MoreHorizontalIcon`, and emit `editPermissions`.

The row action emit must be:

```ts
const emit = defineEmits<{
  editPermissions: [role: ConsoleIamRoleResponse]
}>()
```

- [ ] **Step 5: Add permission editor**

Create `RolePermissionEditor.vue`:

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

- [ ] **Step 6: Add role dialogs**

Create `RoleCreateDialog.vue` using `Dialog`, `FieldGroup`, `Field`, `Input`, `RolePermissionEditor`, and `Button`. It emits:

```ts
const emit = defineEmits<{
  submit: [payload: { roleName: string; permissionCodes: string[] }]
}>()
```

For editing existing role permissions, the roles page can reuse `Dialog` directly with `RolePermissionEditor` and selected role state. Do not create a second file unless the template becomes longer than the route page can hold clearly.

- [ ] **Step 7: Add roles route page**

Create `pages/iam/roles/index.vue` using `IamPageHeader`, `IamListToolbar`, `RolesTable`, `RoleCreateDialog`, `RolePermissionEditor`, `Dialog`, `Alert`, and `toast`. The page must:

```ts
definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Roles',
  },
})
```

Use these submit handlers:

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

Render an Alert in the permission editor dialog when `selectedRole?.roleId === 'role-platform-admin'`:

```vue
<Alert>
  <AlertTitle>Administrator role</AlertTitle>
  <AlertDescription>Removing IAM management permissions from this role can block future role edits.</AlertDescription>
</Alert>
```

- [ ] **Step 8: Run roles page test**

Run:

```powershell
pnpm -C frontend test apps/console/src/pages/iam/roles/index.test.ts
```

Expected: PASS.

- [ ] **Step 9: Commit roles page**

Run:

```powershell
git add frontend/apps/console/src/components/iam frontend/apps/console/src/pages/iam/roles
git commit -m "feat: add iam roles console page"
```

Expected: commit succeeds.

## Task 8: Build IAM Sessions Page

**Files:**

- Create: `frontend/apps/console/src/components/iam/SessionsTable.vue`
- Create: `frontend/apps/console/src/components/iam/RevokeSessionDialog.vue`
- Create: `frontend/apps/console/src/pages/iam/sessions/index.vue`
- Create: `frontend/apps/console/src/pages/iam/sessions/index.test.ts`

- [ ] **Step 1: Write failing sessions page test**

Create `pages/iam/sessions/index.test.ts`:

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

- [ ] **Step 2: Run sessions page test and confirm RED**

Run:

```powershell
pnpm -C frontend test apps/console/src/pages/iam/sessions/index.test.ts
```

Expected: FAIL because sessions components and page do not exist.

- [ ] **Step 3: Add sessions table**

Create `SessionsTable.vue` with shadcn `Table`, `Badge`, `Button`, and `Skeleton`. Columns:

```text
Session ID
User ID
Issued at
Expires at
State
Permission version
Actions
```

Props and emit:

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

Badge variant:

```ts
function sessionState(session: ConsoleIamSessionResponse) {
  return session.revokedAtUtc ? 'Revoked' : 'Active'
}
```

Disable revoke button when `session.revokedAtUtc` is present.

- [ ] **Step 4: Add revoke dialog**

Create `RevokeSessionDialog.vue` using `AlertDialog`. It must show a warning when `session.sessionId === currentSessionId`:

```vue
<AlertDialogDescription>
  Revoking {{ session?.sessionId }} ends the refresh path for this session.
  <span v-if="session?.sessionId === currentSessionId">This is your current session and you may be signed out.</span>
</AlertDialogDescription>
```

Emit:

```ts
const emit = defineEmits<{
  confirm: [sessionId: string]
}>()
```

- [ ] **Step 5: Add sessions route page**

Create `pages/iam/sessions/index.vue` with:

```ts
definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Sessions',
  },
})
```

Use `useAuthStore()` for current session id, `useIamSessions()` for data, `IamListToolbar` for search/filter, `SessionsTable` for list, `RevokeSessionDialog` for confirmation, and `toast.success('Session revoked')` after mutation.

The revoke handler:

```ts
async function confirmRevoke(sessionId: string) {
  await sessions.revokeSession({ path: { sessionId } })
  revokeOpen.value = false
  await sessions.refreshSessions()
  toast.success('Session revoked')
}
```

- [ ] **Step 6: Run sessions page test**

Run:

```powershell
pnpm -C frontend test apps/console/src/pages/iam/sessions/index.test.ts
```

Expected: PASS.

- [ ] **Step 7: Commit sessions page**

Run:

```powershell
git add frontend/apps/console/src/components/iam frontend/apps/console/src/pages/iam/sessions
git commit -m "feat: add iam sessions console page"
```

Expected: commit succeeds.

## Task 9: Add E2E Coverage, Documentation And Final Verification

**Files:**

- Create: `frontend/apps/console/e2e/iam-admin.spec.ts`
- Modify: `frontend/apps/console/e2e/console.spec.ts`
- Modify: `docs/architecture/frontend-structure.md`
- Modify: `docs/architecture/iam-authentication-baseline.md`
- Modify: `docs/architecture/authorization-matrix.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Add IAM admin E2E route fixtures**

Create `frontend/apps/console/e2e/iam-admin.spec.ts`:

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

- [ ] **Step 2: Run IAM E2E and confirm GREEN**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/console e2e -- iam-admin.spec.ts
```

Expected: PASS.

- [ ] **Step 3: Browser verification**

Start the Console dev server:

```powershell
pnpm -C frontend --filter @nerv-iip/console dev
```

Open the served URL and verify:

```text
/iam/users
/iam/roles
/iam/sessions
```

Expected: desktop and mobile widths render without text overlap; primary actions, focus rings and selected navigation use blue; state badges do not use blue for danger or success semantics; dialogs have accessible titles.

- [ ] **Step 4: Update architecture docs**

Apply these concrete updates:

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

- [ ] **Step 5: Run focused frontend and backend checks**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
pnpm -C frontend test
pnpm -C frontend typecheck
pnpm -C frontend build
```

Expected: PASS.

- [ ] **Step 6: Run full verification gates**

Run:

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

Expected: PASS. If OpenAPI/api-client generation changes files during `verify-third-slice-console.ps1`, inspect the diff and stage the generated files only when the Gateway contract changed as part of Phase 8.

- [ ] **Step 7: Commit final docs and E2E**

Run:

```powershell
git add frontend/apps/console/e2e docs/architecture README.md
git commit -m "docs: finalize phase 8 iam admin readiness"
```

Expected: commit succeeds.

## Spec Coverage Checklist

1. Blue Calm Control Plane token baseline: Task 1.
2. shadcn-vue component governance and `@nerv-iip/ui` exports: Task 1.
3. No new IAM pages using legacy tokens: Tasks 6, 7 and 8 tests.
4. PostgreSQL role creation and permission patch: Task 2.
5. Permission catalog from seeded permissions only: Task 2.
6. User create/edit/disable/reset password through Console: Tasks 2, 3, 4, 5 and 6.
7. Role create/edit permissions through Console: Tasks 2, 3, 4, 5 and 7.
8. Session view/revoke through Console: Tasks 2, 3, 4, 5 and 8.
9. Gateway IAM permission checks before forwarding: Task 3.
10. Stable OpenAPI operation IDs and api-client regeneration: Tasks 3 and 4.
11. Unit, integration, frontend and E2E coverage: Tasks 2 through 9.
12. Browser verification for desktop/mobile/dialog/focus/no overlap: Task 9.

## Final Verification Checklist

Before opening a PR or merging this branch, verify:

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

Expected final state: all checks pass, generated OpenAPI/api-client files are consistent with Gateway operation IDs, and the only outstanding diffs belong to the Phase 8 implementation branch.

## Self Review

Spec coverage: every in-scope backend, Gateway, api-client, design-system, frontend page, E2E, browser verification and documentation requirement maps to at least one task above.

Red-flag scan: this plan avoids open-ended gaps and names exact files, commands, request shapes, response shapes, operation IDs and component boundaries.

Type consistency: Console IAM model names use the `ConsoleIam...` prefix across Gateway, generated api-client aliases, composables and Vue components. Backend IAM role/user/session response names remain service-local and Gateway does not reference IAM Domain or Infrastructure types.
