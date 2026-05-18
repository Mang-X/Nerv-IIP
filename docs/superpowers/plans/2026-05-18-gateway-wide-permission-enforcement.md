# Gateway-Wide Permission Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce IAM-backed permissions on existing PlatformGateway console APIs before adding Console login UI or new product surfaces.

**Architecture:** PlatformGateway remains a thin BFF and does not read IAM persistence directly. Gateway forwards the incoming bearer token to a new IAM internal authorization check endpoint, and IAM validates session, security stamp, permission version, organization, environment and permission code from its own facts. Existing console endpoints gain manual permission gates and preserve stable operation IDs.

**Tech Stack:** .NET 10, FastEndpoints, xUnit, ASP.NET Core `WebApplicationFactory`, existing IAM PostgreSQL/InMemory profiles, existing Gateway AppHub/Ops HTTP clients.

---

## Completion Record

This plan starts after script governance backlog completion on branch `codex/script-governance-backlog-completion`.

Boundaries:

1. Do not implement Console login UI in this plan.
2. Do not implement OAuth/OIDC, SSO, MFA or ABAC.
3. Do not let PlatformGateway reference IAM Domain or Infrastructure.
4. Do not add high-risk Ops approval flows; only protect current restart task creation and task detail reads.
5. Do not broaden frontend visual/design system work; OpenAPI/api-client regeneration is mechanical only if contracts change.

## File Structure Map

```text
backend/common/Contracts/Nerv.IIP.Contracts.Iam/
  Nerv.IIP.Contracts.Iam.csproj
  AuthorizationContracts.cs

backend/services/Iam/src/Nerv.IIP.Iam.Web/
  Application/Auth/IamAuthService.cs
  Endpoints/Authorization/AuthorizationCheckEndpoint.cs

backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/
  IamAuthorizationCheckEndpointTests.cs

backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/
  Program.cs
  Application/Auth/GatewayAuthorization.cs
  Application/Auth/GatewayAuthorizationClient.cs
  Endpoints/Instances/InstanceEndpoints.cs
  Endpoints/Operations/OperationEndpoints.cs

backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/
  GatewayAuthorizationTests.cs
  GatewayInstanceTests.cs
  GatewayOperationTests.cs
  GatewayOpenApiTests.cs

docs/architecture/
  iam-authentication-baseline.md
  implementation-readiness.md
```

## Task 1: Add Shared IAM Authorization Contract

**Files:**

- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Iam/AuthorizationContracts.cs`
- Modify: `backend/Nerv.IIP.sln`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj`

- [x] **Step 1: Create the contract project**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Iam -o backend/common/Contracts/Nerv.IIP.Contracts.Iam --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj
dotnet add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj
dotnet add backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj
```

Expected: commands exit `0` and no service Domain or Infrastructure reference is introduced into PlatformGateway.

- [x] **Step 2: Replace the generated class with authorization DTOs**

Create `backend/common/Contracts/Nerv.IIP.Contracts.Iam/AuthorizationContracts.cs`:

```csharp
namespace Nerv.IIP.Contracts.Iam;

public sealed record AuthorizationCheckRequest(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId);

public sealed record AuthorizationCheckResponse(
    bool Allowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason);
```

Delete the template `Class1.cs` if it exists.

- [x] **Step 3: Build the contract project**

Run:

```powershell
dotnet build backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj --no-restore
```

Expected: build exits `0`.

- [x] **Step 4: Commit the shared contract**

Run:

```powershell
git add backend/Nerv.IIP.sln
git add backend/common/Contracts/Nerv.IIP.Contracts.Iam
git add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj
git commit -m "feat: add iam authorization check contract"
```

## Task 2: Add IAM Authorization Check Endpoint

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Authorization/AuthorizationCheckEndpoint.cs`
- Create: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamAuthorizationCheckEndpointTests.cs`

- [x] **Step 1: Write failing IAM endpoint tests**

Create `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamAuthorizationCheckEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Tests;

