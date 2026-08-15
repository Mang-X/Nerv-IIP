# Console 认证与 shadcn 基线实施计划

> **面向代理执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**通过 PlatformGateway 增加首个具备生产形态的 Console 登录闭环，并确立 shadcn-vue 作为前端设计系统基线。

**架构：**浏览器继续只通过 PlatformGateway 访问 API。Gateway 暴露 Console 认证端点，将请求转发至 IAM 以获取身份和会话事实，并继续以 OpenAPI/api-client 生成物作为前端契约。前端在 `packages/ui` 中初始化 shadcn-vue，保留由 Pinia Colada 生成的 api-client 选项来管理服务端状态，并使用 Pinia 认证存储管理客户端会话状态。

**技术栈：**.NET 10、FastEndpoints、xUnit、ASP.NET Core `WebApplicationFactory`、Vue 3 `<script setup lang="ts">`、Vite、Pinia、Pinia Colada、Hey API OpenAPI TypeScript、采用 `nova` 预设的 shadcn-vue 官方组件注册表、lucide-vue-next。

---

## 当前基线

1. 当前分支为 `main`，比 `origin/main` 多出已提交的设计规格 `501ce97 docs: design console auth shadcn baseline`。
2. `docs/superpowers/specs/2026-05-18-console-auth-shadcn-design.md` 是已批准的设计依据。
3. `frontend/packages/api-client` 已使用 Hey API，并启用了 `@pinia/colada` 生成。
4. Console 当前使用 `@nerv-iip/api-client` 中生成的 Colada 选项。
5. `frontend/packages/ui` 仍包含本地基础组件：`UiButton`、`UiPanel`、`UiBadge`。
6. `pnpm dlx shadcn-vue@latest info --json` 当前报告不存在 `components.json`、Tailwind 配置和已初始化的 shadcn-vue 配置。
7. `pnpm dlx shadcn-vue@latest docs ...` 当前因 `logger.debug is not a function` 而失败；若实施期间文档命令持续失败，则使用 `search`、`view`、生成文件和官方组件注册表输出。

## 文件结构图

```text
backend/services/Iam/src/Nerv.IIP.Iam.Web/
  Application/Auth/IamAuthModels.cs
  Application/Auth/IamAuthService.cs
  Endpoints/Auth/AuthEndpoints.cs

backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/
  IamFoundationTests.cs
  IamPostgresProfileTests.cs

backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/
  Program.cs
  Application/Auth/ConsoleAuthModels.cs
  Application/Auth/GatewayIamAuthClient.cs
  Endpoints/Auth/ConsoleAuthEndpoints.cs

backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/
  GatewayConsoleAuthTests.cs
  GatewayOpenApiTests.cs

frontend/
  components.json
  package.json
  pnpm-lock.yaml
  vite.config.ts
  tsconfig.base.json

frontend/packages/ui/
  package.json
  tsconfig.json
  src/index.ts
  src/lib/utils.ts
  src/components/ui/**
  src/UiBadge.vue            (delete after migration)
  src/UiButton.vue           (delete after migration)
  src/UiPanel.vue            (delete after migration)

frontend/packages/api-client/
  openapi/platform-gateway.v1.json
  src/auth.ts
  src/console.ts
  src/index.ts
  src/transport/client-config.ts
  src/transport/client-config.test.ts
  src/generated/**

frontend/apps/console/src/
  main.ts
  App.vue
  assets/main.css
  api/auth.ts
  components/auth/LoginForm.vue
  components/auth/LoginForm.test.ts
  components/console/InstanceDetailPanel.vue
  components/console/InstanceTable.vue
  components/console/OperationTimeline.vue
  layouts/DefaultLayout.vue
  pages/login.vue
  router/index.ts
  router/guards/auth.ts
  router/guards/auth.test.ts
  stores/auth.ts
  stores/auth.test.ts
  test/setup.ts

docs/architecture/
  frontend-design-system-planning.md
  frontend-structure.md
  iam-authentication-baseline.md
  implementation-readiness.md
  database-schema-catalog.md

README.md
```

## 任务 1：为 Console 上下文补充 IAM 当前主体信息

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthModels.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Auth/AuthEndpoints.cs`
- 修改：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamFoundationTests.cs`
- 修改：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`

- [ ] **步骤 1：编写失败的 IAM `/me` 断言**

在 `IamFoundationTests.cs` 中，替换本地 `AuthResponse` 记录并增加 `MeResponse` 记录：

```csharp
private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
private sealed record MeResponse(
    string UserId,
    string LoginName,
    string Email,
    string PrincipalType,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion);
```

然后在 `Admin_can_login_refresh_logout_and_validate_connector_host` 中，于刷新成功后、注销前增加：

```csharp
_client.DefaultRequestHeaders.Authorization = new("Bearer", rotated.AccessToken);
var meBeforeLogout = await _client.GetAsync("/api/iam/v1/me");
meBeforeLogout.EnsureSuccessStatusCode();
var principal = await meBeforeLogout.Content.ReadFromJsonAsync<MeResponse>();

Assert.Equal("user-admin", principal!.UserId);
Assert.Equal("admin", principal.LoginName);
Assert.Equal("user", principal.PrincipalType);
Assert.Equal("org-001", principal.OrganizationId);
Assert.Equal("env-dev", principal.EnvironmentId);
Assert.Equal(1, principal.PermissionVersion);
Assert.True(rotated.ExpiresAtUtc > DateTimeOffset.UtcNow);
```

在 `IamPostgresProfileTests.cs` 中，将底部的本地记录改为：

```csharp
private sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
private sealed record MeResponse(
    string UserId,
    string LoginName,
    string Email,
    string PrincipalType,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion);
```

然后在现有 `var principal = await me.Content.ReadFromJsonAsync<MeResponse>();` 断言之后增加：

```csharp
Assert.Equal("user-admin", principal!.UserId);
Assert.Equal("admin", principal.LoginName);
Assert.Equal("user", principal.PrincipalType);
Assert.Equal("org-001", principal.OrganizationId);
Assert.Equal("env-dev", principal.EnvironmentId);
Assert.Equal(1, principal.PermissionVersion);
Assert.True(auth.ExpiresAtUtc > DateTimeOffset.UtcNow);
```

- [ ] **步骤 2：运行 IAM 测试并确认红灯状态**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~IamFoundationTests|FullyQualifiedName~IamPostgresProfileTests"
```

预期：失败，因为 `AuthResponse` 不包含 `ExpiresAtUtc`，且 `/me` 不返回组织、环境或权限版本。

- [ ] **步骤 3：扩展 IAM 认证模型**

在 `IamAuthModels.cs` 中，将现有 `AuthResponse` 和 `CurrentPrincipalResponse` 记录替换为：

```csharp
public sealed record AuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
public sealed record CurrentPrincipalResponse(
    string UserId,
    string LoginName,
    string Email,
    string PrincipalType,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion);
```

- [ ] **步骤 4：从 IAM 令牌创建流程返回到期时间**

在 `IamTokenService.cs` 中增加：

```csharp
public DateTimeOffset GetAccessTokenExpiresAtUtc(DateTimeOffset issuedAtUtc)
{
    return issuedAtUtc.AddMinutes(GetAccessTokenMinutes());
}
```

然后在 `IamAuthService.CreateSessionResponse` 中，将访问令牌创建代码块替换为：

```csharp
var issuedAtUtc = DateTimeOffset.UtcNow;
var accessToken = tokenService.CreateAccessToken(user, session);
var expiresAtUtc = tokenService.GetAccessTokenExpiresAtUtc(issuedAtUtc);
return new AuthResponse(accessToken, refreshToken, session.Id.Id, expiresAtUtc);
```

继续使用现有 `now` 值填充 `UserSession` 的签发和到期字段。

- [ ] **步骤 5：从 PostgreSQL `/me` 返回成员关系上下文**

在 `IamAuthService.GetCurrentPrincipalAsync` 中，于用户校验代码块之后、返回之前增加：

```csharp
var membership = await dbContext.Memberships
    .Where(x => x.UserId == userId)
    .OrderBy(x => x.Id)
    .FirstOrDefaultAsync(cancellationToken);
if (membership is null)
{
    return null;
}

return new CurrentPrincipalResponse(
    user.Id.Id,
    user.LoginName,
    user.Email,
    "user",
    membership.OrganizationId.Id,
    membership.EnvironmentId.Id,
    user.PermissionVersion);
```

删除旧的四字段 `CurrentPrincipalResponse` 返回代码。

- [ ] **步骤 6：从 InMemory `/me` 返回成员关系上下文**

在 `InMemoryIamStore.cs` 中增加：

```csharp
public CurrentPrincipalSnapshot GetCurrentPrincipal(UserFact user)
{
    lock (_gate)
    {
        var membership = _memberships
            .OrderBy(x => x.OrganizationId, StringComparer.Ordinal)
            .ThenBy(x => x.EnvironmentId, StringComparer.Ordinal)
            .FirstOrDefault(x => x.UserId == user.UserId)
            ?? throw new UnauthorizedAccessException("User has no membership.");

        return new CurrentPrincipalSnapshot(
            user.UserId,
            user.LoginName,
            user.Email,
            "user",
            membership.OrganizationId,
            membership.EnvironmentId,
            user.PermissionVersion);
    }
}
```

