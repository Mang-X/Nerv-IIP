# Console Auth Shadcn Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the first production-shaped Console login loop through PlatformGateway and establish shadcn-vue as the frontend design-system baseline.

**Architecture:** The browser continues to use PlatformGateway as its only API surface. Gateway exposes Console auth endpoints, forwards to IAM for identity/session facts, and keeps OpenAPI/api-client generation as the frontend contract. The frontend initializes shadcn-vue in `packages/ui`, keeps Pinia Colada-generated api-client options for server state, and uses a Pinia auth store for client session state.

**Tech Stack:** .NET 10, FastEndpoints, xUnit, ASP.NET Core `WebApplicationFactory`, Vue 3 `<script setup lang="ts">`, Vite, Pinia, Pinia Colada, Hey API OpenAPI TypeScript, shadcn-vue official registry with `nova` preset, lucide-vue-next.

---

## Current Baseline

1. Current branch is `main`, ahead of `origin/main` by the committed design spec `501ce97 docs: design console auth shadcn baseline`.
2. `docs/superpowers/specs/2026-05-18-console-auth-shadcn-design.md` is the approved design source.
3. `frontend/packages/api-client` already uses Hey API with `@pinia/colada` generation.
4. Console currently consumes generated Colada options from `@nerv-iip/api-client`.
5. `frontend/packages/ui` still contains local primitives: `UiButton`, `UiPanel`, `UiBadge`.
6. `pnpm dlx shadcn-vue@latest info --json` currently reports no `components.json`, no Tailwind config, and no initialized shadcn-vue config.
7. `pnpm dlx shadcn-vue@latest docs ...` currently fails with `logger.debug is not a function`; use `search`, `view`, generated files, and official registry output during implementation if docs keeps failing.

## File Structure Map

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

## Task 1: Enrich IAM Current Principal For Console Context

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthModels.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Auth/IamAuthService.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Auth/AuthEndpoints.cs`
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamFoundationTests.cs`
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`

- [ ] **Step 1: Write failing IAM `/me` assertions**

In `IamFoundationTests.cs`, replace the local `AuthResponse` record and add a `MeResponse` record:

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

Then in `Admin_can_login_refresh_logout_and_validate_connector_host`, after the refresh succeeds and before logout, add:

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

In `IamPostgresProfileTests.cs`, change the local records at the bottom to:

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

Then after the existing `var principal = await me.Content.ReadFromJsonAsync<MeResponse>();` assertions, add:

```csharp
Assert.Equal("user-admin", principal!.UserId);
Assert.Equal("admin", principal.LoginName);
Assert.Equal("user", principal.PrincipalType);
Assert.Equal("org-001", principal.OrganizationId);
Assert.Equal("env-dev", principal.EnvironmentId);
Assert.Equal(1, principal.PermissionVersion);
Assert.True(auth.ExpiresAtUtc > DateTimeOffset.UtcNow);
```

- [ ] **Step 2: Run IAM tests and confirm red state**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~IamFoundationTests|FullyQualifiedName~IamPostgresProfileTests"
```

Expected: FAIL because `AuthResponse` does not include `ExpiresAtUtc` and `/me` does not return organization, environment, or permission version.

- [ ] **Step 3: Extend IAM auth models**

In `IamAuthModels.cs`, replace the existing `AuthResponse` and `CurrentPrincipalResponse` records with:

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

- [ ] **Step 4: Return token expiry from IAM token creation**

In `IamTokenService.cs`, add:

```csharp
public DateTimeOffset GetAccessTokenExpiresAtUtc(DateTimeOffset issuedAtUtc)
{
    return issuedAtUtc.AddMinutes(GetAccessTokenMinutes());
}
```

Then in `IamAuthService.CreateSessionResponse`, replace the access-token creation block with:

```csharp
var issuedAtUtc = DateTimeOffset.UtcNow;
var accessToken = tokenService.CreateAccessToken(user, session);
var expiresAtUtc = tokenService.GetAccessTokenExpiresAtUtc(issuedAtUtc);
return new AuthResponse(accessToken, refreshToken, session.Id.Id, expiresAtUtc);
```

Keep the existing `now` value for `UserSession` issued/expires fields.

- [ ] **Step 5: Return membership context from PostgreSQL `/me`**

In `IamAuthService.GetCurrentPrincipalAsync`, after the user validation block and before the return, add:

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

Remove the old four-field `CurrentPrincipalResponse` return.

- [ ] **Step 6: Return membership context from InMemory `/me`**