public sealed class IamAuthorizationCheckEndpointTests
{
    [Fact]
    public async Task Authorization_check_rejects_anonymous_callers_before_touching_persistence()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest("apphub.instances.read", "org-001", "env-dev", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_check_allows_seeded_admin_for_matching_organization_environment_and_permission()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new LoginRequest("admin", "admin"));
        var tokens = await auth.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await client.PostAsJsonAsync("/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest("apphub.instances.read", "org-001", "env-dev", "application-instance", "demo-api-001"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthorizationCheckResponse>();
        Assert.True(body!.Allowed);
        Assert.Equal("user", body.PrincipalType);
        Assert.Equal("admin", body.LoginName);
    }

    [Fact]
    public async Task Authorization_check_denies_wrong_environment_even_when_permission_code_exists()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        var auth = await client.PostAsJsonAsync("/api/iam/v1/auth/login", new LoginRequest("admin", "admin"));
        var tokens = await auth.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await client.PostAsJsonAsync("/internal/iam/v1/authorization/check",
            new AuthorizationCheckRequest("apphub.instances.read", "org-001", "env-prod", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

If the local seed password is configured differently in tests, use the same test fixture pattern already used by `IamPostgresProfileTests` to set `Iam:Seed:AdminPassword=admin` and seed InMemory.

- [x] **Step 2: Run the new tests and confirm red state**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamAuthorizationCheckEndpointTests
```

Expected: FAIL because `/internal/iam/v1/authorization/check` does not exist yet.

- [x] **Step 3: Add organization/environment scoped permission check**

In `IamAuthService.cs`, keep the existing `UserHasPermissionAsync(string userId, string permissionCode, ...)` overload for current IAM management endpoints and add:

```csharp
public async Task<bool> UserHasPermissionAsync(
    string userId,
    string organizationId,
    string environmentId,
    string permissionCode,
    CancellationToken cancellationToken)
{
    var dbContext = GetDbContext();
    var userIdValue = new UserId(userId);
    var organizationIdValue = new OrganizationId(organizationId);
    var environmentIdValue = new IamEnvironmentId(environmentId);

    return await (
        from membership in dbContext.Memberships
        join membershipRole in dbContext.MembershipRoles on membership.Id equals membershipRole.MembershipId
        join role in dbContext.Roles on membershipRole.RoleId equals role.Id
        join rolePermission in dbContext.RolePermissions on role.Id equals rolePermission.RoleId
        where membership.UserId == userIdValue
            && membership.OrganizationId == organizationIdValue
            && membership.EnvironmentId == environmentIdValue
            && role.Deleted == NotDeleted
            && rolePermission.PermissionCode == permissionCode
        select rolePermission.Id)
        .AnyAsync(cancellationToken);
}
```

Add `using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;` and `using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;` if the file does not already compile.

- [x] **Step 4: Add the internal endpoint**

Create `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Authorization/AuthorizationCheckEndpoint.cs`:

```csharp
using FastEndpoints;
using Nerv.IIP.Contracts.Iam;
using Nerv.IIP.Iam.Web.Application.Auth;

namespace Nerv.IIP.Iam.Web.Endpoints.Authorization;

[HttpPost("/internal/iam/v1/authorization/check")]
public sealed class AuthorizationCheckEndpoint(IamAuthService auth) : Endpoint<AuthorizationCheckRequest, AuthorizationCheckResponse>
{
    public override async Task HandleAsync(AuthorizationCheckRequest req, CancellationToken ct)
    {
        var principal = await auth.GetCurrentPrincipalAsync(HttpContext, ct);
        if (principal is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await HttpContext.Response.WriteAsJsonAsync(
                new AuthorizationCheckResponse(false, null, null, null, "unauthorized"),
                ct);
            return;
        }

        var allowed = await auth.UserHasPermissionAsync(
            principal.UserId,
            req.OrganizationId,
            req.EnvironmentId,
            req.PermissionCode,
            ct);

        if (!allowed)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await HttpContext.Response.WriteAsJsonAsync(
                new AuthorizationCheckResponse(false, principal.UserId, principal.PrincipalType, principal.LoginName, "forbidden"),
                ct);
            return;
        }

        await HttpContext.Response.WriteAsJsonAsync(
            new AuthorizationCheckResponse(true, principal.UserId, principal.PrincipalType, principal.LoginName, null),
            ct);
    }
}
```

- [x] **Step 5: Run IAM authorization tests**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamAuthorizationCheckEndpointTests
```

Expected: PASS.

- [x] **Step 6: Commit IAM authorization check endpoint**

Run:

```powershell
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Authorization/AuthorizationCheckEndpoint.cs
git add backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamAuthorizationCheckEndpointTests.cs
git commit -m "feat: add iam authorization check endpoint"
```

## Task 3: Add Gateway Authorization Client And Helper

**Files:**

- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorization.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorizationClient.cs`
- Create: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayAuthorizationTests.cs`

- [x] **Step 1: Write failing Gateway authorization tests**

Create `GatewayAuthorizationTests.cs` with a fake authorization client:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayAuthorizationTests
{
    [Fact]
    public async Task Console_instances_require_bearer_token()
    {
        var auth = FakeGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(auth.LastRequirement);
    }

    [Fact]
    public async Task Console_instances_return_forbidden_when_iam_denies_permission()
    {
        var auth = FakeGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "token-without-permission");

        var response = await client.GetAsync("/api/console/v1/instances?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("apphub.instances.read", auth.LastRequirement!.PermissionCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeGatewayAuthorizationClient auth) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayAuthorizationClient>();
            services.AddSingleton<IGatewayAuthorizationClient>(auth);
        }));
}
```

Add fake implementation at the bottom of the file:

```csharp
internal sealed class FakeGatewayAuthorizationClient(bool allowed) : IGatewayAuthorizationClient
{
    public GatewayPermissionRequirement? LastRequirement { get; private set; }
    public static FakeGatewayAuthorizationClient Allowed() => new(true);
    public static FakeGatewayAuthorizationClient Forbidden() => new(false);