在同一文件底部增加：

```csharp
public sealed record CurrentPrincipalSnapshot(
    string UserId,
    string LoginName,
    string Email,
    string PrincipalType,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion);
```

在 `AuthEndpoints.cs` 中，将 InMemory `/me` 响应替换为：

```csharp
var principal = store.GetCurrentPrincipal(user);
await Send.OkAsync(new CurrentPrincipalResponse(
    principal.UserId,
    principal.LoginName,
    principal.Email,
    principal.PrincipalType,
    principal.OrganizationId,
    principal.EnvironmentId,
    principal.PermissionVersion), ct);
```

- [ ] **步骤 7：从 InMemory 登录/刷新流程返回到期时间**

在 `InMemoryIamStore.CreateSession` 中，将最终返回代码替换为：

```csharp
return new AuthResult(accessToken, refreshToken, sessionId, DateTimeOffset.UtcNow.AddMinutes(15));
```

在同一文件底部，将 `AuthResult` 替换为：

```csharp
public sealed record AuthResult(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
```

- [ ] **步骤 8：运行 IAM 测试并确认绿灯状态**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

预期：通过。

## 任务 2：增加 PlatformGateway Console 认证 facade

**文件：**

- 新建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/ConsoleAuthModels.cs`
- 新建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayIamAuthClient.cs`
- 新建：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Auth/ConsoleAuthEndpoints.cs`
- 新建：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayConsoleAuthTests.cs`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 修改：`backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOpenApiTests.cs`

- [ ] **步骤 1：编写失败的 Gateway 认证 facade 测试**

新建 `GatewayConsoleAuthTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Tests;

public sealed class GatewayConsoleAuthTests
{
    [Fact]
    public async Task Console_login_forwards_to_iam_and_returns_session_payload()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/console/v1/auth/login", new ConsoleLoginRequest("admin", "Admin123!"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsoleAuthResponse>();
        Assert.Equal("access-token", body!.AccessToken);
        Assert.Equal("refresh-token", body.RefreshToken);
        Assert.Equal("session-001", body.SessionId);
        Assert.Equal("admin", body.Principal.LoginName);
        Assert.Equal("org-001", body.Principal.OrganizationId);
        Assert.Equal("env-dev", body.Principal.EnvironmentId);
        Assert.Equal(new ConsoleLoginRequest("admin", "Admin123!"), iam.LastLogin);
    }

    [Fact]
    public async Task Console_login_maps_invalid_credentials_to_unauthorized()
    {
        var iam = new FakeGatewayIamAuthClient { NextException = GatewayAuthException.Unauthorized("invalid-login") };
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/login",
            new ConsoleLoginRequest("admin", "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Console_refresh_forwards_refresh_token()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/refresh",
            new ConsoleRefreshRequest("refresh-token"));

        response.EnsureSuccessStatusCode();
        Assert.Equal(new ConsoleRefreshRequest("refresh-token"), iam.LastRefresh);
    }

    [Fact]
    public async Task Console_logout_forwards_bearer_and_session()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "access-token");

        var response = await client.PostAsJsonAsync("/api/console/v1/auth/logout", new ConsoleLogoutRequest("session-001"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("access-token", iam.LastLogoutBearerToken);
        Assert.Equal(new ConsoleLogoutRequest("session-001"), iam.LastLogout);
    }

    [Fact]
    public async Task Console_me_forwards_bearer()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "access-token");

        var response = await client.GetAsync("/api/console/v1/auth/me");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsolePrincipalResponse>();
        Assert.Equal("admin", body!.LoginName);
        Assert.Equal("access-token", iam.LastMeBearerToken);
    }

    [Fact]
    public async Task Console_me_requires_bearer()
    {
        var iam = new FakeGatewayIamAuthClient();
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().GetAsync("/api/console/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(iam.LastMeBearerToken);
    }

    [Fact]
    public async Task Console_auth_maps_iam_unavailable_to_service_unavailable()
    {
        var iam = new FakeGatewayIamAuthClient { NextException = GatewayAuthException.Unavailable("iam-unavailable") };
        await using var factory = CreateFactory(iam);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/console/v1/auth/login",
            new ConsoleLoginRequest("admin", "Admin123!"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeGatewayIamAuthClient iam) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGatewayIamAuthClient>();
            services.AddSingleton<IGatewayIamAuthClient>(iam);
        }));

    private sealed class FakeGatewayIamAuthClient : IGatewayIamAuthClient
    {
        private static readonly ConsolePrincipalResponse Principal = new(
            "user-admin",
            "user",
            "admin",
            "admin@nerv-iip.local",
            "org-001",
            "env-dev",
            1);

        public GatewayAuthException? NextException { get; init; }
        public ConsoleLoginRequest? LastLogin { get; private set; }
        public ConsoleRefreshRequest? LastRefresh { get; private set; }
        public ConsoleLogoutRequest? LastLogout { get; private set; }
        public string? LastLogoutBearerToken { get; private set; }
        public string? LastMeBearerToken { get; private set; }

        public Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken)
        {
            ThrowIfNeeded();
            LastLogin = request;
            return Task.FromResult(Session());
        }

        public Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken)
        {
            ThrowIfNeeded();
            LastRefresh = request;
            return Task.FromResult(Session());
        }

        public Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken)
        {
            ThrowIfNeeded();
            LastLogoutBearerToken = bearerToken;
            LastLogout = request;
            return Task.CompletedTask;
        }

        public Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken)
        {
            ThrowIfNeeded();
            LastMeBearerToken = bearerToken;
            return Task.FromResult(Principal);
        }

        private static ConsoleAuthResponse Session() =>
            new("access-token", "refresh-token", "session-001", DateTimeOffset.UtcNow.AddMinutes(15), Principal);

        private void ThrowIfNeeded()
        {
            if (NextException is not null)
            {
                throw NextException;
            }
        }
    }
}
```

- [ ] **步骤 2：运行 Gateway 认证测试并确认红灯状态**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayConsoleAuthTests
```

预期：失败，因为 `ConsoleLoginRequest`、`IGatewayIamAuthClient` 和 Console 认证端点尚不存在。

- [ ] **步骤 3：增加 Gateway 认证模型和异常类型**

新建 `ConsoleAuthModels.cs`：

```csharp
using System.Net;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed record ConsoleLoginRequest(string LoginName, string Password);
public sealed record ConsoleRefreshRequest(string RefreshToken);
public sealed record ConsoleLogoutRequest(string? SessionId);

public sealed record ConsolePrincipalResponse(
    string PrincipalId,
    string PrincipalType,
    string LoginName,
    string Email,
    string OrganizationId,
    string EnvironmentId,
    int PermissionVersion);

public sealed record ConsoleAuthResponse(
    string AccessToken,
    string RefreshToken,
    string SessionId,
    DateTimeOffset ExpiresAtUtc,
    ConsolePrincipalResponse Principal);

public interface IGatewayIamAuthClient
{
    Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken);
    Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken);
    Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken);
}

