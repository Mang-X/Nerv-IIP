# Gateway 全面权限强制实施计划

> **面向代理执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**在增加 Console 登录 UI 或新产品界面之前，对现有 PlatformGateway Console API 强制实施由 IAM 支撑的权限校验。

**架构：**PlatformGateway 继续作为轻量 BFF，不直接读取 IAM 持久化数据。Gateway 将传入的 bearer 令牌转发至新的 IAM 内部授权检查端点，IAM 基于自身事实校验会话、安全戳、权限版本、组织、环境和权限代码。现有 Console 端点增加显式权限门禁，并保持操作 ID 稳定。

**技术栈：**.NET 10、FastEndpoints、xUnit、ASP.NET Core `WebApplicationFactory`、现有 IAM PostgreSQL/InMemory profile、现有 Gateway AppHub/Ops HTTP 客户端。

---

## 完成记录

本计划在分支 `codex/script-governance-backlog-completion` 完成脚本治理积压后开始。

边界：

1. 本计划不得实施 Console 登录 UI。
2. 不得实施 OAuth/OIDC、SSO、MFA 或 ABAC。
3. 不得让 PlatformGateway 引用 IAM Domain 或 Infrastructure。
4. 不得增加高风险 Ops 审批流程；只保护现有重启任务创建和任务详情读取。
5. 不得扩大前端视觉/设计系统工作范围；仅在契约发生变化时机械地重新生成 OpenAPI/api-client。

## 文件结构图

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

## 任务 1：增加共享 IAM 授权契约

**文件：**

- 新建：`backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj`
- 新建：`backend/common/Contracts/Nerv.IIP.Contracts.Iam/AuthorizationContracts.cs`
- 修改：`backend/Nerv.IIP.sln`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj`

- [x] **步骤 1：创建契约项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Iam -o backend/common/Contracts/Nerv.IIP.Contracts.Iam --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj
dotnet add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj
dotnet add backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj
```

预期：命令以 `0` 退出，且没有向 PlatformGateway 引入任何服务 Domain 或 Infrastructure 引用。

- [x] **步骤 2：用授权 DTO 替换生成的类**

新建 `backend/common/Contracts/Nerv.IIP.Contracts.Iam/AuthorizationContracts.cs`：

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

如果模板文件 `Class1.cs` 存在，则将其删除。

- [x] **步骤 3：构建契约项目**

运行：

```powershell
dotnet build backend/common/Contracts/Nerv.IIP.Contracts.Iam/Nerv.IIP.Contracts.Iam.csproj --no-restore
```

预期：构建以 `0` 退出。

- [x] **步骤 4：提交共享契约**

运行：

```powershell
git add backend/Nerv.IIP.sln
git add backend/common/Contracts/Nerv.IIP.Contracts.Iam
git add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Nerv.IIP.PlatformGateway.Web.csproj
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Nerv.IIP.Iam.Web.csproj
git commit -m "feat: add iam authorization check contract"
```

## 任务 2：增加 IAM 授权检查端点

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`
- 新建：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Authorization/AuthorizationCheckEndpoint.cs`
- 新建：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamAuthorizationCheckEndpointTests.cs`

- [x] **步骤 1：编写失败的 IAM 端点测试**

新建 `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamAuthorizationCheckEndpointTests.cs`：

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

如果测试中的本地初始密码配置不同，请使用 `IamPostgresProfileTests` 已采用的同一测试夹具模式设置 `Iam:Seed:AdminPassword=admin`，并初始化 InMemory 数据。

- [x] **步骤 2：运行新测试并确认红灯状态**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamAuthorizationCheckEndpointTests
```

预期：失败，因为 `/internal/iam/v1/authorization/check` 尚不存在。

- [x] **步骤 3：增加组织/环境范围的权限检查**

在 `IamAuthService.cs` 中，为当前 IAM 管理端点保留现有 `UserHasPermissionAsync(string userId, string permissionCode, ...)` 重载，并增加：

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

如果文件尚无法编译，请增加 `using Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;` 和 `using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;`。

- [x] **步骤 4：增加内部端点**

新建 `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Authorization/AuthorizationCheckEndpoint.cs`：

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

- [x] **步骤 5：运行 IAM 授权测试**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamAuthorizationCheckEndpointTests
```

预期：通过。

- [x] **步骤 6：提交 IAM 授权检查端点**

运行：

```powershell
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Authorization/AuthorizationCheckEndpoint.cs
git add backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamAuthorizationCheckEndpointTests.cs
git commit -m "feat: add iam authorization check endpoint"
```

## 任务 3：增加 Gateway 授权客户端和辅助程序

**文件：**

- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 新建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorization.cs`
- 新建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayAuthorizationClient.cs`
- 新建：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayAuthorizationTests.cs`

