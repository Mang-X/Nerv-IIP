# Third Vertical Slice Console Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立第三条纵切：创建 Vue 控制台工作区、导出 Gateway OpenAPI、生成类型安全 API client，并在控制台完成实例列表、实例详情、restart 动作和 OperationTask 状态查看。

**Architecture:** PlatformGateway 继续作为前端唯一控制台 API 入口，先补齐稳定 OpenAPI 文档与 operationId，再由 `frontend/packages/api-client` 使用 Hey API 生成 fetch SDK、TypeScript types 和 Pinia Colada query/mutation options。`frontend/apps/console` 使用 Vue Router 官方文件路由插件、Pinia、Pinia Colada 和薄页面组件消费 `api-client` 的稳定导出，不在页面里手写 URL 或 DTO。

**Tech Stack:** .NET 10、FastEndpoints、FastEndpoints.Swagger、PowerShell、pnpm 10.13.1、Node.js 22.17.0、Vue 3.5.34、Vue Router 5.0.7、Vite 8.0.13、TypeScript 6.0.3、Pinia 3.0.4、Pinia Colada 1.3.0、Pinia Colada Auto Refetch 0.2.6、Hey API OpenAPI TypeScript 0.97.1、Vitest 4.1.6。

---

## Current Gate

Verification run on 2026-05-16 after adding repository-level `NuGet.config`:

```powershell
pwsh scripts/verify-second-slice-ops.ps1
```

Observed result:

```text
backend restore/build/test: exit 0
connector-hosts restore/build/test: exit 0
Second vertical slice verified with operationTaskId op-000001.
```

The project is ready for the third phase. The only pre-plan adjustment already made is `NuGet.config`, which keeps Central Package Management from failing on local machines with multiple global NuGet sources and `TreatWarningsAsErrors=true`.

## Scope

### In This Plan

1. Add Gateway OpenAPI support for console-facing endpoints.
2. Freeze console operation IDs for generated frontend clients:
   - `listConsoleInstances`
   - `getConsoleInstanceDetail`
   - `restartConsoleInstance`
   - `getConsoleOperationTask`
3. Create the `frontend` pnpm workspace skeleton.
4. Create `frontend/packages/api-client` with Hey API generation and stable hand-written exports.
5. Create `frontend/packages/ui` and `frontend/packages/app-shell` as small first-pass local packages.
6. Create `frontend/apps/console` with typed file routing, Pinia, Pinia Colada, console overview, restart action and operation detail polling.
7. Add a third-slice verification script covering backend OpenAPI export, frontend install, typecheck, tests and production build.

### Outside This Plan

1. Full IAM login and permission enforcement.
2. shadcn-vue registry initialization and long-lived visual design system migration.
3. High-risk operation approvals, notification routing and audit inbox UI.
4. PostgreSQL/RabbitMQ/Redis persistence migration.
5. Aspire AppHost and deployment package generation.

## File Structure Map

```text
backend/
  Directory.Packages.props
  gateway/PlatformGateway/
    src/Nerv.IIP.PlatformGateway.Web/
      Nerv.IIP.PlatformGateway.Web.csproj
      Program.cs
      Endpoints/Instances/InstanceEndpoints.cs
      Endpoints/Operations/OperationEndpoints.cs
    tests/Nerv.IIP.PlatformGateway.Web.Tests/
      GatewayOpenApiTests.cs

frontend/
  package.json
  pnpm-workspace.yaml
  tsconfig.base.json
  vite.config.ts
  apps/console/
    package.json
    index.html
    tsconfig.json
    vite.config.ts
    typed-router.d.ts
    src/
      main.ts
      App.vue
      router/index.ts
      layouts/DefaultLayout.vue
      pages/index.vue
      pages/operations/[operationTaskId].vue
      pages/[...path].vue
      components/console/InstanceTable.vue
      components/console/InstanceDetailPanel.vue
      components/console/OperationTimeline.vue
      composables/useConsoleOperations.ts
      assets/main.css
      test/setup.ts
      pages/index.test.ts
  packages/
    api-client/
      package.json
      tsconfig.json
      openapi-ts.config.ts
      openapi/platform-gateway.v1.json
      src/generated/
      src/transport/base-url.ts
      src/transport/client-config.ts
      src/console.ts
      src/index.ts
      src/transport/client-config.test.ts
    ui/
      package.json
      tsconfig.json
      src/UiBadge.vue
      src/UiButton.vue
      src/UiPanel.vue
      src/index.ts
    app-shell/
      package.json
      tsconfig.json
      src/AppShell.vue
      src/index.ts

scripts/
  export-gateway-openapi.ps1
  verify-third-slice-console.ps1

README.md
docs/architecture/implementation-readiness.md
```

## Boundary Rules

1. Console pages must import API calls from `@nerv-iip/api-client`; they must not handwrite `/api/...` URLs.
2. `frontend/packages/api-client/src/generated` is generated-only and must not be edited by hand.
3. `api-client` may configure transport, base URL and stable exports, but must not contain view logic.
4. Pinia stores are only for client state. Server state goes through Pinia Colada.
5. Route-level Vue pages stay thin; feature markup and interactions live in components and composables.
6. `PlatformGateway.Web` remains the only frontend-facing API boundary in this phase.
7. Gateway still does not reference AppHub or Ops Domain/Infrastructure projects.
8. `layer-base`、`layer-platform`、`auth`、`shared-types` are reserved frontend package boundaries and are not created until a real cross-page or cross-app need appears.
9. Operation polling uses Pinia Colada auto-refetch options; Vue components must not own raw polling timers.

## Architecture Inputs

Read these documents before executing the tasks:

1. `docs/architecture/api-contract-and-codegen.md` for Gateway OpenAPI, operationId, Hey API and generated-client boundaries.
2. `docs/architecture/frontend-structure.md` for pnpm workspace packages, Vue Router file-routing, typed route and Pinia Colada rules.
3. `docs/architecture/implementation-readiness.md` for NuGet restore baseline, current stage status and third-iteration acceptance boundaries.
4. `docs/adr/0006-frontend-workspace-structure.md` and `docs/adr/0007-vue-router-file-routing-colocation.md` for durable frontend decisions.

---

## Task 1: Add Gateway OpenAPI For Console APIs

**Files:**

- Modify: `backend/Directory.Packages.props`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Create: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOpenApiTests.cs`

- [ ] **Step 1: Write OpenAPI endpoint test**

Create `GatewayOpenApiTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayOpenApiTests
{
    [Fact]
    public async Task Gateway_exports_console_openapi_document_with_stable_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/console/v1/instances", out var instances));
        Assert.True(instances.GetProperty("get").TryGetProperty("operationId", out var listOperation));
        Assert.Equal("listConsoleInstances", listOperation.GetString());

        var detail = paths.GetProperty("/api/console/v1/instances/{instanceKey}");
        Assert.Equal("getConsoleInstanceDetail", detail.GetProperty("get").GetProperty("operationId").GetString());

        var restart = paths.GetProperty("/api/console/v1/instances/{instanceKey}/operations/restart");
        Assert.Equal("restartConsoleInstance", restart.GetProperty("post").GetProperty("operationId").GetString());

        var operationDetail = paths.GetProperty("/api/console/v1/operation-tasks/{operationTaskId}");
        Assert.Equal("getConsoleOperationTask", operationDetail.GetProperty("get").GetProperty("operationId").GetString());
    }
}
```

- [ ] **Step 2: Run the new test to verify it fails**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --filter Gateway_exports_console_openapi_document_with_stable_operation_ids
```