public sealed class GatewayAuthException(HttpStatusCode statusCode, string reason) : Exception(reason)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Reason { get; } = reason;

    public static GatewayAuthException Unauthorized(string reason) => new(HttpStatusCode.Unauthorized, reason);
    public static GatewayAuthException BadGateway(string reason) => new(HttpStatusCode.BadGateway, reason);
    public static GatewayAuthException Unavailable(string reason) => new(HttpStatusCode.ServiceUnavailable, reason);
}
```

- [ ] **步骤 4：增加 Gateway 使用的 IAM HTTP 客户端**

新建 `GatewayIamAuthClient.cs`：

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Nerv.IIP.PlatformGateway.Web.Application.Auth;

public sealed class HttpGatewayIamAuthClient(HttpClient httpClient) : IGatewayIamAuthClient
{
    public async Task<ConsoleAuthResponse> LoginAsync(ConsoleLoginRequest request, CancellationToken cancellationToken)
    {
        var auth = await SendJsonAsync<IamAuthResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, "/api/iam/v1/auth/login")
            {
                Content = JsonContent.Create(new IamLoginRequest(request.LoginName, request.Password))
            },
            cancellationToken);
        var principal = await GetMeAsync(auth.AccessToken, cancellationToken);
        return auth.ToConsole(principal);
    }

    public async Task<ConsoleAuthResponse> RefreshAsync(ConsoleRefreshRequest request, CancellationToken cancellationToken)
    {
        var auth = await SendJsonAsync<IamAuthResponse>(
            () => new HttpRequestMessage(HttpMethod.Post, "/api/iam/v1/auth/refresh")
            {
                Content = JsonContent.Create(new IamRefreshRequest(request.RefreshToken))
            },
            cancellationToken);
        var principal = await GetMeAsync(auth.AccessToken, cancellationToken);
        return auth.ToConsole(principal);
    }

    public async Task LogoutAsync(string bearerToken, ConsoleLogoutRequest request, CancellationToken cancellationToken)
    {
        await SendNoContentAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, "/api/iam/v1/auth/logout")
                {
                    Content = JsonContent.Create(new IamLogoutRequest(request.SessionId))
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                return message;
            },
            cancellationToken);
    }

    public async Task<ConsolePrincipalResponse> GetMeAsync(string bearerToken, CancellationToken cancellationToken)
    {
        var principal = await SendJsonAsync<IamMeResponse>(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Get, "/api/iam/v1/me");
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                return message;
            },
            cancellationToken);

        return new ConsolePrincipalResponse(
            principal.UserId,
            principal.PrincipalType,
            principal.LoginName,
            principal.Email,
            principal.OrganizationId,
            principal.EnvironmentId,
            principal.PermissionVersion);
    }

    private async Task<T> SendJsonAsync<T>(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(createRequest, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        return body is not null ? body : throw GatewayAuthException.BadGateway("iam-empty-response");
    }

    private async Task SendNoContentAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        using var _ = await SendAsync(createRequest, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(createRequest(), cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw GatewayAuthException.Unauthorized("unauthorized");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw GatewayAuthException.Unauthorized("forbidden");
            }

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw GatewayAuthException.BadGateway("iam-error");
            }

            return response;
        }
        catch (HttpRequestException)
        {
            throw GatewayAuthException.Unavailable("iam-unavailable");
        }
    }

    private sealed record IamLoginRequest(string LoginName, string Password);
    private sealed record IamRefreshRequest(string RefreshToken);
    private sealed record IamLogoutRequest(string? SessionId);
    private sealed record IamAuthResponse(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc)
    {
        public ConsoleAuthResponse ToConsole(ConsolePrincipalResponse principal) =>
            new(AccessToken, RefreshToken, SessionId, ExpiresAtUtc, principal);
    }
    private sealed record IamMeResponse(
        string UserId,
        string LoginName,
        string Email,
        string PrincipalType,
        string OrganizationId,
        string EnvironmentId,
        int PermissionVersion);
}
```

- [ ] **步骤 5：增加 Console 认证端点**

新建 `ConsoleAuthEndpoints.cs`：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.PlatformGateway.Web.Application.Auth;

namespace Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;

[HttpPost("/api/console/v1/auth/login")]
[AllowAnonymous]
public sealed class LoginConsoleUserEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleLoginRequest, ConsoleAuthResponse>
{
    public override async Task HandleAsync(ConsoleLoginRequest req, CancellationToken ct)
    {
        await ConsoleAuthEndpointResults.SendAsync(HttpContext, () => iam.LoginAsync(req, ct), ct);
    }
}

[HttpPost("/api/console/v1/auth/refresh")]
[AllowAnonymous]
public sealed class RefreshConsoleSessionEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleRefreshRequest, ConsoleAuthResponse>
{
    public override async Task HandleAsync(ConsoleRefreshRequest req, CancellationToken ct)
    {
        await ConsoleAuthEndpointResults.SendAsync(HttpContext, () => iam.RefreshAsync(req, ct), ct);
    }
}

[HttpPost("/api/console/v1/auth/logout")]
[AllowAnonymous]
public sealed class LogoutConsoleSessionEndpoint(IGatewayIamAuthClient iam) : Endpoint<ConsoleLogoutRequest>
{
    public override async Task HandleAsync(ConsoleLogoutRequest req, CancellationToken ct)
    {
        var bearerToken = ConsoleAuthEndpointResults.ReadBearerToken(HttpContext);
        if (bearerToken is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await ConsoleAuthEndpointResults.SendNoContentAsync(HttpContext, () => iam.LogoutAsync(bearerToken, req, ct), ct);
    }
}

[HttpGet("/api/console/v1/auth/me")]
[AllowAnonymous]
public sealed class GetConsolePrincipalEndpoint(IGatewayIamAuthClient iam) : EndpointWithoutRequest<ConsolePrincipalResponse>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        var bearerToken = ConsoleAuthEndpointResults.ReadBearerToken(HttpContext);
        if (bearerToken is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await ConsoleAuthEndpointResults.SendAsync(HttpContext, () => iam.GetMeAsync(bearerToken, ct), ct);
    }
}

internal static class ConsoleAuthEndpointResults
{
    public static string? ReadBearerToken(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? value["Bearer ".Length..].Trim()
            : null;
    }

    public static async Task SendAsync<T>(HttpContext context, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            await context.Response.WriteAsJsonAsync(await action(), cancellationToken);
        }
        catch (GatewayAuthException ex)
        {
            context.Response.StatusCode = (int)ex.StatusCode;
            await context.Response.WriteAsJsonAsync(
                new { title = ex.StatusCode.ToString(), detail = ex.Reason, status = (int)ex.StatusCode },
                cancellationToken);
        }
    }

    public static async Task SendNoContentAsync(HttpContext context, Func<Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (GatewayAuthException ex)
        {
            context.Response.StatusCode = (int)ex.StatusCode;
            await context.Response.WriteAsJsonAsync(
                new { title = ex.StatusCode.ToString(), detail = ex.Reason, status = (int)ex.StatusCode },
                cancellationToken);
        }
    }
}
```

- [ ] **步骤 6：注册 Gateway IAM 认证客户端和操作 ID**

在 `Program.cs` 中增加端点命名空间：

```csharp
using Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;
```

在现有 Gateway 客户端附近增加 HTTP 客户端注册：

```csharp
builder.Services.AddHttpClient<IGatewayIamAuthClient, HttpGatewayIamAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
```

在 FastEndpoints 名称生成器中增加：

```csharp
nameof(LoginConsoleUserEndpoint) => "loginConsoleUser",
nameof(RefreshConsoleSessionEndpoint) => "refreshConsoleSession",
nameof(LogoutConsoleSessionEndpoint) => "logoutConsoleSession",
nameof(GetConsolePrincipalEndpoint) => "getConsolePrincipal",
```

- [ ] **步骤 7：更新 OpenAPI 操作 ID 测试**

在 `GatewayOpenApiTests.cs` 中增加：

```csharp
var login = paths.GetProperty("/api/console/v1/auth/login");
Assert.Equal("loginConsoleUser", login.GetProperty("post").GetProperty("operationId").GetString());

var refresh = paths.GetProperty("/api/console/v1/auth/refresh");
Assert.Equal("refreshConsoleSession", refresh.GetProperty("post").GetProperty("operationId").GetString());

var logout = paths.GetProperty("/api/console/v1/auth/logout");
Assert.Equal("logoutConsoleSession", logout.GetProperty("post").GetProperty("operationId").GetString());

var me = paths.GetProperty("/api/console/v1/auth/me");
Assert.Equal("getConsolePrincipal", me.GetProperty("get").GetProperty("operationId").GetString());
AssertJsonResponseSchema(me.GetProperty("get"), "200", "NervIIPPlatformGatewayWebApplicationAuthConsolePrincipalResponse");
```

- [ ] **步骤 8：运行 Gateway 测试**

运行：

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
```

预期：通过。

## 任务 3：重新生成 Gateway API 客户端并增加认证传输层

**文件：**

- 修改：`frontend/packages/api-client/openapi/platform-gateway.v1.json`
- 修改生成内容：`frontend/packages/api-client/src/generated/**`
- 新建：`frontend/packages/api-client/src/auth.ts`
- 修改：`frontend/packages/api-client/src/index.ts`
- 修改：`frontend/packages/api-client/src/transport/client-config.ts`
- 修改：`frontend/packages/api-client/src/transport/client-config.test.ts`

- [ ] **步骤 1：后端认证端点就绪后导出 Gateway OpenAPI**

运行：

```powershell
pwsh scripts/export-gateway-openapi.ps1
```

预期：`frontend/packages/api-client/openapi/platform-gateway.v1.json` 包含四条新的 Console 认证路径及其操作 ID。

- [ ] **步骤 2：重新生成 api-client**

运行：

```powershell
pnpm -C frontend generate:api
```

预期：生成的 SDK 和 `@pinia/colada.gen.ts` 包含：

```text
loginConsoleUserMutationOptions
refreshConsoleSessionMutationOptions
logoutConsoleSessionMutationOptions
getConsolePrincipalQueryOptions
```

- [ ] **步骤 3：增加稳定的认证导出**

新建 `frontend/packages/api-client/src/auth.ts`：