    public Task<GatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        LastRequirement = requirement;
        return Task.FromResult(allowed
            ? GatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
            : GatewayAuthorizationResult.Forbidden("forbidden"));
    }
}
```

- [x] **Step 2: Run Gateway authorization tests and confirm red state**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayAuthorizationTests
```

Expected: FAIL because Gateway has no `IGatewayAuthorizationClient` or permission helper.

- [x] **Step 3: Add Gateway authorization models and helper**

Create `Application/Auth/GatewayAuthorization.cs`:

```csharp
namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed record GatewayPermissionRequirement(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId);

public sealed record GatewayAuthorizationResult(
    bool Allowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason)
{
    public static GatewayAuthorizationResult Allowed(string principalId, string principalType, string loginName) =>
        new(true, principalId, principalType, loginName, null);

    public static GatewayAuthorizationResult Forbidden(string reason) =>
        new(false, null, null, null, reason);
}

public interface IGatewayAuthorizationClient
{
    Task<GatewayAuthorizationResult> CheckAsync(string bearerToken, GatewayPermissionRequirement requirement, CancellationToken cancellationToken);
}

public static class GatewayPermissions
{
    public const string AppHubInstancesRead = "apphub.instances.read";
    public const string OpsTasksCreate = "ops.tasks.create";
    public const string OpsTasksRead = "ops.tasks.read";
}

public static class GatewayAuthorization
{
    public const string PrincipalItemKey = "Nerv.IIP.PlatformGateway.Principal";

    public static async Task<GatewayAuthorizationResult?> RequireAsync(
        HttpContext context,
        IGatewayAuthorizationClient client,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        var bearerToken = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(bearerToken) || !bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { title = "Unauthorized", detail = "Unauthorized.", status = StatusCodes.Status401Unauthorized }, cancellationToken);
            return null;
        }

        var result = await client.CheckAsync(bearerToken["Bearer ".Length..], requirement, cancellationToken);
        if (!result.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { title = "Forbidden", detail = "Forbidden.", status = StatusCodes.Status403Forbidden }, cancellationToken);
            return null;
        }

        context.Items[PrincipalItemKey] = result;
        return result;
    }
}
```

- [x] **Step 4: Add the IAM-backed Gateway client**