Expected: FAIL with `/swagger/v1/swagger.json` returning `404 Not Found` or missing operation IDs.

- [ ] **Step 3: Add FastEndpoints.Swagger package version**

Modify `backend/Directory.Packages.props`:

```xml
<PackageVersion Include="FastEndpoints.Swagger" Version="8.1.0" />
```

Place it beside the existing `FastEndpoints` package version.

- [ ] **Step 4: Reference FastEndpoints.Swagger from Gateway**

Modify `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj`:

```xml
<PackageReference Include="FastEndpoints.Swagger" />
```

Place it in the same `ItemGroup` as `FastEndpoints`.

- [ ] **Step 5: Configure Swagger document in Program.cs**

Replace `Program.cs` with:

```csharp
using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.Caching;
using Nerv.IIP.Observability;
using Nerv.IIP.PlatformGateway.Web;
using Nerv.IIP.PlatformGateway.Web.Application.OpsClient;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(options =>
    {
        options.DocumentSettings = settings =>
        {
            settings.DocumentName = "v1";
            settings.Title = "Nerv-IIP PlatformGateway Console API";
            settings.Version = "v1";
        };
    });
builder.Services.AddNervIipCaching(builder.Configuration, "platform-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "platform-gateway");
builder.Services.AddHttpClient<IAppHubClient, HttpAppHubClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5103");
});
builder.Services.AddHttpClient<IGatewayOpsClient, GatewayOpsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5105");
});

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseFastEndpoints(options =>
{
    options.Endpoints.NameGenerator = context => context.EndpointType.Name switch
    {
        "ListInstancesEndpoint" => "listConsoleInstances",
        "GetInstanceDetailEndpoint" => "getConsoleInstanceDetail",
        "RestartInstanceEndpoint" => "restartConsoleInstance",
        "GetConsoleOperationTaskEndpoint" => "getConsoleOperationTask",
        var name when name.EndsWith("Endpoint", StringComparison.Ordinal) => name[..^"Endpoint".Length],
        var name => name
    };
})
    .UseSwaggerGen();
app.Run();

public partial class Program;
```

- [ ] **Step 6: Preserve Gateway endpoint style**

Keep the existing console endpoint classes in attribute-routing style:

1. `ListInstancesEndpoint` and `GetInstanceDetailEndpoint` keep `[HttpGet]` plus `[AllowAnonymous]`.
2. `RestartInstanceEndpoint` keeps `[HttpPost]` plus `[AllowAnonymous]`.
3. `GetConsoleOperationTaskEndpoint` keeps `[HttpGet]` plus `[AllowAnonymous]`.
4. `GatewayEndpointResults`, `RestartInstanceRequest` and `GatewayOpsEndpointResults` stay unchanged.

Do not convert these Endpoint classes solely to set OpenAPI `operationId`. In this project, route/auth declarations stay close to the Endpoint class, while stable generated-client names for Gateway console APIs are owned by the `NameGenerator` mapping in `Program.cs`. If a future Endpoint needs advanced FastEndpoints metadata that attributes cannot express, switch that Endpoint fully to `Configure()` and remove the route/auth attributes from the class.

- [ ] **Step 7: Run Gateway tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj
```

Expected: exit code `0`; existing instance/operation tests and the new OpenAPI test all pass.

- [ ] **Step 8: Commit**

Run:

```powershell
git add backend/Directory.Packages.props backend/gateway/PlatformGateway
git commit -m "feat: expose gateway console openapi"
```

## Task 2: Add Frontend Workspace Skeleton

**Files:**

- Modify: `.gitignore`
- Create: `frontend/package.json`
- Create: `frontend/pnpm-workspace.yaml`
- Create: `frontend/tsconfig.base.json`
- Create: `frontend/vite.config.ts`

- [ ] **Step 1: Update root ignore rules**

Append to `.gitignore`:

```gitignore
node_modules/
frontend/**/node_modules/
frontend/**/dist/
frontend/**/coverage/
frontend/.vite/
```

- [ ] **Step 2: Create pnpm workspace manifest**

Create `frontend/package.json`:

```json
{
  "name": "@nerv-iip/frontend-workspace",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "packageManager": "pnpm@10.13.1",
  "scripts": {
    "generate:api": "pnpm --filter @nerv-iip/api-client generate",
    "test": "vitest run",
    "typecheck": "pnpm -r --if-present typecheck",
    "build": "pnpm --filter @nerv-iip/console build"
  },
  "devDependencies": {
    "@types/node": "25.8.0",
    "@vitejs/plugin-vue": "6.0.7",
    "@vue/test-utils": "2.4.10",
    "jsdom": "29.1.1",
    "typescript": "6.0.3",
    "vite": "8.0.13",
    "vitest": "4.1.6",
    "vue-tsc": "3.2.9"
  }
}
```

- [ ] **Step 3: Create workspace package map**

Create `frontend/pnpm-workspace.yaml`:

```yaml
packages:
  - apps/*
  - packages/*
```

- [ ] **Step 4: Create shared TypeScript baseline**

Create `frontend/tsconfig.base.json`:

```json
{
  "compilerOptions": {
    "target": "ES2024",
    "useDefineForClassFields": true,
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "strict": true,
    "jsx": "preserve",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "lib": ["ES2024", "DOM", "DOM.Iterable"],
    "types": ["node", "vite/client", "vitest/globals"]
  }
}
```

- [ ] **Step 5: Create root Vite config for shared Vitest defaults**

Create `frontend/vite.config.ts`:

```ts
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    globals: true,
    environment: 'jsdom',
  },
})
```

- [ ] **Step 6: Install workspace dependencies**

Run:

```powershell
corepack enable
pnpm -C frontend install
```

Expected: exit code `0`; `frontend/pnpm-lock.yaml` is created.

- [ ] **Step 7: Commit**

Run:

```powershell
git add .gitignore frontend/package.json frontend/pnpm-workspace.yaml frontend/tsconfig.base.json frontend/vite.config.ts frontend/pnpm-lock.yaml
git commit -m "chore: bootstrap frontend workspace"
```

## Task 3: Add API Client Generation Package

**Files:**

- Create: `scripts/export-gateway-openapi.ps1`
- Create: `frontend/packages/api-client/package.json`
- Create: `frontend/packages/api-client/tsconfig.json`
- Create: `frontend/packages/api-client/openapi-ts.config.ts`
- Create: `frontend/packages/api-client/openapi/platform-gateway.v1.json`
- Create: `frontend/packages/api-client/src/generated/*`
- Create: `frontend/packages/api-client/src/transport/base-url.ts`
- Create: `frontend/packages/api-client/src/transport/client-config.ts`
- Create: `frontend/packages/api-client/src/transport/client-config.test.ts`
- Create: `frontend/packages/api-client/src/console.ts`
- Create: `frontend/packages/api-client/src/index.ts`

- [ ] **Step 1: Create OpenAPI export script**

Create `scripts/export-gateway-openapi.ps1`:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-Healthy {
  param([string]$Uri)
  $deadline = (Get-Date).AddSeconds(30)
  do {
    try {
      $result = Invoke-RestMethod -Method Get -Uri $Uri
      if ($result -eq "Healthy") { return }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
  } while ((Get-Date) -lt $deadline)
  throw "Service did not become healthy at $Uri"
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$gatewayUrl = "http://127.0.0.1:58204"
$gatewayProject = Join-Path $root "backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj"
$output = Join-Path $root "frontend/packages/api-client/openapi/platform-gateway.v1.json"
$outputDirectory = Split-Path -Parent $output

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$gatewayJob = $null
try {
  dotnet build $gatewayProject

  $gatewayJob = Start-Job -ScriptBlock {
    param($project, $url)
    $env:ASPNETCORE_URLS = $url
    dotnet run --project $project --no-build --no-launch-profile
  } -ArgumentList $gatewayProject, $gatewayUrl

  Wait-Healthy "$gatewayUrl/health"
  Invoke-WebRequest -Method Get -Uri "$gatewayUrl/swagger/v1/swagger.json" -OutFile $output
  Write-Host "Gateway OpenAPI exported to $output"
}
finally {
  if ($gatewayJob) {
    Stop-Job $gatewayJob -ErrorAction SilentlyContinue
    Remove-Job $gatewayJob -Force -ErrorAction SilentlyContinue
  }
}
```

- [ ] **Step 2: Create api-client package manifest**

Create `frontend/packages/api-client/package.json`:

```json
{
  "name": "@nerv-iip/api-client",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": "./src/index.ts"
  },
  "scripts": {
    "generate": "openapi-ts",
    "test": "vitest run src",
    "typecheck": "vue-tsc --noEmit -p tsconfig.json"
  },
  "dependencies": {
    "@hey-api/client-fetch": "0.13.1",
    "@pinia/colada": "1.3.0"
  },
  "devDependencies": {
    "@hey-api/openapi-ts": "0.97.1"
  }
}
```

- [ ] **Step 3: Create api-client TypeScript config**

Create `frontend/packages/api-client/tsconfig.json`:

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["openapi-ts.config.ts", "src/**/*.ts"],
  "compilerOptions": {
    "composite": false
  }
}
```

- [ ] **Step 4: Configure Hey API generation**

Create `frontend/packages/api-client/openapi-ts.config.ts`:

```ts
import { defineConfig } from '@hey-api/openapi-ts'