```ts
export {
  getConsolePrincipalQueryOptions,
  loginConsoleUserMutationOptions,
  logoutConsoleSessionMutationOptions,
  refreshConsoleSessionMutationOptions,
} from './generated/@pinia/colada.gen'

export {
  getConsolePrincipal,
  loginConsoleUser,
  logoutConsoleSession,
  refreshConsoleSession,
} from './generated/sdk.gen'

import type {
  NervIipPlatformGatewayWebApplicationAuthConsoleAuthResponse,
  NervIipPlatformGatewayWebApplicationAuthConsoleLoginRequest,
  NervIipPlatformGatewayWebApplicationAuthConsoleLogoutRequest,
  NervIipPlatformGatewayWebApplicationAuthConsolePrincipalResponse,
  NervIipPlatformGatewayWebApplicationAuthConsoleRefreshRequest,
} from './generated/types.gen'

export type ConsoleAuthResponse =
  NervIipPlatformGatewayWebApplicationAuthConsoleAuthResponse
export type ConsoleLoginRequest =
  NervIipPlatformGatewayWebApplicationAuthConsoleLoginRequest
export type ConsoleLogoutRequest =
  NervIipPlatformGatewayWebApplicationAuthConsoleLogoutRequest
export type ConsolePrincipalResponse =
  NervIipPlatformGatewayWebApplicationAuthConsolePrincipalResponse
export type ConsoleRefreshRequest =
  NervIipPlatformGatewayWebApplicationAuthConsoleRefreshRequest
```

如果生成的类型名称不同，请使用 `frontend/packages/api-client/src/generated/types.gen.ts` 中的准确名称，并保留这些公开别名。

在 `frontend/packages/api-client/src/index.ts` 中增加：

```ts
export * from './auth'
```

- [ ] **步骤 4：编写失败的传输层认证测试**

在 `client-config.test.ts` 中增加：

```ts
import { client } from '../generated/client.gen'
import { configureApiClient } from './client-config'
```

然后增加测试：

```ts
describe('configureApiClient auth transport', () => {
  it('injects a bearer token from the configured provider', async () => {
    const requests: Request[] = []
    configureApiClient({
      accessTokenProvider: () => 'token-123',
      fetch: async (request) => {
        requests.push(request)
        return new Response(JSON.stringify({ ok: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    })

    await client.get({ url: '/secure-test' })

    expect(requests[0]!.headers.get('Authorization')).toBe('Bearer token-123')
  })

  it('does not send Authorization after the provider returns nothing', async () => {
    const requests: Request[] = []
    configureApiClient({
      accessTokenProvider: () => undefined,
      fetch: async (request) => {
        requests.push(request)
        return new Response(JSON.stringify({ ok: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      },
    })

    await client.get({ url: '/anonymous-test' })

    expect(requests[0]!.headers.has('Authorization')).toBe(false)
  })

  it('notifies once when a response is unauthorized', async () => {
    let unauthorizedCount = 0
    configureApiClient({
      accessTokenProvider: () => 'expired-token',
      onUnauthorized: () => {
        unauthorizedCount += 1
      },
      fetch: async () => new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401 }),
    })

    await client.get({ url: '/secure-test' })

    expect(unauthorizedCount).toBe(1)
  })
})
```

- [ ] **步骤 5：运行传输层测试并确认红灯状态**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test -- src/transport/client-config.test.ts
```

预期：失败，因为 `ConfigureApiClientOptions` 不支持 `accessTokenProvider`、`fetch` 或 `onUnauthorized`。

- [ ] **步骤 6：实现动态 bearer 认证传输层**

将 `client-config.ts` 替换为：

```ts
import { client } from '../generated/client.gen'
import { getApiBaseUrl } from './base-url'

export interface ConfigureApiClientOptions {
  accessTokenProvider?: () => string | undefined
  baseUrl?: string
  fetch?: typeof fetch
  headers?: HeadersInit
  onUnauthorized?: () => void
}

let requestInterceptorId: number | undefined
let responseInterceptorId: number | undefined

export function configureApiClient(options: ConfigureApiClientOptions = {}): void {
  client.setConfig({
    baseUrl: options.baseUrl ?? getApiBaseUrl(),
    fetch: options.fetch,
    headers: options.headers,
  })

  if (requestInterceptorId !== undefined) {
    client.interceptors.request.eject(requestInterceptorId)
  }
  if (responseInterceptorId !== undefined) {
    client.interceptors.response.eject(responseInterceptorId)
  }

  requestInterceptorId = client.interceptors.request.use((request) => {
    const accessToken = options.accessTokenProvider?.()
    if (accessToken) {
      request.headers.set('Authorization', `Bearer ${accessToken}`)
    } else {
      request.headers.delete('Authorization')
    }

    return request
  })

  responseInterceptorId = client.interceptors.response.use((response) => {
    if (response.status === 401) {
      options.onUnauthorized?.()
    }

    return response
  })
}
```

- [ ] **步骤 7：运行 api-client 测试**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
```

预期：通过。

## 任务 4：在 UI 包中初始化 shadcn-vue

**文件：**

- 新建/修改：`frontend/components.json`
- 修改：`frontend/package.json`
- 修改：`frontend/pnpm-lock.yaml`
- 修改：`frontend/vite.config.ts`
- 修改：`frontend/tsconfig.base.json`
- 修改：`frontend/packages/ui/package.json`
- 修改：`frontend/packages/ui/tsconfig.json`
- 修改/新建：`frontend/packages/ui/src/index.ts`
- 新建：`frontend/packages/ui/src/lib/utils.ts`
- 新建：`frontend/packages/ui/src/components/ui/**`
- 修改：`frontend/apps/console/src/assets/main.css`

- [ ] **步骤 1：初始化前检查 shadcn-vue 上下文**

运行：

```powershell
pnpm dlx shadcn-vue@latest info --json
pnpm dlx shadcn-vue@latest search -q field '@shadcn'
pnpm dlx shadcn-vue@latest init --help
pnpm dlx shadcn-vue@latest add --help
```

预期：初始化前，信息命令仍报告没有配置；搜索结果包含 `@shadcn/field`。

- [ ] **步骤 2：使用选定基线初始化 shadcn-vue**

在 `frontend` 中运行：

```powershell
pnpm dlx shadcn-vue@latest init --template vite --preset nova --base reka --icon-library lucide --base-color neutral --css-variables --no-src-dir --cwd .
```

提示输入别名时，使用包内路径：

```text
components: @nerv-iip/ui/components
utils: @nerv-iip/ui/lib/utils
ui: @nerv-iip/ui/components/ui
lib: @nerv-iip/ui/lib
```

预期：创建 `components.json`，并将 UI 代码指向 `frontend/packages/ui/src/components/ui`、工具指向 `frontend/packages/ui/src/lib/utils.ts`。如果 CLI 改为写入 `frontend/components/ui`，请先将生成文件移至 `frontend/packages/ui/src/components/ui` 并更新 `components.json`，再增加组件。

- [ ] **步骤 3：增加首批 shadcn-vue 组件**

在 `frontend` 中运行：

```powershell
pnpm dlx shadcn-vue@latest add button card field input alert badge separator skeleton dropdown-menu avatar sonner spinner --cwd .
```

预期：文件新增至 `frontend/packages/ui/src/components/ui/**` 下。

- [ ] **步骤 4：通过 `@nerv-iip/ui` 导出 shadcn-vue 组件**

将 `frontend/packages/ui/src/index.ts` 替换为：

```ts
export { cn } from './lib/utils'

export { Button, buttonVariants } from './components/ui/button'
export {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from './components/ui/card'
export {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSeparator,
  FieldSet,
  FieldTitle,
} from './components/ui/field'
export { Input } from './components/ui/input'
export { Alert, AlertDescription, AlertTitle } from './components/ui/alert'
export { Badge } from './components/ui/badge'
export { Separator } from './components/ui/separator'
export { Skeleton } from './components/ui/skeleton'
export {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './components/ui/dropdown-menu'
export { Avatar, AvatarFallback, AvatarImage } from './components/ui/avatar'
export { Toaster } from './components/ui/sonner'
export { Spinner } from './components/ui/spinner'
```

如果生成的组件导出名称不同，请检查其本地 `index.ts`，并继续将 `@nerv-iip/ui` 作为稳定的公开边界。

- [ ] **步骤 5：更新 TypeScript 和 Vite 别名**

在 `frontend/tsconfig.base.json` 中增加路径：

```json
"baseUrl": ".",
"paths": {
  "@nerv-iip/api-client": ["packages/api-client/src/index.ts"],
  "@nerv-iip/app-shell": ["packages/app-shell/src/index.ts"],
  "@nerv-iip/ui": ["packages/ui/src/index.ts"],
  "@nerv-iip/ui/*": ["packages/ui/src/*"]
}
```

在 `frontend/vite.config.ts` 和 `frontend/apps/console/vite.config.ts` 中均增加：

```ts
'@nerv-iip/ui/': fileURLToPath(new URL('./packages/ui/src/', import.meta.url)),
```

对于应用级配置，使用：

```ts
'@nerv-iip/ui/': fileURLToPath(new URL('../../packages/ui/src/', import.meta.url)),
```

- [ ] **步骤 6：安装并验证依赖**

运行：

```powershell
pnpm -C frontend install
pnpm -C frontend --filter @nerv-iip/ui typecheck
```

预期：锁文件得到更新；UI 包类型检查通过。

## 任务 5：增加 Console 认证状态存储、会话持久化和路由守卫