Create `Application/Auth/GatewayAuthorizationClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed class HttpGatewayAuthorizationClient(HttpClient httpClient) : IGatewayAuthorizationClient
{
    public async Task<GatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        GatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/iam/v1/authorization/check");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = JsonContent.Create(new AuthorizationCheckRequest(
            requirement.PermissionCode,
            requirement.OrganizationId,
            requirement.EnvironmentId,
            requirement.ResourceType,
            requirement.ResourceId));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return GatewayAuthorizationResult.Forbidden("unauthorized");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return GatewayAuthorizationResult.Forbidden("forbidden");
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthorizationCheckResponse>(cancellationToken);
        return body is not null && body.Allowed
            ? GatewayAuthorizationResult.Allowed(body.PrincipalId!, body.PrincipalType!, body.LoginName!)
            : GatewayAuthorizationResult.Forbidden(body?.DenialReason ?? "forbidden");
    }
}
```

In `Program.cs`, register:

```csharp
builder.Services.AddHttpClient<IGatewayAuthorizationClient, HttpGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
```

- [x] **Step 5: Run Gateway authorization tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayAuthorizationTests
```

Expected: tests still fail until endpoints call `GatewayAuthorization.RequireAsync`; Task 4 completes the green state.

## Task 4: Protect Existing Console Endpoints

**Files:**

- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Instances/InstanceEndpoints.cs`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Operations/OperationEndpoints.cs`
- Modify: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayInstanceTests.cs`
- Modify: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOperationTests.cs`

- [x] **Step 1: Inject authorization client into instance endpoints**

Change constructors to include `IGatewayAuthorizationClient auth` and add the guard at the start of each handler:

```csharp
var principal = await GatewayAuthorization.RequireAsync(
    HttpContext,
    auth,
    new GatewayPermissionRequirement(
        GatewayPermissions.AppHubInstancesRead,
        req.OrganizationId,
        req.EnvironmentId,
        "application-instance",
        req.InstanceKey),
    ct);
if (principal is null)
{
    return;
}
```

For list, pass `ResourceId: null`. Keep AppHub calls behind the guard.

- [x] **Step 2: Inject authorization client into operation endpoints**

For restart:

```csharp
var principal = await GatewayAuthorization.RequireAsync(
    HttpContext,
    auth,
    new GatewayPermissionRequirement(
        GatewayPermissions.OpsTasksCreate,
        req.OrganizationId,
        req.EnvironmentId,
        "application-instance",
        Route<string>("instanceKey")),
    ct);
if (principal is null)
{
    return;
}
```

Use `principal.PrincipalId ?? "unknown"` as `requestedBy` instead of `X-User-Id` or `local-admin`.

For operation detail, require `GatewayPermissions.OpsTasksRead`. If the current route lacks organization/environment, add `OrganizationId` and `EnvironmentId` query parameters to the request type before authorizing.

- [x] **Step 3: Update existing Gateway tests to include authorization**

In existing Gateway tests, register `FakeGatewayAuthorizationClient.Allowed()` and send `Authorization: Bearer test-token` before calling protected endpoints:

```csharp
services.RemoveAll<IGatewayAuthorizationClient>();
services.AddSingleton<IGatewayAuthorizationClient>(FakeGatewayAuthorizationClient.Allowed());
```

```csharp
client.DefaultRequestHeaders.Authorization = new("Bearer", "test-token");
```

- [x] **Step 4: Assert permission mapping and no downstream calls on denied requests**

Add assertions in Gateway tests:

```csharp
Assert.Equal("apphub.instances.read", auth.LastRequirement!.PermissionCode);
Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
Assert.Equal(0, fake.QueryCallCount);
```

For restart:

```csharp
Assert.Equal("ops.tasks.create", auth.LastRequirement!.PermissionCode);
Assert.Equal("user-admin", fake.LastRequest!.RequestedBy);
```

For operation detail:

```csharp
Assert.Equal("ops.tasks.read", auth.LastRequirement!.PermissionCode);
```

- [x] **Step 5: Run Gateway tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
```

Expected: all Gateway tests pass.

- [x] **Step 6: Commit Gateway enforcement**

Run:

```powershell
git add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
git add backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests
git commit -m "feat: enforce gateway console permissions"
```

## Task 5: Preserve OpenAPI And Regenerate Mechanical Client If Needed

**Files:**