export default defineConfig({
  input: './openapi/platform-gateway.v1.json',
  output: {
    path: './src/generated',
  },
  plugins: [
    '@hey-api/client-fetch',
    '@hey-api/typescript',
    '@hey-api/sdk',
    {
      name: '@pinia/colada',
      includeInEntry: true,
      queryKeys: {
        tags: true,
      },
      queryOptions: {
        name: '{{name}}QueryOptions',
      },
      mutationOptions: {
        name: '{{name}}MutationOptions',
      },
    },
  ],
})
```

- [ ] **Step 5: Export Gateway OpenAPI JSON**

Run:

```powershell
pwsh scripts/export-gateway-openapi.ps1
```

Expected: exit code `0`; `frontend/packages/api-client/openapi/platform-gateway.v1.json` exists and contains the four console operation IDs.

- [ ] **Step 6: Generate API client**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/api-client generate
```

Expected: exit code `0`; generated files include `src/generated/client.gen.ts`, `src/generated/sdk.gen.ts`, `src/generated/types.gen.ts` and `src/generated/@pinia/colada.gen.ts`.

- [ ] **Step 7: Add transport base URL helpers**

Create `frontend/packages/api-client/src/transport/base-url.ts`:

```ts
const defaultBrowserBaseUrl = ''
const defaultServerBaseUrl = 'http://localhost:5100'

export function getApiBaseUrl(env: ImportMetaEnv = import.meta.env): string {
  const configured = env.VITE_NERV_IIP_API_BASE_URL
  if (configured && configured.trim().length > 0) {
    return configured
  }

  if (typeof window === 'undefined') {
    return defaultServerBaseUrl
  }

  return defaultBrowserBaseUrl
}
```

Create `frontend/packages/api-client/src/transport/client-config.ts`:

```ts
import { client } from '../generated/client.gen'
import { getApiBaseUrl } from './base-url'

export interface ConfigureApiClientOptions {
  baseUrl?: string
  headers?: HeadersInit
}

export function configureApiClient(options: ConfigureApiClientOptions = {}): void {
  client.setConfig({
    baseUrl: options.baseUrl ?? getApiBaseUrl(),
    headers: options.headers,
  })
}
```

- [ ] **Step 8: Add transport test**

Create `frontend/packages/api-client/src/transport/client-config.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { getApiBaseUrl } from './base-url'

describe('getApiBaseUrl', () => {
  it('uses explicit Vite environment value first', () => {
    expect(getApiBaseUrl({ VITE_NERV_IIP_API_BASE_URL: 'http://127.0.0.1:58204' } as ImportMetaEnv)).toBe('http://127.0.0.1:58204')
  })

  it('uses browser-relative API base URL when no explicit value is configured', () => {
    expect(getApiBaseUrl({} as ImportMetaEnv)).toBe('')
  })
})
```

- [ ] **Step 9: Add stable api-client exports**

Create `frontend/packages/api-client/src/console.ts`:

```ts
export {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
} from './generated/@pinia/colada.gen'

export type {
  InstanceDetailResponse,
  InstanceListItem,
  InstanceListResponse,
  OperationTaskResponse,
  RestartInstanceRequest,
} from './generated/types.gen'
```

Create `frontend/packages/api-client/src/index.ts`:

```ts
export { configureApiClient } from './transport/client-config'
export type { ConfigureApiClientOptions } from './transport/client-config'
export * from './console'
```

- [ ] **Step 10: Run api-client tests and typecheck**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

Expected: both commands exit with code `0`.

- [ ] **Step 11: Commit**

Run:

```powershell
git add scripts/export-gateway-openapi.ps1 frontend/packages/api-client frontend/pnpm-lock.yaml
git commit -m "feat: generate gateway api client"
```

## Task 4: Add UI And App Shell Packages

**Files:**

- Create: `frontend/packages/ui/package.json`
- Create: `frontend/packages/ui/tsconfig.json`
- Create: `frontend/packages/ui/src/UiBadge.vue`
- Create: `frontend/packages/ui/src/UiButton.vue`
- Create: `frontend/packages/ui/src/UiPanel.vue`
- Create: `frontend/packages/ui/src/index.ts`
- Create: `frontend/packages/app-shell/package.json`
- Create: `frontend/packages/app-shell/tsconfig.json`
- Create: `frontend/packages/app-shell/src/AppShell.vue`
- Create: `frontend/packages/app-shell/src/index.ts`

- [ ] **Step 1: Create UI package manifest and tsconfig**

Create `frontend/packages/ui/package.json`:

```json
{
  "name": "@nerv-iip/ui",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": "./src/index.ts"
  },
  "scripts": {
    "typecheck": "vue-tsc --noEmit -p tsconfig.json"
  },
  "dependencies": {
    "vue": "3.5.34"
  }
}
```