**文件：**

- 新建：`frontend/apps/console/src/api/auth.ts`
- 新建：`frontend/apps/console/src/stores/auth.ts`
- 新建：`frontend/apps/console/src/stores/auth.test.ts`
- 新建：`frontend/apps/console/src/router/guards/auth.ts`
- 新建：`frontend/apps/console/src/router/guards/auth.test.ts`
- 修改：`frontend/apps/console/src/router/index.ts`
- 修改：`frontend/apps/console/src/main.ts`

- [ ] **步骤 1：增加认证 API 包装层**

新建 `api/auth.ts`：

```ts
import {
  getConsolePrincipal,
  loginConsoleUser,
  logoutConsoleSession,
  refreshConsoleSession,
  type ConsoleAuthResponse,
  type ConsoleLoginRequest,
  type ConsoleLogoutRequest,
  type ConsolePrincipalResponse,
  type ConsoleRefreshRequest,
} from '@nerv-iip/api-client'

export class ConsoleAuthError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
  }
}

function assertData<T>(result: { data?: T; error?: unknown; response?: Response }, fallback: string): T {
  if (result.data) {
    return result.data
  }

  const status = result.response?.status
  throw new ConsoleAuthError(status === 401 ? 'Invalid credentials or expired session.' : fallback, status)
}

export async function loginConsole(request: ConsoleLoginRequest): Promise<ConsoleAuthResponse> {
  return assertData(
    await loginConsoleUser({ body: request }),
    'Unable to connect to the authentication service.',
  )
}

export async function refreshConsole(request: ConsoleRefreshRequest): Promise<ConsoleAuthResponse> {
  return assertData(await refreshConsoleSession({ body: request }), 'Unable to refresh the session.')
}

export async function logoutConsole(accessToken: string, request: ConsoleLogoutRequest): Promise<void> {
  await logoutConsoleSession({
    body: request,
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
  })
}

export async function getConsoleMe(accessToken: string): Promise<ConsolePrincipalResponse> {
  return assertData(
    await getConsolePrincipal({
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
    }),
    'Unable to load the current principal.',
  )
}
```

- [ ] **步骤 2：编写认证存储测试**

新建 `stores/auth.test.ts`，并模拟 `@/api/auth`：

```ts
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from './auth'

const api = vi.hoisted(() => ({
  getConsoleMe: vi.fn(),
  loginConsole: vi.fn(),
  logoutConsole: vi.fn(),
  refreshConsole: vi.fn(),
}))

vi.mock('@/api/auth', () => api)

const principal = {
  principalId: 'user-admin',
  principalType: 'user',
  loginName: 'admin',
  email: 'admin@nerv-iip.local',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-001',
  expiresAtUtc: '2026-05-18T08:00:00Z',
  principal,
}

describe('auth store', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
    vi.resetAllMocks()
  })

  it('stores session after login', async () => {
    api.loginConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.login('admin', 'Admin123!')

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('access-token')
    expect(auth.principal?.loginName).toBe('admin')
    expect(localStorage.getItem('nerv-iip.console.auth')).toContain('refresh-token')
  })

  it('clears state after login failure', async () => {
    api.loginConsole.mockRejectedValue(new Error('Invalid credentials.'))
    const auth = useAuthStore()

    await expect(auth.login('admin', 'wrong')).rejects.toThrow('Invalid credentials.')

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('restores a saved refresh token', async () => {
    localStorage.setItem('nerv-iip.console.auth', JSON.stringify({ refreshToken: 'refresh-token', sessionId: 'session-001', principal }))
    api.refreshConsole.mockResolvedValue(session)
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(api.refreshConsole).toHaveBeenCalledWith({ refreshToken: 'refresh-token' })
    expect(auth.isAuthenticated).toBe(true)
  })

  it('clears storage when restore fails', async () => {
    localStorage.setItem('nerv-iip.console.auth', JSON.stringify({ refreshToken: 'bad-token', sessionId: 'session-001', principal }))
    api.refreshConsole.mockRejectedValue(new Error('expired'))
    const auth = useAuthStore()

    await auth.restoreSession()

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })

  it('clears local state even when logout request fails', async () => {
    api.loginConsole.mockResolvedValue(session)
    api.logoutConsole.mockRejectedValue(new Error('network'))
    const auth = useAuthStore()
    await auth.login('admin', 'Admin123!')

    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(localStorage.getItem('nerv-iip.console.auth')).toBeNull()
  })
})
```

- [ ] **步骤 3：运行认证存储测试并确认红灯状态**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- src/stores/auth.test.ts
```

预期：失败，因为 `stores/auth.ts` 不存在。

- [ ] **步骤 4：实现组合式 Pinia 认证存储**

新建 `stores/auth.ts`：

```ts
import {
  getConsoleMe,
  loginConsole,
  logoutConsole,
  refreshConsole,
  type ConsoleAuthError,
} from '@/api/auth'
import type { ConsoleAuthResponse, ConsolePrincipalResponse } from '@nerv-iip/api-client'
import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

const STORAGE_KEY = 'nerv-iip.console.auth'

interface StoredSession {
  principal?: ConsolePrincipalResponse
  refreshToken: string
  sessionId: string
}

export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string>()
  const refreshToken = ref<string>()
  const sessionId = ref<string>()
  const expiresAtUtc = ref<string>()
  const principal = ref<ConsolePrincipalResponse>()
  const restoreStatus = ref<'idle' | 'restoring' | 'restored' | 'failed'>('idle')
  const authError = ref<string>()

  const isAuthenticated = computed(() => Boolean(accessToken.value && principal.value))
  const isRestoring = computed(() => restoreStatus.value === 'restoring')
  const displayName = computed(() => principal.value?.loginName ?? 'Unknown user')

  async function login(loginName: string, password: string) {
    authError.value = undefined
    try {
      applySession(await loginConsole({ loginName, password }))
    } catch (error) {
      clearSession('login-failed')
      authError.value = error instanceof Error ? error.message : 'Unable to sign in.'
      throw error
    }
  }

  async function restoreSession() {
    if (restoreStatus.value === 'restoring') {
      return
    }

    const stored = readStoredSession()
    if (!stored) {
      restoreStatus.value = 'failed'
      return
    }

    restoreStatus.value = 'restoring'
    try {
      applySession(await refreshConsole({ refreshToken: stored.refreshToken }))
      restoreStatus.value = 'restored'
    } catch {
      clearSession('restore-failed')
      restoreStatus.value = 'failed'
    }
  }

  async function refreshSession() {
    if (!refreshToken.value) {
      clearSession('missing-refresh-token')
      return
    }

    applySession(await refreshConsole({ refreshToken: refreshToken.value }))
  }

  async function loadPrincipal() {
    if (!accessToken.value) {
      clearSession('missing-access-token')
      return
    }

    principal.value = await getConsoleMe(accessToken.value)
    persistSession()
  }

  async function logout() {
    const token = accessToken.value
    const currentSessionId = sessionId.value
    clearSession('logout')
    if (token) {
      await logoutConsole(token, { sessionId: currentSessionId }).catch(() => undefined)
    }
  }

  function clearSession(_reason: string) {
    accessToken.value = undefined
    refreshToken.value = undefined
    sessionId.value = undefined
    expiresAtUtc.value = undefined
    principal.value = undefined
    localStorage.removeItem(STORAGE_KEY)
  }

  function applySession(session: ConsoleAuthResponse) {
    accessToken.value = session.accessToken
    refreshToken.value = session.refreshToken
    sessionId.value = session.sessionId
    expiresAtUtc.value = session.expiresAtUtc
    principal.value = session.principal
    persistSession()
  }

  function persistSession() {
    if (!refreshToken.value || !sessionId.value) {
      return
    }

    const stored: StoredSession = {
      principal: principal.value,
      refreshToken: refreshToken.value,
      sessionId: sessionId.value,
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
  }

  function readStoredSession(): StoredSession | undefined {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return undefined
    }

    try {
      const parsed = JSON.parse(raw) as Partial<StoredSession>
      return parsed.refreshToken && parsed.sessionId
        ? {
            principal: parsed.principal,
            refreshToken: parsed.refreshToken,
            sessionId: parsed.sessionId,
          }
        : undefined
    } catch {
      localStorage.removeItem(STORAGE_KEY)
      return undefined
    }
  }

  return {
    accessToken,
    authError,
    clearSession,
    displayName,
    expiresAtUtc,
    isAuthenticated,
    isRestoring,
    loadPrincipal,
    login,
    logout,
    principal,
    refreshSession,
    refreshToken,
    restoreSession,
    restoreStatus,
    sessionId,
  }
})
```

如果代码检查报告 `ConsoleAuthError` 导入未使用，请将其删除。

- [ ] **步骤 5：增加路由守卫测试**

新建 `router/guards/auth.test.ts`：

```ts
import { createMemoryHistory, createRouter } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installAuthGuard } from './auth'
import { useAuthStore } from '@/stores/auth'