- [x] **步骤 1：编写失败的 Gateway 授权测试**

新建 `GatewayAuthorizationTests.cs`，其中使用模拟授权客户端：

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

在文件底部增加模拟实现：

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

- [x] **步骤 2：运行 Gateway 授权测试并确认红灯状态**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayAuthorizationTests
```

预期：失败，因为 Gateway 尚无 `IGatewayAuthorizationClient` 或权限辅助程序。

- [x] **步骤 3：增加 Gateway 授权模型和辅助程序**

新建 `Application/Auth/GatewayAuthorization.cs`：

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

- [x] **步骤 4：增加由 IAM 支撑的 Gateway 客户端**

新建 `Application/Auth/GatewayAuthorizationClient.cs`：

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

在 `Program.cs` 中注册：

```csharp
builder.Services.AddHttpClient<IGatewayAuthorizationClient, HttpGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
```

- [x] **步骤 5：运行 Gateway 授权测试**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayAuthorizationTests
```

预期：在端点调用 `GatewayAuthorization.RequireAsync` 之前，测试仍然失败；任务 4 将使测试转为绿灯状态。

## 任务 4：保护现有 Console 端点

**文件：**

- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Instances/InstanceEndpoints.cs`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Operations/OperationEndpoints.cs`
- 修改：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayInstanceTests.cs`
- 修改：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOperationTests.cs`

- [x] **步骤 1：向实例端点注入授权客户端**

修改构造函数以包含 `IGatewayAuthorizationClient auth`，并在每个处理器开头增加门禁：

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

对于列表，传入 `ResourceId: null`。确保 AppHub 调用位于门禁之后。

- [x] **步骤 2：向操作端点注入授权客户端**

对于重启：

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

使用 `principal.PrincipalId ?? "unknown"` 作为 `requestedBy`，而不是 `X-User-Id` 或 `local-admin`。

对于操作详情，要求 `GatewayPermissions.OpsTasksRead`。如果当前路由缺少组织/环境信息，请在授权前向请求类型增加 `OrganizationId` 和 `EnvironmentId` 查询参数。

- [x] **步骤 3：更新现有 Gateway 测试以包含授权**

在现有 Gateway 测试中，注册 `FakeGatewayAuthorizationClient.Allowed()`，并在调用受保护端点前发送 `Authorization: Bearer test-token`：

```csharp
services.RemoveAll<IGatewayAuthorizationClient>();
services.AddSingleton<IGatewayAuthorizationClient>(FakeGatewayAuthorizationClient.Allowed());
```

```csharp
client.DefaultRequestHeaders.Authorization = new("Bearer", "test-token");
```

- [x] **步骤 4：断言权限映射，并断言请求被拒绝时不调用下游**

在 Gateway 测试中增加断言：

```csharp
Assert.Equal("apphub.instances.read", auth.LastRequirement!.PermissionCode);
Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
Assert.Equal(0, fake.QueryCallCount);
```

对于重启：

```csharp
Assert.Equal("ops.tasks.create", auth.LastRequirement!.PermissionCode);
Assert.Equal("user-admin", fake.LastRequest!.RequestedBy);
```

对于操作详情：

```csharp
Assert.Equal("ops.tasks.read", auth.LastRequirement!.PermissionCode);
```