Create `frontend/packages/ui/tsconfig.json`:

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["src/**/*.ts", "src/**/*.vue"],
  "compilerOptions": {
    "composite": false
  }
}
```

- [ ] **Step 2: Add focused UI primitives**

Create `frontend/packages/ui/src/UiBadge.vue`:

```vue
<script setup lang="ts">
interface Props {
  tone?: 'neutral' | 'success' | 'warning' | 'danger'
}

withDefaults(defineProps<Props>(), {
  tone: 'neutral',
})
</script>

<template>
  <span class="ui-badge" :data-tone="tone">
    <slot />
  </span>
</template>

<style scoped>
.ui-badge {
  display: inline-flex;
  align-items: center;
  min-height: 1.5rem;
  padding: 0 0.625rem;
  border: 1px solid var(--color-border);
  border-radius: 999px;
  color: var(--color-text-muted);
  background: var(--color-surface);
  font-size: 0.75rem;
  font-weight: 650;
}

.ui-badge[data-tone='success'] {
  color: var(--color-success);
  border-color: color-mix(in srgb, var(--color-success) 36%, transparent);
}

.ui-badge[data-tone='warning'] {
  color: var(--color-warning);
  border-color: color-mix(in srgb, var(--color-warning) 36%, transparent);
}

.ui-badge[data-tone='danger'] {
  color: var(--color-danger);
  border-color: color-mix(in srgb, var(--color-danger) 36%, transparent);
}
</style>
```

Create `frontend/packages/ui/src/UiButton.vue`:

```vue
<script setup lang="ts">
interface Props {
  variant?: 'primary' | 'secondary' | 'danger'
  disabled?: boolean
  type?: 'button' | 'submit' | 'reset'
}

withDefaults(defineProps<Props>(), {
  variant: 'primary',
  disabled: false,
  type: 'button',
})
</script>

<template>
  <button class="ui-button" :data-variant="variant" :disabled="disabled" :type="type">
    <slot />
  </button>
</template>

<style scoped>
.ui-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 2.25rem;
  padding: 0 0.875rem;
  border: 1px solid transparent;
  border-radius: 0.5rem;
  font: inherit;
  font-size: 0.875rem;
  font-weight: 650;
  cursor: pointer;
  transition: background 140ms ease, border-color 140ms ease, color 140ms ease, opacity 140ms ease;
}

.ui-button:disabled {
  cursor: not-allowed;
  opacity: 0.58;
}

.ui-button[data-variant='primary'] {
  color: var(--color-on-accent);
  background: var(--color-accent);
}

.ui-button[data-variant='secondary'] {
  color: var(--color-text);
  background: var(--color-surface);
  border-color: var(--color-border);
}

.ui-button[data-variant='danger'] {
  color: var(--color-on-danger);
  background: var(--color-danger);
}
</style>
```

Create `frontend/packages/ui/src/UiPanel.vue`:

```vue
<template>
  <section class="ui-panel">
    <slot />
  </section>
</template>

<style scoped>
.ui-panel {
  border: 1px solid var(--color-border);
  border-radius: 0.5rem;
  background: var(--color-surface);
}
</style>
```

Create `frontend/packages/ui/src/index.ts`:

```ts
export { default as UiBadge } from './UiBadge.vue'
export { default as UiButton } from './UiButton.vue'
export { default as UiPanel } from './UiPanel.vue'
```

- [ ] **Step 3: Create app shell package**

Create `frontend/packages/app-shell/package.json`:

```json
{
  "name": "@nerv-iip/app-shell",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": "./src/index.ts"
  },
  "scripts": {
    "typecheck": "vue-tsc --noEmit -p tsconfig.json"
  },
  "dependencies": {
    "vue": "3.5.34"
  }
}
```

Create `frontend/packages/app-shell/tsconfig.json`:

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["src/**/*.ts", "src/**/*.vue"],
  "compilerOptions": {
    "composite": false
  }
}
```

Create `frontend/packages/app-shell/src/AppShell.vue`:

```vue
<script setup lang="ts">
interface NavItem {
  label: string
  href: string
}

defineProps<{
  title: string
  navItems: NavItem[]
}>()
</script>

<template>
  <div class="app-shell">
    <aside class="app-shell-sidebar">
      <div class="app-shell-brand">{{ title }}</div>
      <nav class="app-shell-nav" aria-label="Primary navigation">
        <a v-for="item in navItems" :key="item.href" class="app-shell-nav-link" :href="item.href">
          {{ item.label }}
        </a>
      </nav>
    </aside>
    <main class="app-shell-main">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.app-shell {
  display: grid;
  min-height: 100vh;
  grid-template-columns: 16rem minmax(0, 1fr);
  background: var(--color-page);
}

.app-shell-sidebar {
  border-right: 1px solid var(--color-border);
  background: var(--color-surface);
  padding: 1rem;
}

.app-shell-brand {
  margin-bottom: 1.5rem;
  color: var(--color-text);
  font-size: 1rem;
  font-weight: 750;
}

.app-shell-nav {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.app-shell-nav-link {
  border-radius: 0.5rem;
  color: var(--color-text-muted);
  padding: 0.625rem 0.75rem;
  text-decoration: none;
}

.app-shell-nav-link:hover {
  color: var(--color-text);
  background: var(--color-page);
}

.app-shell-main {
  min-width: 0;
  padding: 1.25rem;
}

@media (max-width: 820px) {
  .app-shell {
    grid-template-columns: 1fr;
  }

  .app-shell-sidebar {
    border-right: 0;
    border-bottom: 1px solid var(--color-border);
  }
}
</style>
```

Create `frontend/packages/app-shell/src/index.ts`:

```ts
export { default as AppShell } from './AppShell.vue'
```

- [ ] **Step 4: Typecheck shared packages**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/ui typecheck
pnpm -C frontend --filter @nerv-iip/app-shell typecheck
```

Expected: both commands exit with code `0`.

- [ ] **Step 5: Commit**

Run:

```powershell
git add frontend/packages/ui frontend/packages/app-shell frontend/pnpm-lock.yaml
git commit -m "feat: add frontend shell packages"
```

## Task 5: Add Console App Runtime And Routing

**Files:**

- Create: `frontend/apps/console/package.json`
- Create: `frontend/apps/console/index.html`
- Create: `frontend/apps/console/tsconfig.json`
- Create: `frontend/apps/console/vite.config.ts`
- Generate: `frontend/apps/console/typed-router.d.ts`
- Create: `frontend/apps/console/src/main.ts`
- Create: `frontend/apps/console/src/App.vue`
- Create: `frontend/apps/console/src/router/index.ts`
- Create: `frontend/apps/console/src/layouts/DefaultLayout.vue`
- Create: `frontend/apps/console/src/pages/[...path].vue`
- Create: `frontend/apps/console/src/assets/main.css`
- Create: `frontend/apps/console/src/test/setup.ts`

- [ ] **Step 1: Create console package manifest**

Create `frontend/apps/console/package.json`:

```json
{
  "name": "@nerv-iip/console",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite --host 127.0.0.1 --port 5173",
    "build": "vue-tsc --noEmit -p tsconfig.json && vite build",
    "test": "vitest run src",
    "typecheck": "vue-tsc --noEmit -p tsconfig.json"
  },
  "dependencies": {
    "@nerv-iip/api-client": "workspace:*",
    "@nerv-iip/app-shell": "workspace:*",
    "@nerv-iip/ui": "workspace:*",
    "@pinia/colada": "1.3.0",
    "@pinia/colada-plugin-auto-refetch": "0.2.6",
    "lucide-vue-next": "1.0.0",
    "pinia": "3.0.4",
    "vue": "3.5.34",
    "vue-router": "5.0.7"
  }
}
```

- [ ] **Step 2: Create app shell files**

Create `frontend/apps/console/index.html`:

```html
<!doctype html>
<html lang="zh-CN">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Nerv-IIP Console</title>
  </head>
  <body>
    <div id="app"></div>
    <script type="module" src="/src/main.ts"></script>
  </body>