describe('auth route guard', () => {
  beforeEach(() => {
    localStorage.clear()
    setActivePinia(createPinia())
  })

  function createGuardedRouter() {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div />' }, meta: { requiresAuth: true } },
        { path: '/login', component: { template: '<div />' }, meta: { guestOnly: true } },
      ],
    })
    installAuthGuard(router)
    return router
  }

  it('redirects unauthenticated users to login', async () => {
    const router = createGuardedRouter()

    await router.push('/')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query.redirect).toBe('/')
  })

  it('redirects authenticated users away from login', async () => {
    const router = createGuardedRouter()
    const auth = useAuthStore()
    auth.$patch({
      accessToken: 'access-token',
      principal: {
        principalId: 'user-admin',
        principalType: 'user',
        loginName: 'admin',
        email: 'admin@nerv-iip.local',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionVersion: 1,
      },
    })

    await router.push('/login')

    expect(router.currentRoute.value.path).toBe('/')
  })
})
```

- [ ] **步骤 6：实现认证守卫**

新建 `router/guards/auth.ts`：

```ts
import { useAuthStore } from '@/stores/auth'
import type { Router } from 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    guestOnly?: boolean
    requiresAuth?: boolean
    title?: string
  }
}

export function installAuthGuard(router: Router) {
  router.beforeEach(async (to) => {
    const auth = useAuthStore()

    if (auth.restoreStatus === 'idle') {
      await auth.restoreSession()
    }

    if (to.meta.requiresAuth && !auth.isAuthenticated) {
      return {
        path: '/login',
        query: {
          redirect: to.fullPath,
        },
      }
    }

    if (to.meta.guestOnly && auth.isAuthenticated) {
      const redirect = typeof to.query.redirect === 'string' ? to.query.redirect : '/'
      return redirect
    }

    return true
  })
}
```

在 `router/index.ts` 中，于路由器创建后增加：

```ts
import { installAuthGuard } from './guards/auth'

installAuthGuard(router)
```

- [ ] **步骤 7：为 api-client 配置认证提供程序**

在 `main.ts` 中，将前面的 `configureApiClient()` 调用替换为：

```ts
const app = createApp(App)
const pinia = createPinia()

app.use(pinia)

const auth = useAuthStore()
configureApiClient({
  accessTokenProvider: () => auth.accessToken,
  onUnauthorized: () => {
    auth.clearSession('api-unauthorized')
    void router.push({ path: '/login', query: { redirect: router.currentRoute.value.fullPath } })
  },
})
```

移动现有 `const app` 和 `const pinia` 声明，以便在调用 `useAuthStore()` 前安装 Pinia。继续将现有 Pinia Colada 安装保留在 `app.use(pinia)` 之后。

- [ ] **步骤 8：运行存储和守卫测试**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- src/stores/auth.test.ts src/router/guards/auth.test.ts
```

预期：通过。

## 任务 6：构建登录 UI 和已认证应用外壳

**文件：**

- 新建：`frontend/apps/console/src/components/auth/LoginForm.vue`
- 新建：`frontend/apps/console/src/components/auth/LoginForm.test.ts`
- 新建：`frontend/apps/console/src/pages/login.vue`
- 修改：`frontend/apps/console/src/pages/index.vue`
- 修改：`frontend/apps/console/src/pages/operations/[operationTaskId].vue`
- 修改：`frontend/apps/console/src/layouts/DefaultLayout.vue`
- 修改：`frontend/packages/app-shell/src/AppShell.vue`
- 修改：`frontend/packages/app-shell/src/index.ts`
- 修改：`frontend/apps/console/src/App.test.ts`

- [ ] **步骤 1：编写 LoginForm 测试**

新建 `LoginForm.test.ts`：

```ts
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import LoginForm from './LoginForm.vue'

describe('LoginForm', () => {
  it('emits credentials on submit', async () => {
    const wrapper = mount(LoginForm)

    await wrapper.get('input[name="loginName"]').setValue('admin')
    await wrapper.get('input[name="password"]').setValue('Admin123!')
    await wrapper.get('form').trigger('submit.prevent')

    expect(wrapper.emitted('submit')?.[0]).toEqual([{ loginName: 'admin', password: 'Admin123!' }])
  })

  it('disables controls while pending', () => {
    const wrapper = mount(LoginForm, { props: { pending: true } })

    expect(wrapper.get('input[name="loginName"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('renders inline error text', () => {
    const wrapper = mount(LoginForm, { props: { error: 'Invalid credentials.' } })

    expect(wrapper.text()).toContain('Invalid credentials.')
    expect(wrapper.get('input[name="loginName"]').attributes('aria-invalid')).toBe('true')
  })
})
```

- [ ] **步骤 2：使用 shadcn-vue 组件实现 LoginForm**

新建 `LoginForm.vue`：

```vue
<script setup lang="ts">
import {
  Alert,
  AlertDescription,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  Spinner,
} from '@nerv-iip/ui'
import { LogInIcon } from 'lucide-vue-next'
import { reactive } from 'vue'

withDefaults(
  defineProps<{
    error?: string
    pending?: boolean
  }>(),
  {
    error: undefined,
    pending: false,
  },
)

const emit = defineEmits<{
  submit: [{ loginName: string; password: string }]
}>()

const form = reactive({
  loginName: '',
  password: '',
})

function submit() {
  emit('submit', {
    loginName: form.loginName.trim(),
    password: form.password,
  })
}
</script>

<template>
  <Card class="mx-auto w-full max-w-md">
    <CardHeader>
      <CardTitle>Sign in to Nerv-IIP</CardTitle>
      <CardDescription>Use your platform administrator account.</CardDescription>
    </CardHeader>
    <form @submit.prevent="submit">
      <CardContent class="flex flex-col gap-4">
        <Alert v-if="error" variant="destructive">
          <AlertDescription>{{ error }}</AlertDescription>
        </Alert>

        <FieldGroup>
          <Field :data-invalid="Boolean(error) || undefined" :data-disabled="pending || undefined">
            <FieldLabel for="login-name">Login name</FieldLabel>
            <Input
              id="login-name"
              v-model="form.loginName"
              :aria-invalid="Boolean(error)"
              autocomplete="username"
              :disabled="pending"
              name="loginName"
              required
            />
            <FieldDescription>Seeded local admin uses admin.</FieldDescription>
          </Field>

          <Field :data-invalid="Boolean(error) || undefined" :data-disabled="pending || undefined">
            <FieldLabel for="password">Password</FieldLabel>
            <Input
              id="password"
              v-model="form.password"
              :aria-invalid="Boolean(error)"
              autocomplete="current-password"
              :disabled="pending"
              name="password"
              required
              type="password"
            />
          </Field>
        </FieldGroup>
      </CardContent>
      <CardFooter>
        <Button class="w-full" :disabled="pending" type="submit">
          <Spinner v-if="pending" data-icon="inline-start" />
          <LogInIcon v-else data-icon="inline-start" />
          Sign in
        </Button>
      </CardFooter>
    </form>
  </Card>
</template>
```

- [ ] **步骤 3：增加登录路由**

新建 `pages/login.vue`：

```vue
<script setup lang="ts">
import LoginForm from '@/components/auth/LoginForm.vue'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    guestOnly: true,
    title: 'Sign in',
  },
})

const auth = useAuthStore()
const { authError } = storeToRefs(auth)
const route = useRoute('/login')
const router = useRouter()
const pending = ref(false)
const redirectPath = computed(() => (typeof route.query.redirect === 'string' ? route.query.redirect : '/'))

async function submit(credentials: { loginName: string; password: string }) {
  pending.value = true
  try {
    await auth.login(credentials.loginName, credentials.password)
    await router.push(redirectPath.value)
  } finally {
    pending.value = false
  }
}
</script>

<template>
  <main class="login-page">
    <section class="login-page__intro" aria-labelledby="login-title">
      <p class="login-page__eyebrow">Control plane</p>
      <h1 id="login-title">Nerv-IIP Console</h1>
      <p>Authenticate once, then manage application instances and operation tasks through the Gateway.</p>
    </section>
    <LoginForm :error="authError" :pending="pending" @submit="submit" />
  </main>
</template>

<style scoped>
.login-page {
  align-items: center;
  background: hsl(var(--background));
  color: hsl(var(--foreground));
  display: grid;
  gap: 2rem;
  grid-template-columns: minmax(0, 1fr) minmax(20rem, 28rem);
  min-height: 100vh;
  padding: 2rem;
}

.login-page__intro {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 42rem;
}

.login-page__intro h1,
.login-page__intro p {
  margin: 0;
}

.login-page__intro h1 {
  font-size: clamp(2rem, 5vw, 4rem);
  line-height: 1;
}

.login-page__intro p {
  color: hsl(var(--muted-foreground));
  font-size: 1rem;
  line-height: 1.6;
}

.login-page__eyebrow {
  color: hsl(var(--primary));
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0;
  text-transform: uppercase;
}

@media (max-width: 820px) {
  .login-page {
    grid-template-columns: 1fr;
    padding: 1rem;
  }
}
</style>
```