In `InMemoryIamStore.cs`, add:

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

At the bottom of the same file, add:

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

In `AuthEndpoints.cs`, replace the InMemory `/me` response with:

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

- [ ] **Step 7: Return expiry from InMemory login/refresh**

In `InMemoryIamStore.CreateSession`, replace the final return with:

```csharp
return new AuthResult(accessToken, refreshToken, sessionId, DateTimeOffset.UtcNow.AddMinutes(15));
```

At the bottom of the same file, replace `AuthResult` with:

```csharp
public sealed record AuthResult(string AccessToken, string RefreshToken, string SessionId, DateTimeOffset ExpiresAtUtc);
```

- [ ] **Step 8: Run IAM tests and confirm green state**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
```

Expected: PASS.

## Task 2: Add PlatformGateway Console Auth Facade

**Files:**

- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/ConsoleAuthModels.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Application/Auth/GatewayIamAuthClient.cs`
- Create: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Endpoints/Auth/ConsoleAuthEndpoints.cs`
- Create: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayConsoleAuthTests.cs`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Modify: `backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/GatewayOpenApiTests.cs`

- [ ] **Step 1: Write failing Gateway auth facade tests**

Create `GatewayConsoleAuthTests.cs`:

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

- [ ] **Step 2: Run Gateway auth tests and confirm red state**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayConsoleAuthTests
```

Expected: FAIL because `ConsoleLoginRequest`, `IGatewayIamAuthClient`, and Console auth endpoints do not exist yet.

- [ ] **Step 3: Add Gateway auth models and exception type**

Create `ConsoleAuthModels.cs`:

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

- [ ] **Step 4: Add IAM HTTP client used by Gateway**

Create `GatewayIamAuthClient.cs`:

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

- [ ] **Step 5: Add Console auth endpoints**

Create `ConsoleAuthEndpoints.cs`:

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

- [ ] **Step 6: Register Gateway IAM auth client and operation IDs**

In `Program.cs`, add the endpoint namespace:

```csharp
using Nerv.IIP.PlatformGateway.Web.Endpoints.Auth;
```

Add the HTTP client registration near existing Gateway clients:

```csharp
builder.Services.AddHttpClient<IGatewayIamAuthClient, HttpGatewayIamAuthClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5104");
});
```

Add to the FastEndpoints name generator:

```csharp
nameof(LoginConsoleUserEndpoint) => "loginConsoleUser",
nameof(RefreshConsoleSessionEndpoint) => "refreshConsoleSession",
nameof(LogoutConsoleSessionEndpoint) => "logoutConsoleSession",
nameof(GetConsolePrincipalEndpoint) => "getConsolePrincipal",
```

- [ ] **Step 7: Update OpenAPI operation ID tests**

In `GatewayOpenApiTests.cs`, add:

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

- [ ] **Step 8: Run Gateway tests**

Run:

```powershell
dotnet test backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests/Nerv.IIP.PlatformGateway.Web.Tests.csproj --no-restore
```

Expected: PASS.

## Task 3: Regenerate Gateway API Client And Add Auth Transport

**Files:**

- Modify: `frontend/packages/api-client/openapi/platform-gateway.v1.json`
- Modify generated: `frontend/packages/api-client/src/generated/**`
- Create: `frontend/packages/api-client/src/auth.ts`
- Modify: `frontend/packages/api-client/src/index.ts`
- Modify: `frontend/packages/api-client/src/transport/client-config.ts`
- Modify: `frontend/packages/api-client/src/transport/client-config.test.ts`

- [ ] **Step 1: Export Gateway OpenAPI after backend auth endpoints exist**

Run:

```powershell
pwsh scripts/export-gateway-openapi.ps1
```

Expected: `frontend/packages/api-client/openapi/platform-gateway.v1.json` includes the four new Console auth paths and operation IDs.

- [ ] **Step 2: Regenerate api-client**

Run:

```powershell
pnpm -C frontend generate:api
```

Expected: generated SDK and `@pinia/colada.gen.ts` include:

```text
loginConsoleUserMutationOptions
refreshConsoleSessionMutationOptions
logoutConsoleSessionMutationOptions
getConsolePrincipalQueryOptions
```

- [ ] **Step 3: Add stable auth exports**

Create `frontend/packages/api-client/src/auth.ts`:

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

If generated type names differ, use the exact names from `frontend/packages/api-client/src/generated/types.gen.ts` and keep these public aliases.

In `frontend/packages/api-client/src/index.ts`, add:

```ts
export * from './auth'
```

- [ ] **Step 4: Write failing transport auth tests**

In `client-config.test.ts`, add:

```ts
import { client } from '../generated/client.gen'
import { configureApiClient } from './client-config'
```

Then add tests:

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

- [ ] **Step 5: Run transport tests and confirm red state**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test -- src/transport/client-config.test.ts
```

Expected: FAIL because `ConfigureApiClientOptions` does not support `accessTokenProvider`, `fetch`, or `onUnauthorized`.

- [ ] **Step 6: Implement dynamic bearer transport**

Replace `client-config.ts` with:

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

- [ ] **Step 7: Run api-client tests**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
```

Expected: PASS.

## Task 4: Initialize shadcn-vue In The UI Package

**Files:**

- Create/Modify: `frontend/components.json`
- Modify: `frontend/package.json`
- Modify: `frontend/pnpm-lock.yaml`
- Modify: `frontend/vite.config.ts`
- Modify: `frontend/tsconfig.base.json`
- Modify: `frontend/packages/ui/package.json`
- Modify: `frontend/packages/ui/tsconfig.json`
- Modify/Create: `frontend/packages/ui/src/index.ts`
- Create: `frontend/packages/ui/src/lib/utils.ts`
- Create: `frontend/packages/ui/src/components/ui/**`
- Modify: `frontend/apps/console/src/assets/main.css`

- [ ] **Step 1: Inspect shadcn-vue context before initialization**

Run:

```powershell
pnpm dlx shadcn-vue@latest info --json
pnpm dlx shadcn-vue@latest search -q field '@shadcn'
pnpm dlx shadcn-vue@latest init --help
pnpm dlx shadcn-vue@latest add --help
```

Expected: info still reports no config before initialization; search includes `@shadcn/field`.

- [ ] **Step 2: Initialize shadcn-vue with the selected baseline**

Run from `frontend`:

```powershell
pnpm dlx shadcn-vue@latest init --template vite --preset nova --base reka --icon-library lucide --base-color neutral --css-variables --no-src-dir --cwd .
```

When prompted for aliases, use package-owned paths:

```text
components: @nerv-iip/ui/components
utils: @nerv-iip/ui/lib/utils
ui: @nerv-iip/ui/components/ui
lib: @nerv-iip/ui/lib
```

Expected: `components.json` is created and points UI code to `frontend/packages/ui/src/components/ui` and utilities to `frontend/packages/ui/src/lib/utils.ts`. If the CLI writes to `frontend/components/ui` instead, move generated files into `frontend/packages/ui/src/components/ui` and update `components.json` before adding components.

- [ ] **Step 3: Add initial shadcn-vue components**

Run from `frontend`:

```powershell
pnpm dlx shadcn-vue@latest add button card field input alert badge separator skeleton dropdown-menu avatar sonner spinner --cwd .
```

Expected: files are added below `frontend/packages/ui/src/components/ui/**`.

- [ ] **Step 4: Export shadcn-vue components through `@nerv-iip/ui`**

Replace `frontend/packages/ui/src/index.ts` with:

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

If a generated component exports different names, inspect its local `index.ts` and keep `@nerv-iip/ui` as the stable public surface.

- [ ] **Step 5: Update TypeScript and Vite aliases**

In `frontend/tsconfig.base.json`, add paths:

```json
"baseUrl": ".",
"paths": {
  "@nerv-iip/api-client": ["packages/api-client/src/index.ts"],
  "@nerv-iip/app-shell": ["packages/app-shell/src/index.ts"],
  "@nerv-iip/ui": ["packages/ui/src/index.ts"],
  "@nerv-iip/ui/*": ["packages/ui/src/*"]
}
```

In both `frontend/vite.config.ts` and `frontend/apps/console/vite.config.ts`, add:

```ts
'@nerv-iip/ui/': fileURLToPath(new URL('./packages/ui/src/', import.meta.url)),
```

For the app-level config, use:

```ts
'@nerv-iip/ui/': fileURLToPath(new URL('../../packages/ui/src/', import.meta.url)),
```

- [ ] **Step 6: Install and verify dependencies**

Run:

```powershell
pnpm -C frontend install
pnpm -C frontend --filter @nerv-iip/ui typecheck
```

Expected: lockfile updates; UI package typecheck passes.

## Task 5: Add Console Auth Store, Storage, And Route Guards

**Files:**

- Create: `frontend/apps/console/src/api/auth.ts`
- Create: `frontend/apps/console/src/stores/auth.ts`
- Create: `frontend/apps/console/src/stores/auth.test.ts`
- Create: `frontend/apps/console/src/router/guards/auth.ts`
- Create: `frontend/apps/console/src/router/guards/auth.test.ts`
- Modify: `frontend/apps/console/src/router/index.ts`
- Modify: `frontend/apps/console/src/main.ts`

- [ ] **Step 1: Add auth API wrapper**

Create `api/auth.ts`:

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

- [ ] **Step 2: Write auth store tests**

Create `stores/auth.test.ts` with mocked `@/api/auth`:

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

- [ ] **Step 3: Run auth store tests and confirm red state**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- src/stores/auth.test.ts
```

Expected: FAIL because `stores/auth.ts` does not exist.

- [ ] **Step 4: Implement setup-style Pinia auth store**

Create `stores/auth.ts`:

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

Remove the unused `ConsoleAuthError` import if lint reports it unused.

- [ ] **Step 5: Add router guard tests**

Create `router/guards/auth.test.ts`:

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

- [ ] **Step 6: Implement auth guard**

Create `router/guards/auth.ts`:

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

In `router/index.ts`, after router creation, add:

```ts
import { installAuthGuard } from './guards/auth'

installAuthGuard(router)
```

- [ ] **Step 7: Configure api-client with auth provider**

In `main.ts`, replace the early `configureApiClient()` call with:

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

Move the existing `const app` and `const pinia` declarations so Pinia is installed before `useAuthStore()` is called. Keep the existing Pinia Colada installation after `app.use(pinia)`.

- [ ] **Step 8: Run store and guard tests**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- src/stores/auth.test.ts src/router/guards/auth.test.ts
```

Expected: PASS.

## Task 6: Build Login UI And Authenticated App Shell

**Files:**

- Create: `frontend/apps/console/src/components/auth/LoginForm.vue`
- Create: `frontend/apps/console/src/components/auth/LoginForm.test.ts`
- Create: `frontend/apps/console/src/pages/login.vue`
- Modify: `frontend/apps/console/src/pages/index.vue`
- Modify: `frontend/apps/console/src/pages/operations/[operationTaskId].vue`
- Modify: `frontend/apps/console/src/layouts/DefaultLayout.vue`
- Modify: `frontend/packages/app-shell/src/AppShell.vue`
- Modify: `frontend/packages/app-shell/src/index.ts`
- Modify: `frontend/apps/console/src/App.test.ts`

- [ ] **Step 1: Write LoginForm tests**

Create `LoginForm.test.ts`:

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

- [ ] **Step 2: Implement LoginForm with shadcn-vue components**

Create `LoginForm.vue`:

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

- [ ] **Step 3: Add login route**

Create `pages/login.vue`:

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

- [ ] **Step 4: Mark existing pages as protected**

In `pages/index.vue` and `pages/operations/[operationTaskId].vue`, add:

```ts
definePage({
  meta: {
    requiresAuth: true,
    title: 'Instances',
  },
})
```

For operation detail, use title `Operation task`.

- [ ] **Step 5: Add user menu and sign-out command to AppShell**

Replace `packages/app-shell/src/AppShell.vue` with a shell that accepts an optional user:

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

- [ ] **Step 6: Wire DefaultLayout to auth store**

Replace `DefaultLayout.vue` script with:

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

Replace the template with:

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

- [ ] **Step 7: Install Toaster**

In `App.vue`, use the shadcn-vue Toaster:

```vue
<script setup lang="ts">
import { Toaster } from '@nerv-iip/ui'
</script>

<template>
  <RouterView />
  <Toaster />
</template>
```

- [ ] **Step 8: Run frontend component tests**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/console test -- src/components/auth/LoginForm.test.ts src/App.test.ts
```

Expected: PASS.

## Task 7: Migrate Existing Console Components To shadcn-vue And Delete Old UI Primitives

**Files:**

- Modify: `frontend/apps/console/src/components/console/InstanceTable.vue`
- Modify: `frontend/apps/console/src/components/console/InstanceDetailPanel.vue`
- Modify: `frontend/apps/console/src/components/console/OperationTimeline.vue`
- Modify: `frontend/apps/console/src/pages/index.vue`
- Modify: `frontend/apps/console/src/pages/operations/[operationTaskId].vue`
- Delete: `frontend/packages/ui/src/UiBadge.vue`
- Delete: `frontend/packages/ui/src/UiButton.vue`
- Delete: `frontend/packages/ui/src/UiPanel.vue`
- Modify: `frontend/packages/ui/src/index.ts`
- Modify: `frontend/apps/console/src/pages/index.test.ts`

- [ ] **Step 1: Replace old imports in console components**

In `InstanceTable.vue`, replace:

```ts
import { UiBadge, UiButton } from '@nerv-iip/ui'
```

with:

```ts
import { Badge, Button } from '@nerv-iip/ui'
```

Replace `<UiBadge>` with:

```vue
<Badge variant="secondary">
  {{ instance.reportedStatus ?? 'unknown' }}
</Badge>
```

Replace `<UiButton>` with:

```vue
<Button
  :disabled="restartPending || !instance.instanceKey"
  variant="outline"
  @click="restartInstance(instance)"
>
  Restart
</Button>
```

In `InstanceDetailPanel.vue` and `OperationTimeline.vue`, replace `UiBadge` imports and tags with `Badge`.

- [ ] **Step 2: Replace custom loading and error callouts**

In `pages/index.vue`, import:

```ts
import { Alert, AlertDescription, Skeleton } from '@nerv-iip/ui'
```

Replace loading/error paragraphs with:

```vue
<Skeleton v-if="listPending" class="h-12 w-full" />
<Alert v-if="listError" variant="destructive">
  <AlertDescription>{{ listError.message }}</AlertDescription>
</Alert>
<Alert v-if="restartError" variant="destructive">
  <AlertDescription>{{ restartError.message }}</AlertDescription>
</Alert>
```

In `pages/operations/[operationTaskId].vue`, use `Alert` for `operationError`.

- [ ] **Step 3: Delete old primitive exports and files**

Run:

```powershell
rg -n "UiButton|UiPanel|UiBadge" frontend
```

Expected before deletion: only `frontend/packages/ui/src/index.ts` and old primitive files remain.

Delete:

```text
frontend/packages/ui/src/UiBadge.vue
frontend/packages/ui/src/UiButton.vue
frontend/packages/ui/src/UiPanel.vue
```

Ensure `frontend/packages/ui/src/index.ts` no longer exports `UiBadge`, `UiButton`, or `UiPanel`.

- [ ] **Step 4: Update page tests for auth guard and shadcn markup**

In `pages/index.test.ts`, mock auth store or set route meta behavior so the page mounts without redirect. Add a Pinia auth state before mounting:

```ts
import { useAuthStore } from '@/stores/auth'
```

Inside `mountPage`, after `createPinia()`:

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

Use `pinia` in the global plugins array.

- [ ] **Step 5: Run old primitive search and frontend tests**

Run:

```powershell
rg -n "UiButton|UiPanel|UiBadge" frontend
pnpm -C frontend --filter @nerv-iip/console test
pnpm -C frontend --filter @nerv-iip/ui typecheck
```

Expected: `rg` returns no matches; tests and typecheck pass.

## Task 8: Documentation, Browser Verification, And Final Gate

**Files:**

- Modify: `README.md`
- Modify: `docs/architecture/frontend-design-system-planning.md`
- Modify: `docs/architecture/frontend-structure.md`
- Modify: `docs/architecture/iam-authentication-baseline.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/superpowers/plans/2026-05-18-console-auth-shadcn-baseline.md`

- [ ] **Step 1: Update stale README worktree wording**

In `README.md`, replace:

```markdown
- 当前工作树：`codex/iam-persistent-auth-foundation`，当前阶段为 IAM Persistent Auth Foundation。
```

with:

```markdown
- 当前主线：`main` 已合入 IAM Persistent Auth Foundation、Gateway-wide permission enforcement、pnpm 11.1.2 基线和 Console Auth + shadcn-vue 设计规格。
```

- [ ] **Step 2: Update stale schema catalog Gateway status**

In `docs/architecture/database-schema-catalog.md`, replace the sentence that says Gateway-wide permission enforcement is not connected with:

```markdown
Gateway-wide permission enforcement 已覆盖现有 Console API；Gateway 转发 bearer token 与 permission/context 到 IAM internal authorization check endpoint，不直接读取 IAM schema。
```

- [ ] **Step 3: Update frontend design-system planning**

Append to `docs/architecture/frontend-design-system-planning.md`:

```markdown
## Selected Baseline

Console Auth + shadcn-vue Baseline 选择 official shadcn-vue registry、`nova` preset、Vite template、Reka base components 和 semantic token 体系。组件源码归属 `frontend/packages/ui`，Console 应用通过 `@nerv-iip/ui` 稳定导出消费组件。旧 `UiButton`、`UiPanel` 和 `UiBadge` primitives 在迁移完成后删除，不再作为并行设计系统维护。
```

- [ ] **Step 4: Update frontend structure**

In `docs/architecture/frontend-structure.md`, add under state/request layering:

```markdown
### Console Auth

Console 登录闭环通过 PlatformGateway Console Auth facade 调用 IAM。`stores/auth.ts` 只管理客户端会话状态，`api-client` 继续由 Gateway OpenAPI 生成 SDK 与 Pinia Colada options。路由守卫放在 `src/router/guards/auth.ts`，登录页和登录表单放在 `src/pages/login.vue` 与 `src/components/auth/LoginForm.vue`。
```

- [ ] **Step 5: Update IAM baseline and readiness**

In `docs/architecture/iam-authentication-baseline.md`, add to current implementation status:

```markdown
Console login UI now consumes IAM through PlatformGateway Console Auth facade. The browser keeps a single Gateway API base URL; Gateway forwards login, refresh, logout and current-principal requests to IAM without owning identity facts.
```

In `docs/architecture/implementation-readiness.md`, add a new current conclusion after Gateway permission enforcement:

```markdown
Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线；完整用户/角色/会话管理、OAuth/OIDC、SSO、MFA 和 ABAC 仍属于后续阶段。
```

- [ ] **Step 6: Run backend and frontend quality gates**

Run:

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

Expected: every command exits `0`; `check-script-governance` prints `Script governance check passed.`

- [ ] **Step 7: Browser verification**

Start the local frontend dev server:

```powershell
pnpm -C frontend --filter @nerv-iip/console dev
```

Start the backend stack needed for Console auth with the existing local verification entry point:

```powershell
pwsh scripts/verify-third-slice-console.ps1 -UsePostgres
```

If the verification script exits after completing the check instead of leaving services running, run the Gateway/IAM/AppHub/Ops services through the existing AppHost or the service-specific run commands used by the script, then open:

```text
http://127.0.0.1:5173/login
```

Use Browser/Playwright verification for:

1. Desktop login page at 1440x900.
2. Mobile login page at 390x844.
3. Invalid credentials show inline error.
4. Valid seeded admin login redirects to `/`.
5. Instance list requests include bearer and render.
6. Sign-out returns to `/login`.

Expected: screenshots show shadcn-vue styles, no overlapping text, visible focus states, and usable layout on desktop and mobile.

- [ ] **Step 8: Commit implementation**

Run:

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

Expected: commit succeeds after all verification commands pass.

## Execution Order

1. Task 1 first, because Gateway facade needs IAM `/me` to return Console principal context.
2. Task 2 adds Gateway browser-facing auth endpoints and stable OpenAPI operation IDs.
3. Task 3 regenerates api-client and preserves Pinia Colada integration.
4. Task 4 initializes shadcn-vue before UI code depends on its components.
5. Task 5 adds session state and route protection.
6. Task 6 adds Login UI and authenticated shell behavior.
7. Task 7 migrates existing visible Console components and deletes old primitives.
8. Task 8 updates durable docs and runs final verification.

## Self Review

Spec coverage:

1. Gateway Auth facade is covered by Task 2.
2. IAM remains the identity/session fact owner through Task 1 and Task 2.
3. Generated api-client and Pinia Colada integration are preserved in Task 3.
4. shadcn-vue official registry + `nova` baseline is covered by Task 4.
5. Login UI, auth store, startup restore, bearer injection, route guards and logout are covered by Tasks 5 and 6.
6. Old UI primitive deletion is covered by Task 7.
7. Documentation residual cleanup is covered by Task 8.
8. `packages/auth` extraction remains future-only and is intentionally not implemented.

Placeholder scan:

1. No step uses placeholder wording or deferred implementation markers.
2. Generated files are handled by exact CLI commands and explicit inspection requirements.
3. Manual code-changing steps include concrete code blocks.

Type consistency:

1. `ConsoleAuthResponse`, `ConsolePrincipalResponse`, `ConsoleLoginRequest`, `ConsoleRefreshRequest`, and `ConsoleLogoutRequest` are defined before Gateway endpoints, api-client exports and frontend store use them.
2. `expiresAtUtc` is added to IAM auth responses before Gateway and frontend consume it.
3. `organizationId`, `environmentId`, and `permissionVersion` are added to `/me` before Console principal state depends on them.
4. Auth store uses generated api-client functions and keeps server-state query/mutation options in `frontend/packages/api-client/src/auth.ts`.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-18-console-auth-shadcn-baseline.md`. Two execution options:

1. **Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