</html>
```

Create `frontend/apps/console/src/assets/main.css`:

```css
:root {
  color-scheme: light;
  --color-page: #f7f8fa;
  --color-surface: #ffffff;
  --color-border: #d8dde6;
  --color-text: #172033;
  --color-text-muted: #62708a;
  --color-accent: #256f67;
  --color-on-accent: #ffffff;
  --color-danger: #b8323a;
  --color-on-danger: #ffffff;
  --color-success: #15724b;
  --color-warning: #9a6200;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--color-page);
  color: var(--color-text);
  font-family: ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
}

button,
input {
  font: inherit;
}
```

Create `frontend/apps/console/src/App.vue`:

```vue
<template>
  <RouterView />
</template>
```

- [ ] **Step 3: Create Vite and TypeScript configuration**

Create `frontend/apps/console/tsconfig.json`:

```json
{
  "extends": "../../tsconfig.base.json",
  "include": ["src/**/*.ts", "src/**/*.vue", "typed-router.d.ts"],
  "compilerOptions": {
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"],
      "@nerv-iip/api-client": ["../../packages/api-client/src/index.ts"],
      "@nerv-iip/app-shell": ["../../packages/app-shell/src/index.ts"],
      "@nerv-iip/ui": ["../../packages/ui/src/index.ts"]
    }
  },
  "vueCompilerOptions": {
    "plugins": ["vue-router/volar/sfc-route-blocks", "vue-router/volar/sfc-typed-router"]
  }
}
```

Create `frontend/apps/console/vite.config.ts`:

```ts
import { fileURLToPath, URL } from 'node:url'
import Vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vitest/config'
import VueRouter from 'vue-router/vite'