- [ ] **步骤 4：将现有页面标记为受保护页面**

在 `pages/index.vue` 和 `pages/operations/[operationTaskId].vue` 中增加：

```ts
definePage({
  meta: {
    requiresAuth: true,
    title: 'Instances',
  },
})
```

对于操作详情，使用标题 `Operation task`。

- [ ] **步骤 5：为 AppShell 增加用户菜单和注销命令**

将 `packages/app-shell/src/AppShell.vue` 替换为接受可选用户参数的应用外壳：

```vue
<script setup lang="ts">
import {
  Avatar,
  AvatarFallback,
  Button,
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@nerv-iip/ui'
import { LogOutIcon } from 'lucide-vue-next'

interface NavItem {
  href: string
  label: string
}

defineProps<{
  navItems: NavItem[]
  title: string
  user?: {
    email?: string
    loginName: string
  }
}>()

const emit = defineEmits<{
  signOut: []
}>()
</script>

<template>
  <div class="app-shell">
    <aside class="app-shell__sidebar">
      <a class="app-shell__brand" href="/">
        <span class="app-shell__brand-mark">N</span>
        <span class="app-shell__brand-text">{{ title }}</span>
      </a>

      <nav class="app-shell__nav" aria-label="Primary navigation">
        <a v-for="item in navItems" :key="item.href" class="app-shell__nav-link" :href="item.href">
          {{ item.label }}
        </a>
      </nav>
    </aside>

    <div class="app-shell__workspace">
      <header class="app-shell__topbar">
        <DropdownMenu v-if="user">
          <DropdownMenuTrigger as-child>
            <Button variant="ghost">
              <Avatar>
                <AvatarFallback>{{ user.loginName.slice(0, 2).toUpperCase() }}</AvatarFallback>
              </Avatar>
              {{ user.loginName }}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuLabel>
              <span>{{ user.loginName }}</span>
              <span class="app-shell__user-email">{{ user.email }}</span>
            </DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuGroup>
              <DropdownMenuItem @select="emit('signOut')">
                <LogOutIcon />
                Sign out
              </DropdownMenuItem>
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      </header>

      <main class="app-shell__main">
        <slot />
      </main>
    </div>
  </div>
</template>

<style scoped>
.app-shell {
  background: hsl(var(--background));
  color: hsl(var(--foreground));
  display: grid;
  grid-template-columns: 17rem minmax(0, 1fr);
  min-height: 100vh;
}

.app-shell__sidebar {
  background: hsl(var(--sidebar));
  border-right: 1px solid hsl(var(--sidebar-border));
  color: hsl(var(--sidebar-foreground));
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  padding: 1.25rem;
}

.app-shell__brand,
.app-shell__nav-link {
  color: inherit;
  text-decoration: none;
}

.app-shell__brand {
  align-items: center;
  display: flex;
  gap: 0.75rem;
  min-width: 0;
}

.app-shell__brand-mark {
  align-items: center;
  background: hsl(var(--primary));
  border-radius: var(--radius-sm);
  color: hsl(var(--primary-foreground));
  display: inline-flex;
  flex: 0 0 auto;
  font-weight: 800;
  justify-content: center;
  line-height: 1;
  min-height: 2.25rem;
  min-width: 2.25rem;
}

.app-shell__brand-text {
  font-weight: 800;
  letter-spacing: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-shell__nav {
  display: grid;
  gap: 0.35rem;
}

.app-shell__nav-link {
  border-radius: var(--radius-sm);
  color: hsl(var(--sidebar-foreground) / 0.78);
  display: block;
  font-size: 0.925rem;
  font-weight: 650;
  line-height: 1.35;
  padding: 0.65rem 0.75rem;
}

.app-shell__nav-link:hover,
.app-shell__nav-link:focus-visible {
  background: hsl(var(--sidebar-accent));
  color: hsl(var(--sidebar-accent-foreground));
  outline: none;
}

.app-shell__workspace {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  min-width: 0;
}

.app-shell__topbar {
  align-items: center;
  border-bottom: 1px solid hsl(var(--border));
  display: flex;
  justify-content: flex-end;
  min-height: 4rem;
  padding: 0.75rem 1.5rem;
}

.app-shell__main {
  min-width: 0;
  padding: 1.5rem;
}

.app-shell__user-email {
  color: hsl(var(--muted-foreground));
  display: block;
  font-size: 0.75rem;
  margin-top: 0.15rem;
}

@media (max-width: 760px) {
  .app-shell {
    grid-template-columns: 1fr;
  }

  .app-shell__sidebar {
    border-bottom: 1px solid hsl(var(--sidebar-border));
    border-right: 0;
    gap: 1rem;
    padding: 1rem;
  }

  .app-shell__nav {
    display: flex;
    gap: 0.5rem;
    overflow-x: auto;
    padding-bottom: 0.15rem;
  }

  .app-shell__nav-link {
    flex: 0 0 auto;
    white-space: nowrap;
  }

  .app-shell__topbar,
  .app-shell__main {
    padding: 1rem;
  }
}
</style>
```

- [ ] **步骤 6：将 DefaultLayout 接入认证存储**

将 `DefaultLayout.vue` 脚本替换为：

```vue
<script setup lang="ts">
import { AppShell } from '@nerv-iip/app-shell'
import { useAuthStore } from '@/stores/auth'
import { storeToRefs } from 'pinia'
import { useRouter } from 'vue-router'

const navItems = [{ label: 'Instances', href: '/' }]
const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const router = useRouter()

async function signOut() {
  await auth.logout()
  await router.push('/login')
}
</script>
```

将模板替换为：

```vue
<template>
  <AppShell
    title="Nerv-IIP"
    :nav-items="navItems"
    :user="principal ? { loginName: principal.loginName, email: principal.email } : undefined"
    @sign-out="signOut"
  >
    <slot />
  </AppShell>
</template>
```

- [ ] **步骤 7：安装 Toaster**

在 `App.vue` 中使用 shadcn-vue Toaster：

```vue
<script setup lang="ts">
import { Toaster } from '@nerv-iip/ui'
</script>

<template>
  <RouterView />
  <Toaster />
</template>
```

- [ ] **步骤 8：运行前端组件测试**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- src/components/auth/LoginForm.test.ts src/App.test.ts
```

预期：通过。

## 任务 7：将现有 Console 组件迁移至 shadcn-vue，并删除旧 UI 基础组件

**文件：**

- 修改：`frontend/apps/console/src/components/console/InstanceTable.vue`
- 修改：`frontend/apps/console/src/components/console/InstanceDetailPanel.vue`
- 修改：`frontend/apps/console/src/components/console/OperationTimeline.vue`
- 修改：`frontend/apps/console/src/pages/index.vue`
- 修改：`frontend/apps/console/src/pages/operations/[operationTaskId].vue`
- 删除：`frontend/packages/ui/src/UiBadge.vue`
- 删除：`frontend/packages/ui/src/UiButton.vue`
- 删除：`frontend/packages/ui/src/UiPanel.vue`
- 修改：`frontend/packages/ui/src/index.ts`
- 修改：`frontend/apps/console/src/pages/index.test.ts`

- [ ] **步骤 1：替换 Console 组件中的旧导入**

在 `InstanceTable.vue` 中，将以下内容：

```ts
import { UiBadge, UiButton } from '@nerv-iip/ui'
```

替换为：

```ts
import { Badge, Button } from '@nerv-iip/ui'
```

将 `<UiBadge>` 替换为：

```vue
<Badge variant="secondary">
  {{ instance.reportedStatus ?? 'unknown' }}
</Badge>
```

将 `<UiButton>` 替换为：

```vue
<Button
  :disabled="restartPending || !instance.instanceKey"
  variant="outline"
  @click="restartInstance(instance)"
>
  Restart
</Button>
```

在 `InstanceDetailPanel.vue` 和 `OperationTimeline.vue` 中，将 `UiBadge` 导入和标签替换为 `Badge`。

- [ ] **步骤 2：替换自定义加载和错误提示块**

在 `pages/index.vue` 中导入：

```ts
import { Alert, AlertDescription, Skeleton } from '@nerv-iip/ui'
```

将加载/错误段落替换为：

```vue
<Skeleton v-if="listPending" class="h-12 w-full" />
<Alert v-if="listError" variant="destructive">
  <AlertDescription>{{ listError.message }}</AlertDescription>
</Alert>
<Alert v-if="restartError" variant="destructive">
  <AlertDescription>{{ restartError.message }}</AlertDescription>