- [x] **步骤 5：运行 Gateway 测试**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
```

预期：所有 Gateway 测试通过。

- [x] **步骤 6：提交 Gateway 权限强制变更**

运行：

```powershell
git add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
git add backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests
git commit -m "feat: enforce gateway console permissions"
```

## 任务 5：保持 OpenAPI 稳定，并按需机械地重新生成客户端

**文件：**

- 如果生成则修改：`frontend/packages/api-client/openapi/platform-gateway.v1.json`
- 如果生成则修改：`frontend/packages/api-client/src/generated/**`
- 如果生成则修改：`frontend/apps/console/typed-router.d.ts`

- [x] **步骤 1：运行 Gateway OpenAPI 测试**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayOpenApiTests
```

预期：稳定的操作 ID 仍为 `listConsoleInstances`、`getConsoleInstanceDetail`、`restartConsoleInstance`、`getConsoleOperationTask`。

- [x] **步骤 2：运行第三阶段 Console 验证**

运行：

```powershell
pwsh scripts/verify-third-slice-console.ps1
```

预期：最终输出显示 `Third vertical slice console verified.`。必须检查所有生成的 OpenAPI/api-client 差异，并且仅在 Gateway 契约确实发生变化时暂存这些差异。

- [x] **步骤 3：仅在存在变更时提交机械生成的 OpenAPI/客户端更新**

如果 `git status --short frontend/packages/api-client frontend/apps/console/typed-router.d.ts` 结果为空，则跳过此步骤。否则：

```powershell
git add frontend/packages/api-client/openapi/platform-gateway.v1.json
git add frontend/packages/api-client/src/generated
git add frontend/apps/console/typed-router.d.ts
git commit -m "chore: regenerate gateway console client"
```

## 任务 6：文档与最终验证

**文件：**

- 修改：`docs/architecture/iam-authentication-baseline.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 按需修改：`docs/architecture/api-contract-and-codegen.md`

- [x] **步骤 1：更新 IAM 基线**

在 `docs/architecture/iam-authentication-baseline.md` 的当前实施状态中增加：

```markdown
Gateway-wide permission enforcement now routes existing console APIs through IAM's internal authorization check endpoint. Gateway does not read IAM persistence directly; it forwards the caller bearer token and required permission/context, and IAM validates session, security stamp, permission version, organization, environment and permission code from IAM-owned facts.
```

- [x] **步骤 2：更新实施就绪状态**

将第七轮迭代部分的“Gateway 全面鉴权...”句子从未来工作改为现有 Console 端点的已完成范围，同时继续将 Console 登录 UI 和 OAuth/OIDC/SSO/MFA/ABAC 保留为未来工作。

- [x] **步骤 3：运行最终验证**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
dotnet test backend/Nerv.IIP.sln --no-restore
pwsh scripts/check-script-governance.ps1
git diff --check
```

预期：每条命令都以 `0` 退出。`git diff --check` 可以输出 CRLF 警告，但不得报告空白错误。

- [x] **步骤 4：提交文档**

运行：

```powershell
git add docs/architecture/iam-authentication-baseline.md
git add docs/architecture/implementation-readiness.md
git add docs/architecture/api-contract-and-codegen.md
git commit -m "docs: record gateway permission enforcement"
```

## 执行顺序

1. 首先执行任务 1，使 IAM 和 Gateway 共享稳定的 DTO 契约，同时不引用服务实现。
2. 任务 2 将 IAM 确立为唯一的授权事实所有者。
3. 任务 3 增加 Gateway 客户端连接层，但尚不改变端点行为。
4. 任务 4 将现有 Console 端点置于权限门禁之后。
5. 任务 5 在端点行为变化后保持 OpenAPI/客户端稳定。
6. 任务 6 记录新的阶段边界并运行完整验证。

## 自我审核

规格覆盖：

1. 任务 2–4 覆盖 Gateway 全面认证/权限强制。
2. 在任务 1–2 中，IAM 继续作为身份和授权事实来源。
3. 在任务 1 和任务 3 中，PlatformGateway 继续作为不引用 IAM Domain/Infrastructure 的 BFF。
4. 任务 4 将现有 Console 端点映射到具体权限代码。
5. Console 登录 UI、OAuth/OIDC/SSO/MFA/ABAC 和高风险审批明确不在范围内。

占位符扫描：

1. 没有任务使用占位语言。
2. 每项代码变更任务都给出准确文件和具体代码形态。
3. 每个验证步骤都给出准确命令和预期结果。

类型一致性：

1. `AuthorizationCheckRequest` 和 `AuthorizationCheckResponse` 由 IAM 与 Gateway 共享。
2. `GatewayPermissionRequirement`、`GatewayAuthorizationResult` 和 `IGatewayAuthorizationClient` 均在端点任务使用它们之前定义。
3. 权限常量与 `docs/architecture/iam-authentication-baseline.md` 一致：`apphub.instances.read`、`ops.tasks.create`、`ops.tasks.read`。

## 执行交接

计划已完成并保存至 `docs/superpowers/plans/2026-05-18-gateway-wide-permission-enforcement.md`。有两种执行方式：

1. **子代理驱动（推荐）**——为每项任务分派新的子代理，在任务之间进行审核，以便快速迭代
2. **会话内执行**——在当前会话中使用 executing-plans 执行任务，分批实施并设置检查点

采用哪种方式？