export default defineConfig({
  plugins: [
    VueRouter({
      routesFolder: [
        {
          src: 'src/pages',
          exclude: excluded => excluded.concat([
            '**/components/**/*',
            '**/dialogs/**/*',
            '**/drawers/**/*',
            '**/fragments/**/*',
          ]),
        },
      ],
      dts: 'typed-router.d.ts',
    }),
    Vue(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      '@nerv-iip/api-client': fileURLToPath(new URL('../../packages/api-client/src/index.ts', import.meta.url)),
      '@nerv-iip/app-shell': fileURLToPath(new URL('../../packages/app-shell/src/index.ts', import.meta.url)),
      '@nerv-iip/ui': fileURLToPath(new URL('../../packages/ui/src/index.ts', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.NERV_IIP_GATEWAY_URL ?? 'http://127.0.0.1:58204',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
})
```

- [ ] **Step 4: Create router and plugin bootstrap**

Create `frontend/apps/console/src/router/index.ts`:

```ts
import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

if (import.meta.hot) {
  handleHotUpdate(router)
}
```

Create `frontend/apps/console/src/main.ts`:

```ts
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import { configureApiClient } from '@nerv-iip/api-client'
import App from './App.vue'
import { router } from './router'
import './assets/main.css'

configureApiClient()

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
app.use(PiniaColada, {
  queryOptions: {
    gcTime: 300_000,
  },
  plugins: [
    PiniaColadaAutoRefetch({
      autoRefetch: false,
    }),
  ],
})
app.use(router)
app.mount('#app')
```

Create `frontend/apps/console/src/test/setup.ts`:

```ts
import { afterEach } from 'vitest'
import { enableAutoUnmount } from '@vue/test-utils'

enableAutoUnmount(afterEach)
```

- [ ] **Step 5: Create default layout and not found page**

Create `frontend/apps/console/src/layouts/DefaultLayout.vue`:

```vue
<script setup lang="ts">
import { AppShell } from '@nerv-iip/app-shell'

const navItems = [
  { label: '实例', href: '/' },
]
</script>

<template>
  <AppShell title="Nerv-IIP" :nav-items="navItems">
    <slot />
  </AppShell>
</template>
```

Create `frontend/apps/console/src/pages/[...path].vue`:

```vue
<script setup lang="ts">
import DefaultLayout from '@/layouts/DefaultLayout.vue'
</script>

<template>
  <DefaultLayout>
    <section class="not-found">
      <h1 class="not-found-title">页面不存在</h1>
      <p class="not-found-copy">请返回实例控制台继续操作。</p>
    </section>
  </DefaultLayout>
</template>

<style scoped>
.not-found {
  max-width: 40rem;
}

.not-found-title {
  margin: 0 0 0.5rem;
  font-size: 1.5rem;
}

.not-found-copy {
  margin: 0;
  color: var(--color-text-muted);
}
</style>
```

- [ ] **Step 6: Install and typecheck console shell**

Run:

```powershell
pnpm -C frontend install
pnpm -C frontend --filter @nerv-iip/console typecheck
```

Expected: exit code `0`; `frontend/apps/console/typed-router.d.ts` is generated and included in git.

- [ ] **Step 7: Commit**

Run:

```powershell
git add frontend/apps/console frontend/pnpm-lock.yaml
git commit -m "feat: add console app shell"
```

## Task 6: Build Console Instance And Operation Experience

**Files:**

- Create: `frontend/apps/console/src/composables/useConsoleOperations.ts`
- Create: `frontend/apps/console/src/components/console/InstanceTable.vue`
- Create: `frontend/apps/console/src/components/console/InstanceDetailPanel.vue`
- Create: `frontend/apps/console/src/components/console/OperationTimeline.vue`
- Create: `frontend/apps/console/src/pages/index.vue`
- Create: `frontend/apps/console/src/pages/operations/[operationTaskId].vue`
- Create: `frontend/apps/console/src/pages/index.test.ts`

- [ ] **Step 1: Write console page test**

Create `frontend/apps/console/src/pages/index.test.ts`:

```ts
import { PiniaColada } from '@pinia/colada'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'
import ConsoleIndexPage from './index.vue'

vi.mock('@nerv-iip/api-client', async () => {
  const actual = await vi.importActual<Record<string, unknown>>('@nerv-iip/api-client')
  return {
    ...actual,
    listConsoleInstancesQueryOptions: () => ({
      key: [{ _id: 'listConsoleInstances' }],
      query: async () => ({
        pageNumber: 1,
        pageSize: 20,
        totalCount: 1,
        items: [
          {
            applicationKey: 'demo-api',
            applicationName: 'Demo API',
            version: '1.0.0',
            nodeKey: 'node-001',
            nodeName: 'local-docker',
            instanceKey: 'docker-container-local-demo-001',
            instanceName: 'demo-api',
            reportedStatus: 'running',
            healthStatus: 'healthy',
            lastHeartbeatAtUtc: '2026-05-16T00:00:00Z',
            lastStateObservedAtUtc: '2026-05-16T00:00:05Z',
          },
        ],
      }),
    }),
    getConsoleInstanceDetailQueryOptions: () => ({
      key: [{ _id: 'getConsoleInstanceDetail' }],
      query: async () => ({
        applicationKey: 'demo-api',
        applicationName: 'Demo API',
        version: '1.0.0',
        nodeKey: 'node-001',
        nodeName: 'local-docker',
        instanceKey: 'docker-container-local-demo-001',
        instanceName: 'demo-api',
        reportedStatus: 'running',
        healthStatus: 'healthy',
        lastHeartbeatAtUtc: '2026-05-16T00:00:00Z',
        lastStateObservedAtUtc: '2026-05-16T00:00:05Z',
        capabilities: [{ capabilityCode: 'lifecycle.restart', capabilityVersion: '1.0', category: 'lifecycle', supportedOperations: ['restart'] }],
        metadata: { containerId: 'local-demo-001' },
      }),
    }),
    restartConsoleInstanceMutationOptions: () => ({
      mutation: async () => ({
        operationTaskId: 'op-000001',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        instanceKey: 'docker-container-local-demo-001',
        operationCode: 'lifecycle.restart',
        status: 'queued',
        requestedBy: 'local-admin',
        requestedAtUtc: '2026-05-16T00:00:10Z',
        currentAttemptId: null,
        attempts: [],
        auditRecords: [],
      }),
    }),
  }
})

describe('ConsoleIndexPage', () => {
  it('renders instance facts and exposes restart action', async () => {
    const wrapper = mount(ConsoleIndexPage, {
      global: {
        plugins: [createPinia(), PiniaColada],
        stubs: {
          RouterLink: {
            template: '<a><slot /></a>',
          },
        },
      },
    })

    await vi.waitFor(() => {
      expect(wrapper.text()).toContain('Demo API')
      expect(wrapper.text()).toContain('running')
      expect(wrapper.text()).toContain('Restart')
    })
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- --run src/pages/index.test.ts
```

Expected: FAIL because the console page and components do not exist yet.

- [ ] **Step 3: Add console operations composable**

Create `frontend/apps/console/src/composables/useConsoleOperations.ts`:

```ts
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, shallowRef } from 'vue'
import {
  getConsoleInstanceDetailQueryOptions,
  getConsoleOperationTaskQueryOptions,
  listConsoleInstancesQueryOptions,
  restartConsoleInstanceMutationOptions,
} from '@nerv-iip/api-client'
import type { InstanceListItem, OperationTaskResponse } from '@nerv-iip/api-client'

const organizationId = 'org-001'
const environmentId = 'env-dev'

export function useConsoleInstances() {
  const selectedInstanceKey = shallowRef<string | undefined>()
  const search = shallowRef('')

  const instancesQuery = useQuery(() =>
    listConsoleInstancesQueryOptions({
      query: {
        organizationId,
        environmentId,
        pageNumber: 1,
        pageSize: 20,
        search: search.value || undefined,
      },
    }),
  )

  const instances = computed<InstanceListItem[]>(() => instancesQuery.data.value?.items ?? [])

  const effectiveInstanceKey = computed(() => selectedInstanceKey.value ?? instances.value[0]?.instanceKey)

  const selectedInstanceQuery = useQuery(() => ({
    ...getConsoleInstanceDetailQueryOptions({
      path: {
        instanceKey: effectiveInstanceKey.value ?? '',
      },
      query: {
        organizationId,
        environmentId,
      },
    }),
    enabled: Boolean(effectiveInstanceKey.value),
  }))

  return {
    environmentId,
    instances,
    instancesQuery,
    organizationId,
    search,
    selectedInstanceKey,
    selectedInstanceQuery,
  }
}

export function useRestartOperation() {
  const queryCache = useQueryCache()
  const restartMutation = useMutation(restartConsoleInstanceMutationOptions())

  async function restart(instanceKey: string): Promise<OperationTaskResponse> {
    const response = await restartMutation.mutateAsync({
      path: {
        instanceKey,
      },
      body: {
        organizationId,
        environmentId,
        reason: 'console restart',
        idempotencyKey: `console-restart-${instanceKey}-${Date.now()}`,
      },
    })

    await queryCache.invalidateQueries({
      key: [{ _id: 'listConsoleInstances' }],
      exact: false,
    })

    return response
  }

  return {
    isPending: restartMutation.isPending,
    restart,
  }
}

export function useOperationTask(operationTaskId: string) {
  return useQuery(() => ({
    ...getConsoleOperationTaskQueryOptions({
      path: {
        operationTaskId,
      },
    }),
    staleTime: 1_000,
    autoRefetch: 1_000,
  }))
}
```

- [ ] **Step 4: Add instance table component**

Create `frontend/apps/console/src/components/console/InstanceTable.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { UiBadge, UiButton } from '@nerv-iip/ui'
import type { InstanceListItem } from '@nerv-iip/api-client'

const props = defineProps<{
  instances: InstanceListItem[]
  selectedInstanceKey?: string
  restartPending: boolean
}>()

const emit = defineEmits<{
  selectInstance: [instanceKey: string]
  restartInstance: [instanceKey: string]
}>()

const hasInstances = computed(() => props.instances.length > 0)

function healthTone(value: string): 'neutral' | 'success' | 'warning' | 'danger' {
  if (value === 'healthy') {
    return 'success'
  }
  if (value === 'degraded') {
    return 'warning'
  }
  if (value === 'unhealthy') {
    return 'danger'
  }
  return 'neutral'
}
</script>

<template>
  <div class="instance-table">
    <table v-if="hasInstances" class="instance-table-grid">
      <thead>
        <tr>
          <th>应用</th>
          <th>实例</th>
          <th>节点</th>
          <th>状态</th>
          <th>健康</th>
          <th>动作</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="instance in instances"
          :key="instance.instanceKey"
          :data-selected="instance.instanceKey === selectedInstanceKey"
          @click="emit('selectInstance', instance.instanceKey)"
        >
          <td>{{ instance.applicationName }}</td>
          <td>{{ instance.instanceName }}</td>
          <td>{{ instance.nodeName }}</td>
          <td><UiBadge>{{ instance.reportedStatus }}</UiBadge></td>
          <td><UiBadge :tone="healthTone(instance.healthStatus)">{{ instance.healthStatus }}</UiBadge></td>
          <td>
            <UiButton variant="secondary" :disabled="restartPending" @click.stop="emit('restartInstance', instance.instanceKey)">
              Restart
            </UiButton>
          </td>
        </tr>
      </tbody>
    </table>
    <p v-else class="instance-table-empty">当前环境没有已纳管实例。</p>
  </div>
</template>

<style scoped>
.instance-table {
  overflow-x: auto;
}

.instance-table-grid {
  width: 100%;
  border-collapse: collapse;
}

.instance-table-grid th,
.instance-table-grid td {
  border-bottom: 1px solid var(--color-border);
  padding: 0.75rem;
  text-align: left;
  white-space: nowrap;
}

.instance-table-grid th {
  color: var(--color-text-muted);
  font-size: 0.75rem;
  font-weight: 750;
}

.instance-table-grid tr[data-selected='true'] td {
  background: color-mix(in srgb, var(--color-accent) 8%, transparent);
}

.instance-table-empty {
  margin: 0;
  color: var(--color-text-muted);
}
</style>
```

- [ ] **Step 5: Add instance detail and operation timeline components**

Create `frontend/apps/console/src/components/console/InstanceDetailPanel.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { UiBadge, UiPanel } from '@nerv-iip/ui'
import type { InstanceDetailResponse } from '@nerv-iip/api-client'

const props = defineProps<{
  instance?: InstanceDetailResponse
}>()

const capabilities = computed(() => props.instance?.capabilities ?? [])
</script>

<template>
  <UiPanel class="instance-detail-panel">
    <h2 class="instance-detail-title">实例详情</h2>
    <p v-if="!instance" class="instance-detail-empty">选择一个实例查看状态、能力和元数据。</p>
    <dl v-else class="instance-detail-list">
      <div>
        <dt>实例</dt>
        <dd>{{ instance.instanceName }}</dd>
      </div>
      <div>
        <dt>状态</dt>
        <dd>{{ instance.reportedStatus }}</dd>
      </div>
      <div>
        <dt>健康</dt>
        <dd>{{ instance.healthStatus }}</dd>
      </div>
      <div>
        <dt>能力</dt>
        <dd class="instance-detail-capabilities">
          <UiBadge v-for="capability in capabilities" :key="capability.capabilityCode">
            {{ capability.capabilityCode }}
          </UiBadge>
        </dd>
      </div>
    </dl>
  </UiPanel>
</template>

<style scoped>
.instance-detail-panel {
  padding: 1rem;
}

.instance-detail-title {
  margin: 0 0 1rem;
  font-size: 1rem;
}

.instance-detail-empty {
  margin: 0;
  color: var(--color-text-muted);
}

.instance-detail-list {
  display: grid;
  gap: 0.875rem;
  margin: 0;
}

.instance-detail-list div {
  display: grid;
  gap: 0.25rem;
}

.instance-detail-list dt {
  color: var(--color-text-muted);
  font-size: 0.75rem;
  font-weight: 700;
}

.instance-detail-list dd {
  margin: 0;
}

.instance-detail-capabilities {
  display: flex;
  flex-wrap: wrap;
  gap: 0.375rem;
}
</style>
```

Create `frontend/apps/console/src/components/console/OperationTimeline.vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { UiBadge, UiPanel } from '@nerv-iip/ui'
import type { OperationTaskResponse } from '@nerv-iip/api-client'

const props = defineProps<{
  operation?: OperationTaskResponse
}>()

const auditRecords = computed(() => props.operation?.auditRecords ?? [])
</script>

<template>
  <UiPanel class="operation-timeline">
    <h2 class="operation-timeline-title">任务状态</h2>
    <p v-if="!operation" class="operation-timeline-empty">正在加载任务事实。</p>
    <template v-else>
      <div class="operation-timeline-summary">
        <UiBadge>{{ operation.status }}</UiBadge>
        <span>{{ operation.operationCode }}</span>
      </div>
      <ol class="operation-timeline-list">
        <li v-for="record in auditRecords" :key="record.auditRecordId">
          <span class="operation-timeline-action">{{ record.action }}</span>
          <span class="operation-timeline-time">{{ record.occurredAtUtc }}</span>
        </li>
      </ol>
    </template>
  </UiPanel>
</template>

<style scoped>
.operation-timeline {
  padding: 1rem;
}

.operation-timeline-title {
  margin: 0 0 1rem;
  font-size: 1rem;
}

.operation-timeline-empty {
  margin: 0;
  color: var(--color-text-muted);
}

.operation-timeline-summary {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.operation-timeline-list {
  display: grid;
  gap: 0.625rem;
  margin: 0;
  padding-left: 1.25rem;
}

.operation-timeline-action {
  display: block;
  font-weight: 650;
}

.operation-timeline-time {
  color: var(--color-text-muted);
  font-size: 0.8125rem;
}
</style>
```

- [ ] **Step 6: Add console index page**

Create `frontend/apps/console/src/pages/index.vue`:

```vue
<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import InstanceDetailPanel from '@/components/console/InstanceDetailPanel.vue'
import InstanceTable from '@/components/console/InstanceTable.vue'
import { useConsoleInstances, useRestartOperation } from '@/composables/useConsoleOperations'

const {
  environmentId,
  instances,
  instancesQuery,
  organizationId,
  selectedInstanceKey,
  selectedInstanceQuery,
} = useConsoleInstances()
const { isPending, restart } = useRestartOperation()
const lastOperationTaskId = shallowRef<string | undefined>()

const selectedKey = computed(() => selectedInstanceKey.value ?? instances.value[0]?.instanceKey)

async function restartInstance(instanceKey: string) {
  const task = await restart(instanceKey)
  lastOperationTaskId.value = task.operationTaskId
}
</script>

<template>
  <DefaultLayout>
    <section class="console-page">
      <header class="console-page-header">
        <div>
          <h1 class="console-page-title">实例控制台</h1>
          <p class="console-page-copy">{{ organizationId }} / {{ environmentId }}</p>
        </div>
        <RouterLink v-if="lastOperationTaskId" class="console-page-link" :to="`/operations/${lastOperationTaskId}`">
          查看最近任务
        </RouterLink>
      </header>

      <p v-if="instancesQuery.error.value" class="console-page-error">实例查询失败：{{ instancesQuery.error.value.message }}</p>
      <div class="console-page-grid">
        <InstanceTable
          :instances="instances"
          :selected-instance-key="selectedKey"
          :restart-pending="isPending"
          @select-instance="selectedInstanceKey = $event"
          @restart-instance="restartInstance"
        />
        <InstanceDetailPanel :instance="selectedInstanceQuery.data.value" />
      </div>
    </section>
  </DefaultLayout>
</template>

<style scoped>
.console-page {
  display: grid;
  gap: 1rem;
}

.console-page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.console-page-title {
  margin: 0;
  font-size: 1.625rem;
}

.console-page-copy {
  margin: 0.25rem 0 0;
  color: var(--color-text-muted);
}

.console-page-link {
  color: var(--color-accent);
  font-weight: 700;
  text-decoration: none;
}

.console-page-error {
  margin: 0;
  color: var(--color-danger);
}

.console-page-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 22rem;
  gap: 1rem;
}

@media (max-width: 1020px) {
  .console-page-grid {
    grid-template-columns: 1fr;
  }
}
</style>
```

- [ ] **Step 7: Add operation detail page**

Create `frontend/apps/console/src/pages/operations/[operationTaskId].vue`:

```vue
<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router/auto'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import OperationTimeline from '@/components/console/OperationTimeline.vue'
import { useOperationTask } from '@/composables/useConsoleOperations'

const route = useRoute('/operations/[operationTaskId]')
const operationTaskId = computed(() => route.params.operationTaskId)
const operationQuery = useOperationTask(operationTaskId.value)
</script>

<template>
  <DefaultLayout>
    <section class="operation-page">
      <header>
        <h1 class="operation-page-title">运维任务</h1>
        <p class="operation-page-copy">{{ operationTaskId }}</p>
      </header>
      <p v-if="operationQuery.error.value" class="operation-page-error">任务查询失败：{{ operationQuery.error.value.message }}</p>
      <OperationTimeline :operation="operationQuery.data.value" />
    </section>
  </DefaultLayout>
</template>

<style scoped>
.operation-page {
  display: grid;
  gap: 1rem;
  max-width: 56rem;
}

.operation-page-title {
  margin: 0;
  font-size: 1.625rem;
}

.operation-page-copy {
  margin: 0.25rem 0 0;
  color: var(--color-text-muted);
}

.operation-page-error {
  margin: 0;
  color: var(--color-danger);
}
</style>
```

- [ ] **Step 8: Run console tests, typecheck and build**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/console test
pnpm -C frontend --filter @nerv-iip/console typecheck
pnpm -C frontend --filter @nerv-iip/console build
```

Expected: all commands exit with code `0`.

- [ ] **Step 9: Commit**

Run:

```powershell
git add frontend/apps/console frontend/pnpm-lock.yaml
git commit -m "feat: add console instance operations view"
```

## Task 7: Add Third Slice Verification And Documentation

**Files:**

- Create: `scripts/verify-third-slice-console.ps1`
- Modify: `README.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Create third-slice verification script**

Create `scripts/verify-third-slice-console.ps1`:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

pwsh scripts/verify-second-slice-ops.ps1
pwsh scripts/export-gateway-openapi.ps1
pnpm -C frontend install --frozen-lockfile
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build

Write-Host "Third vertical slice console verified."
```

- [ ] **Step 2: Run third-slice verification**

Run:

```powershell
pwsh scripts/verify-third-slice-console.ps1
```

Expected:

```text
Second vertical slice verified with operationTaskId op-000001.
Gateway OpenAPI exported to ...
Third vertical slice console verified.
```

- [ ] **Step 3: Update README current status**

Confirm the README "实施计划" list includes this plan, then add the third-slice verification paragraph after the second-slice paragraph in "当前状态":

```markdown
第三阶段控制台纵切可以用 `scripts/verify-third-slice-console.ps1` 验证：Gateway 暴露稳定 OpenAPI，frontend 工作区可生成类型安全 api-client，console 可展示实例列表与详情、创建 restart 任务并查看 OperationTask 状态。
```

- [ ] **Step 4: Update implementation readiness**

Replace the existing "第三迭代计划范围" section with "第三迭代已落地范围":

```markdown
### 第三迭代已落地范围

1. Gateway 已提供控制台 OpenAPI 文档与稳定 operationId。
2. frontend 工作区已创建 pnpm workspace、console 应用、api-client、ui 和 app-shell 初版。
3. api-client 已通过 Hey API 从 Gateway OpenAPI 生成 types、fetch SDK 和 Pinia Colada query/mutation options。
4. console 首屏已展示实例列表、实例详情、restart 动作入口和 OperationTask 状态页。
5. 以 docs/superpowers/plans/2026-05-16-third-vertical-slice-console.md 和 scripts/verify-third-slice-console.ps1 作为第三阶段验收口径。
```

- [ ] **Step 5: Commit**

Run:

```powershell
git add scripts/verify-third-slice-console.ps1 README.md docs/architecture/implementation-readiness.md
git commit -m "docs: document third console vertical slice"
```

## Execution Order

1. Task 1 must be first because frontend code generation needs a stable OpenAPI document and operation IDs.
2. Task 2 must finish before frontend packages can be installed and filtered with pnpm.
3. Task 3 depends on Task 1 OpenAPI and Task 2 workspace setup.
4. Task 4 depends on Task 2 workspace setup and can run in parallel with Task 3 after package manifests exist.
5. Task 5 depends on Tasks 3 and 4 because the console app imports `api-client`, `ui` and `app-shell`.
6. Task 6 depends on Task 5 runtime bootstrapping.
7. Task 7 depends on all implementation tasks.

Recommended parallelization after Task 2:

1. One worker implements Task 3 API client generation.
2. One worker implements Task 4 UI and shell packages.
3. One worker prepares Task 5 console app bootstrapping after Tasks 3 and 4 expose package names.

## Third Iteration Completion Definition

The third iteration is complete when all statements are true:

1. Gateway exposes `/swagger/v1/swagger.json`.
2. Gateway OpenAPI contains operation IDs `listConsoleInstances`, `getConsoleInstanceDetail`, `restartConsoleInstance` and `getConsoleOperationTask`.
3. `frontend/packages/api-client` generates Hey API fetch SDK, TypeScript types and Pinia Colada query/mutation options from Gateway OpenAPI.
4. Console app installs Pinia before Pinia Colada and configures the API client before mounting Vue.
5. Console app uses Vue Router file-based routes under `src/pages` and commits `typed-router.d.ts`.
6. Console index page renders instance list and detail from generated query options.
7. Console restart action calls generated mutation options and exposes the latest OperationTask link.
8. Operation detail page polls OperationTask status through generated query options.
9. No console page handwrites Gateway URLs or service DTOs.
10. `pwsh scripts/verify-third-slice-console.ps1` exits with code `0`.

## Self Review

Spec coverage:

1. README next-stage frontend workspace item: covered by Tasks 2, 4 and 5.
2. API client generation chain: covered by Tasks 1 and 3.
3. Instance query and low-risk restart console flow: covered by Task 6.
4. Verification and documentation: covered by Task 7.
5. Existing backend boundary rules: preserved by Task 1 tests and no service Domain/Infrastructure references.

Placeholder scan:

1. No unresolved markers or undefined fill-in work remains.
2. All file paths are explicit.
3. Each implementation task has commands and expected outputs.

Type consistency:

1. Operation IDs used in Gateway OpenAPI match api-client exports and console composables.
2. `organizationId`, `environmentId`, `instanceKey` and `operationTaskId` match existing Gateway request shapes.
3. `lifecycle.restart` remains the only operation code used by the console action in this phase.

## Source Notes

1. FastEndpoints Swagger support uses `FastEndpoints.Swagger`, `.SwaggerDocument()` and `.UseSwaggerGen()`; operation IDs can be controlled with an endpoint name generator or `Description(x => x.WithName(...))`; attribute-based endpoint configuration is a limited alternative to `Configure()` and the two styles should not be mixed. See https://fast-endpoints.com/docs/swagger-support and https://fast-endpoints.com/docs/get-started.
2. Vue Router file-based routing uses `vue-router/vite`, `vue-router/auto-routes` and committed `typed-router.d.ts` according to https://router.vuejs.org/file-based-routing/.
3. Hey API Pinia Colada generation uses `@hey-api/client-fetch`, `@hey-api/sdk`, `@hey-api/typescript` and `@pinia/colada` plugin configuration according to https://mintlify.wiki/hey-api/openapi-ts/state-management/pinia-colada.
4. Pinia Colada must be installed after Pinia through `app.use(PiniaColada, ...)` according to https://pinia-colada.esm.dev/guide/installation.html.
5. Operation detail polling uses `@pinia/colada-plugin-auto-refetch` and per-query `autoRefetch` according to https://pinia-colada.esm.dev/plugins/official/auto-refetch.html.