</Alert>
```

在 `pages/operations/[operationTaskId].vue` 中，使用 `Alert` 展示 `operationError`。

- [ ] **步骤 3：删除旧基础组件的导出和文件**

运行：

```powershell
rg -n "UiButton|UiPanel|UiBadge" frontend
```

删除前预期：只剩 `frontend/packages/ui/src/index.ts` 和旧基础组件文件仍有匹配项。

删除：

```text
frontend/packages/ui/src/UiBadge.vue
frontend/packages/ui/src/UiButton.vue
frontend/packages/ui/src/UiPanel.vue
```

确保 `frontend/packages/ui/src/index.ts` 不再导出 `UiBadge`、`UiButton` 或 `UiPanel`。

- [ ] **步骤 4：针对认证守卫和 shadcn 标记更新页面测试**

在 `pages/index.test.ts` 中，模拟认证存储或设置路由元数据行为，使页面可以挂载而不发生重定向。在挂载前增加 Pinia 认证状态：

```ts
import { useAuthStore } from '@/stores/auth'
```

在 `mountPage` 内的 `createPinia()` 之后增加：

```ts
const pinia = createPinia()
setActivePinia(pinia)
const auth = useAuthStore()
auth.$patch({
  accessToken: 'access-token',
  principal: {
    principalId: 'user-admin',
    principalType: 'user',
    loginName: 'admin',
    email: 'admin@nerv-iip.local',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
  },
})
```

在全局插件数组中使用 `pinia`。

- [ ] **步骤 5：搜索旧基础组件并运行前端测试**

运行：

```powershell
rg -n "UiButton|UiPanel|UiBadge" frontend
pnpm -C frontend --filter @nerv-iip/console test
pnpm -C frontend --filter @nerv-iip/ui typecheck
```

预期：`rg` 不返回匹配项；测试和类型检查通过。

## 任务 8：文档、浏览器验证和最终门禁

**文件：**

- 修改：`README.md`
- 修改：`docs/architecture/frontend-design-system-planning.md`
- 修改：`docs/architecture/frontend-structure.md`
- 修改：`docs/architecture/iam-authentication-baseline.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/superpowers/plans/2026-05-18-console-auth-shadcn-baseline.md`

- [ ] **步骤 1：更新 README 中过时的工作树表述**

在 `README.md` 中，将以下内容：

```markdown
- 当前工作树：`codex/iam-persistent-auth-foundation`，当前阶段为 IAM Persistent Auth Foundation。
```

替换为：

```markdown
- 当前主线：`main` 已合入 IAM Persistent Auth Foundation、Gateway-wide permission enforcement、pnpm 11.1.2 基线和 Console Auth + shadcn-vue 设计规格。
```

- [ ] **步骤 2：更新 schema 目录中过时的 Gateway 状态**

在 `docs/architecture/database-schema-catalog.md` 中，将说明 Gateway 全面权限强制尚未接入的句子替换为：

```markdown
Gateway-wide permission enforcement 已覆盖现有 Console API；Gateway 转发 bearer token 与 permission/context 到 IAM internal authorization check endpoint，不直接读取 IAM schema。
```

- [ ] **步骤 3：更新前端设计系统规划**

在 `docs/architecture/frontend-design-system-planning.md` 末尾追加：

```markdown
## Selected Baseline

Console Auth + shadcn-vue Baseline 选择 official shadcn-vue registry、`nova` preset、Vite template、Reka base components 和 semantic token 体系。组件源码归属 `frontend/packages/ui`，Console 应用通过 `@nerv-iip/ui` 稳定导出消费组件。旧 `UiButton`、`UiPanel` 和 `UiBadge` primitives 在迁移完成后删除，不再作为并行设计系统维护。
```

- [ ] **步骤 4：更新前端结构**

在 `docs/architecture/frontend-structure.md` 的状态/请求分层下增加：

```markdown
### Console Auth

Console 登录闭环通过 PlatformGateway Console Auth facade 调用 IAM。`stores/auth.ts` 只管理客户端会话状态，`api-client` 继续由 Gateway OpenAPI 生成 SDK 与 Pinia Colada options。路由守卫放在 `src/router/guards/auth.ts`，登录页和登录表单放在 `src/pages/login.vue` 与 `src/components/auth/LoginForm.vue`。
```

- [ ] **步骤 5：更新 IAM 基线和实施就绪状态**

在 `docs/architecture/iam-authentication-baseline.md` 的当前实施状态中增加：

```markdown
Console login UI now consumes IAM through PlatformGateway Console Auth facade. The browser keeps a single Gateway API base URL; Gateway forwards login, refresh, logout and current-principal requests to IAM without owning identity facts.
```

在 `docs/architecture/implementation-readiness.md` 中，于 Gateway 权限强制之后增加新的当前结论：

```markdown
Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线；完整用户/角色/会话管理、OAuth/OIDC、SSO、MFA 和 ABAC 仍属于后续阶段。
```

- [ ] **步骤 6：运行后端和前端质量门禁**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
dotnet test backend/Nerv.IIP.sln --no-restore
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
pwsh scripts/check-script-governance.ps1
git diff --check
```

预期：每条命令都以 `0` 退出；`check-script-governance` 输出 `Script governance check passed.`。

- [ ] **步骤 7：浏览器验证**

启动本地前端开发服务器：

```powershell
pnpm -C frontend --filter @nerv-iip/console dev
```

使用现有本地验证入口启动 Console 认证所需的后端栈：

```powershell
pwsh scripts/verify-third-slice-console.ps1 -UsePostgres
```

如果验证脚本在完成检查后退出，而没有保持服务运行，请通过现有 AppHost 或脚本使用的服务专用运行命令启动 Gateway/IAM/AppHub/Ops 服务，然后打开：

```text
http://127.0.0.1:5173/login
```

使用 Browser/Playwright 验证：

1. 1440x900 尺寸下的桌面端登录页。
2. 390x844 尺寸下的移动端登录页。
3. 凭据无效时显示行内错误。
4. 使用有效的初始管理员登录后重定向到 `/`。
5. 实例列表请求携带 bearer 认证信息并正常渲染。
6. 注销后返回 `/login`。

预期：截图显示 shadcn-vue 样式，无文字重叠，焦点状态清晰可见，且桌面端和移动端布局均可用。

- [ ] **步骤 8：提交实施变更**

运行：

```powershell
git status --short
git add backend/services/Iam/src/Nerv.IIP.Iam.Web
git add backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests
git add backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
git add backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests
git add frontend
git add README.md
git add docs/architecture/frontend-design-system-planning.md
git add docs/architecture/frontend-structure.md
git add docs/architecture/iam-authentication-baseline.md
git add docs/architecture/implementation-readiness.md
git add docs/architecture/database-schema-catalog.md
git commit -m "feat: add console auth shadcn baseline"
```

预期：所有验证命令通过后，提交成功。

## 执行顺序

1. 首先执行任务 1，因为 Gateway facade 需要 IAM `/me` 返回 Console 主体上下文。
2. 任务 2 增加面向浏览器的 Gateway 认证端点和稳定的 OpenAPI 操作 ID。
3. 任务 3 重新生成 api-client，并保留 Pinia Colada 集成。
4. 任务 4 在 UI 代码依赖 shadcn-vue 组件之前完成其初始化。
5. 任务 5 增加会话状态和路由保护。
6. 任务 6 增加登录 UI 和已认证外壳行为。
7. 任务 7 迁移现有可见 Console 组件并删除旧基础组件。
8. 任务 8 更新持久文档并运行最终验证。

## 自我审核

规格覆盖：

1. Gateway 认证 facade 由任务 2 覆盖。
2. 在任务 1 和任务 2 中，IAM 继续作为身份/会话事实的所有者。
3. 任务 3 保留生成的 api-client 和 Pinia Colada 集成。
4. 任务 4 覆盖 shadcn-vue 官方组件注册表和 `nova` 基线。
5. 任务 5 和任务 6 覆盖登录 UI、认证存储、启动恢复、bearer 注入、路由守卫和注销。
6. 任务 7 覆盖旧 UI 基础组件删除。
7. 任务 8 覆盖文档残留清理。
8. `packages/auth` 的提取仍只属于未来工作，本计划有意不予实施。

占位符扫描：

1. 没有步骤使用占位表述或延期实施标记。
2. 生成文件通过准确的 CLI 命令和明确的检查要求处理。
3. 手工代码变更步骤均包含具体代码块。

类型一致性：

1. `ConsoleAuthResponse`、`ConsolePrincipalResponse`、`ConsoleLoginRequest`、`ConsoleRefreshRequest` 和 `ConsoleLogoutRequest` 均在 Gateway 端点、api-client 导出和前端存储使用它们之前定义。
2. `expiresAtUtc` 在 Gateway 和前端使用之前加入 IAM 认证响应。
3. `organizationId`、`environmentId` 和 `permissionVersion` 在 Console 主体状态依赖它们之前加入 `/me`。
4. 认证存储使用生成的 api-client 函数，并将服务端状态查询/变更选项保留在 `frontend/packages/api-client/src/auth.ts` 中。

## 执行交接

计划已完成并保存至 `docs/superpowers/plans/2026-05-18-console-auth-shadcn-baseline.md`。有两种执行方式：

1. **子代理驱动（推荐）**——为每项任务分派新的子代理，在任务之间进行审核，以便快速迭代。
2. **会话内执行**——在当前会话中使用 executing-plans 执行任务，分批实施并设置检查点。

采用哪种方式？