- Modify if generated: `frontend/packages/api-client/openapi/platform-gateway.v1.json`
- Modify if generated: `frontend/packages/api-client/src/generated/**`
- Modify if generated: `frontend/apps/console/typed-router.d.ts`

- [x] **Step 1: Run Gateway OpenAPI tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayOpenApiTests
```

Expected: stable operation IDs remain `listConsoleInstances`, `getConsoleInstanceDetail`, `restartConsoleInstance`, `getConsoleOperationTask`.

- [x] **Step 2: Run third-stage console verification**

Run:

```powershell
pwsh scripts/verify-third-slice-console.ps1
```

Expected: final output says `Third vertical slice console verified.` Any generated OpenAPI/api-client diff must be inspected and staged only if the Gateway contract actually changed.

- [x] **Step 3: Commit mechanical OpenAPI/client updates only if present**

If `git status --short frontend/packages/api-client frontend/apps/console/typed-router.d.ts` is clean, skip this step. Otherwise:

```powershell
git add frontend/packages/api-client/openapi/platform-gateway.v1.json
git add frontend/packages/api-client/src/generated
git add frontend/apps/console/typed-router.d.ts
git commit -m "chore: regenerate gateway console client"
```

## Task 6: Documentation And Final Verification

**Files:**

- Modify: `docs/architecture/iam-authentication-baseline.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify if needed: `docs/architecture/api-contract-and-codegen.md`

- [x] **Step 1: Update IAM baseline**

Add to `docs/architecture/iam-authentication-baseline.md` current implementation status:

```markdown
Gateway-wide permission enforcement now routes existing console APIs through IAM's internal authorization check endpoint. Gateway does not read IAM persistence directly; it forwards the caller bearer token and required permission/context, and IAM validates session, security stamp, permission version, organization, environment and permission code from IAM-owned facts.
```

- [x] **Step 2: Update implementation readiness**

Change the "Gateway 全面鉴权..." sentence under the seventh iteration section from future work to completed scope for existing console endpoints, while keeping Console login UI and OAuth/OIDC/SSO/MFA/ABAC as future work.

- [x] **Step 3: Run final verification**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
dotnet test backend/Nerv.IIP.sln --no-restore
pwsh scripts/check-script-governance.ps1
git diff --check
```

Expected: every command exits `0`. `git diff --check` may print CRLF warnings but must not report whitespace errors.

- [x] **Step 4: Commit documentation**

Run:

```powershell
git add docs/architecture/iam-authentication-baseline.md
git add docs/architecture/implementation-readiness.md
git add docs/architecture/api-contract-and-codegen.md
git commit -m "docs: record gateway permission enforcement"
```

## Execution Order

1. Task 1 first, so IAM and Gateway share a stable DTO contract without service implementation references.
2. Task 2 adds IAM as the only authorization fact owner.
3. Task 3 adds Gateway client plumbing but does not change endpoint behavior yet.
4. Task 4 flips existing console endpoints behind permission gates.
5. Task 5 preserves OpenAPI/client stability after endpoint behavior changes.
6. Task 6 records the new stage boundary and runs full verification.

## Self Review

Spec coverage:

1. Gateway-wide auth/permission enforcement is covered by Tasks 2-4.
2. IAM remains the identity and authorization fact source in Tasks 1-2.
3. PlatformGateway remains a BFF without IAM Domain/Infrastructure references in Tasks 1 and 3.
4. Existing console endpoints are mapped to concrete permission codes in Task 4.
5. Console login UI, OAuth/OIDC/SSO/MFA/ABAC and high-risk approval are explicitly out of scope.

Placeholder scan:

1. No task uses placeholder language.
2. Each code-changing task names exact files and concrete code shapes.
3. Each verification step names exact commands and expected outcomes.

Type consistency:

1. `AuthorizationCheckRequest` and `AuthorizationCheckResponse` are shared by IAM and Gateway.
2. `GatewayPermissionRequirement`, `GatewayAuthorizationResult`, and `IGatewayAuthorizationClient` are defined before endpoint tasks use them.
3. Permission constants match `docs/architecture/iam-authentication-baseline.md`: `apphub.instances.read`, `ops.tasks.create`, `ops.tasks.read`.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-18-gateway-wide-permission-enforcement.md`. Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration
2. **Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
