# Business Console MVP 实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**构建 #166 至 #169 的 Business Console MVP，包括专用 BusinessGateway、生成的 api-client 入口和 `frontend/apps/business-console`。

**架构：**`frontend/apps/business-console` 使用生成的 `@nerv-iip/api-client` business-console 导出。`backend/gateway/BusinessGateway` 暴露 `/api/business-console/v1/**`，使用用户 bearer token 检查 IAM 权限，并使用内部服务 token 调用 BusinessMasterData、Inventory、Quality 和 MES。PlatformGateway 与 `frontend/apps/console` 仍仅用于平台控制平面。

**技术栈：**.NET 10、FastEndpoints、FastEndpoints.Swagger、Microsoft.AspNetCore.Mvc.Testing、Vue 3、Vite、Vue Router 文件路由、Pinia、Pinia Colada、Hey API，以及通过 `@nerv-iip/ui` 使用的 shadcn-vue。

---

## 来源文档

实施前阅读以下文档：

1. `docs/architecture/implementation-readiness.md`
2. `docs/adr/0012-business-platform-domain-layering.md`
3. `docs/architecture/api-contract-and-codegen.md`
4. `docs/architecture/business-platform-domain-architecture.md`
5. `docs/architecture/frontend-structure.md`
6. `docs/superpowers/specs/2026-05-24-business-console-mvp-design.md`

## 文件结构

创建以下后端文件：

```text
backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/
  Nerv.IIP.BusinessGateway.Web.csproj
  Program.cs
  ResponseDataEndpointResults.cs
  Application/Auth/BusinessGatewayAuthentication.cs
  Application/Auth/BusinessGatewayAuthorization.cs
  Application/Auth/AuthorizedBusinessProxyEndpoint.cs
  Application/Http/AcceptLanguageForwardingHandler.cs
  Application/OpenApi/BusinessGatewayOperationIdAttribute.cs
  Application/OpenApi/BusinessGatewayOperationIdConvention.cs
  Application/BusinessServices/BusinessConsoleModels.cs
  Application/BusinessServices/BusinessServiceClients.cs
  Endpoints/Health/HealthEndpoint.cs
  Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs
  Endpoints/Inventory/BusinessConsoleInventoryEndpoints.cs
  Endpoints/Quality/BusinessConsoleQualityEndpoints.cs
  Endpoints/Mes/BusinessConsoleMesEndpoints.cs
  Properties/launchSettings.json
  appsettings.json
  appsettings.Development.json
backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/
  Nerv.IIP.BusinessGateway.Web.Tests.csproj
  BusinessGatewayTestTokens.cs
  BusinessGatewayOpenApiTests.cs
  BusinessGatewayAuthorizationTests.cs
  BusinessGatewayProxyTests.cs
```

修改以下后端和基础设施文件：

```text
backend/Nerv.IIP.sln
infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
infra/aspire/Nerv.IIP.AppHost/Program.cs
nerv.ps1
scripts/export-gateway-openapi.ps1
```

创建和修改以下前端文件：

```text
frontend/apps/business-console/
  package.json
  index.html
  vite.config.ts
  tsconfig.json
  src/App.vue
  src/main.ts
  src/assets/main.css
  src/api/auth.ts
  src/api/unauthorized.ts
  src/components/auth/LoginForm.vue
  src/i18n/index.ts
  src/layouts/BusinessLayout.vue
  src/pages/index.vue
  src/pages/login.vue
  src/pages/master-data/skus/index.vue
  src/pages/inventory/availability/index.vue
  src/pages/inventory/movements/index.vue
  src/pages/inventory/counts/index.vue
  src/pages/quality/inspections/index.vue
  src/pages/quality/ncrs/index.vue
  src/pages/mes/work-orders/index.vue
  src/pages/mes/schedules/index.vue
  src/router/index.ts
  src/router/document-title.ts
  src/router/guards/auth.ts
  src/router/redirects.ts
  src/stores/auth.ts
  src/composables/useBusinessMasterData.ts
  src/composables/useBusinessInventory.ts
  src/composables/useBusinessQuality.ts
  src/composables/useBusinessMes.ts
  src/test/setup.ts
  src/composables/useBusinessMasterData.test.ts
  src/composables/useBusinessInventory.test.ts
  src/composables/useBusinessQuality.test.ts
  src/composables/useBusinessMes.test.ts
  e2e/business-console.spec.ts
```

修改以下前端 package 和 workspace 文件：

```text
frontend/package.json
frontend/vite.config.ts
frontend/packages/api-client/openapi-ts.config.ts
frontend/packages/api-client/package.json
frontend/packages/api-client/src/transport/client-config.ts
frontend/packages/api-client/src/business-console.ts
frontend/packages/api-client/src/generated-contract.test.ts
frontend/packages/api-client/src/index.ts
frontend/packages/api-client/openapi/business-gateway-console.v1.json
```

代码实际落地后修改以下文档：

```text
docs/architecture/api-contract-and-codegen.md
docs/architecture/frontend-structure.md
docs/architecture/implementation-readiness.md
```

## Task 1：创建 BusinessGateway 骨架和失败的契约测试

**文件：**
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/ResponseDataEndpointResults.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Health/HealthEndpoint.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Properties/launchSettings.json`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/appsettings.json`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/appsettings.Development.json`
- 创建：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj`
- 创建：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建项目目录和 SDK 项目**

运行：

```powershell
New-Item -ItemType Directory -Force backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web | Out-Null
New-Item -ItemType Directory -Force backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests | Out-Null
dotnet new web -n Nerv.IIP.BusinessGateway.Web -o backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web
dotnet new xunit -n Nerv.IIP.BusinessGateway.Web.Tests -o backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests
dotnet sln backend/Nerv.IIP.sln add backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
```

预期：两个项目均已创建并加入后端解决方案。

- [ ] **步骤 2：替换 BusinessGateway Web csproj**

使用以下内容：

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <PackageReference Include="FastEndpoints" />
    <PackageReference Include="FastEndpoints.Swagger" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="NetCorePal.Extensions.AspNetCore" />
    <ProjectReference Include="..\..\..\..\common\Contracts\Nerv.IIP.Contracts.Iam\Nerv.IIP.Contracts.Iam.csproj" />
    <ProjectReference Include="..\..\..\..\common\Caching\Nerv.IIP.Caching\Nerv.IIP.Caching.csproj" />
    <ProjectReference Include="..\..\..\..\common\Localization\Nerv.IIP.Localization\Nerv.IIP.Localization.csproj" />
    <ProjectReference Include="..\..\..\..\common\Observability\Nerv.IIP.Observability\Nerv.IIP.Observability.csproj" />
    <ProjectReference Include="..\..\..\..\common\Sdk\Nerv.IIP.Sdk.Core\Nerv.IIP.Sdk.Core.csproj" />
    <ProjectReference Include="..\..\..\..\common\ServiceAuth\Nerv.IIP.ServiceAuth\Nerv.IIP.ServiceAuth.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
```

- [ ] **步骤 3：替换 BusinessGateway 测试 csproj**

使用以下内容：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Nerv.IIP.BusinessGateway.Web\Nerv.IIP.BusinessGateway.Web.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **步骤 4：编写第一个失败的 OpenAPI 测试**

创建 `BusinessGatewayOpenApiTests.cs`：

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayOpenApiTests
{
    [Fact]
    public async Task Business_gateway_exports_openapi_document_with_stable_business_console_operation_ids()
    {
        await using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();

        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        AssertOperationIdsAreUnique(document);

        Assert.Equal(
            "listBusinessConsoleSkus",
            paths.GetProperty("/api/business-console/v1/master-data/skus").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal(
            "createBusinessConsoleSku",
            paths.GetProperty("/api/business-console/v1/master-data/skus").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal(
            "getBusinessConsoleInventoryAvailability",
            paths.GetProperty("/api/business-console/v1/inventory/availability").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal(
            "postBusinessConsoleInventoryMovement",
            paths.GetProperty("/api/business-console/v1/inventory/movements").GetProperty("post").GetProperty("operationId").GetString());
        Assert.Equal(
            "listBusinessConsoleQualityNcrs",
            paths.GetProperty("/api/business-console/v1/quality/ncrs").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal(
            "listBusinessConsoleMesWorkOrders",
            paths.GetProperty("/api/business-console/v1/mes/work-orders").GetProperty("get").GetProperty("operationId").GetString());
        Assert.Equal(
            "HealthEndpoint",
            paths.GetProperty("/health").GetProperty("get").GetProperty("operationId").GetString());
    }

    private static void AssertOperationIdsAreUnique(JsonDocument document)
    {
        var operations = document.RootElement
            .GetProperty("paths")
            .EnumerateObject()
            .SelectMany(path => path.Value
                .EnumerateObject()
                .Where(operation => IsHttpMethod(operation.Name))
                .Select(operation => (
                    Name: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    OperationId: operation.Value.TryGetProperty("operationId", out var operationId)
                        ? operationId.GetString()
                        : null)))
            .ToArray();

        Assert.Empty(operations.Where(operation => string.IsNullOrWhiteSpace(operation.OperationId)).Select(operation => operation.Name));

        var duplicateOperationIds = operations
            .Where(operation => !string.IsNullOrWhiteSpace(operation.OperationId))
            .GroupBy(operation => operation.OperationId!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(operation => operation.Name))}")
            .ToArray();
        Assert.Empty(duplicateOperationIds);
    }

    private static bool IsHttpMethod(string method) =>
        method is "get" or "post" or "put" or "patch" or "delete" or "head" or "options" or "trace";
}
```

- [ ] **步骤 5：运行失败的 OpenAPI 测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_gateway_exports_openapi_document_with_stable_business_console_operation_ids
```

预期：失败，因为尚未实现 `/swagger/v1/swagger.json` 或 business-console 路径。

- [ ] **步骤 6：添加最小化 Program、响应写入器和健康检查 endpoint**

使用以下 `Program.cs`：

```csharp
using FastEndpoints;
using FastEndpoints.Swagger;
using Nerv.IIP.BusinessGateway.Web;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.Caching;
using Nerv.IIP.Localization;
using Nerv.IIP.Observability;
using Nerv.IIP.ServiceAuth;
using NetCorePal.Extensions.AspNetCore;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddFastEndpoints()
    .SwaggerDocument(o =>
    {
        o.DocumentSettings = s =>
        {
            s.Title = "Nerv IIP Business Gateway";
            s.Version = "v1";
        };
    });
builder.Services.AddNervIipCaching(builder.Configuration, "business-gateway");
builder.Services.AddNervIipObservability(builder.Configuration, "business-gateway");
builder.Services.AddNervIipLocalization();
builder.Services.AddNervIipInternalServiceTokenProvider(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseNervIipCorrelation();
app.UseNervIipRequestLocalization();
app.UseKnownExceptionHandler(_ => new() { KnownExceptionStatusCode = HttpStatusCode.BadRequest });
app.UseFastEndpoints(c =>
{
    c.Endpoints.NameGenerator = BusinessGatewayOperationIdConvention.Generate;
}).UseSwaggerGen();
app.Run();

public partial class Program;
```

使用以下 `ResponseDataEndpointResults.cs`：

```csharp
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web;

public static class ResponseDataEndpointResults
{
    public static async Task WriteDataAsync<T>(
        HttpContext context,
        int statusCode,
        T data,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(data.AsResponseData(), cancellationToken);
    }

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(ResponseData.Error<string>(message, statusCode), cancellationToken);
    }
}
```

使用以下 `HealthEndpoint.cs`：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.Health;

[HttpGet("/health")]
[AllowAnonymous]
public sealed class HealthEndpoint : EndpointWithoutRequest<string>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync("Healthy", cancellation: ct);
    }
}
```

使用以下 `BusinessGatewayOperationIdAttribute.cs`：

```csharp
namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BusinessGatewayOperationIdAttribute(string operationId) : Attribute
{
    public string OperationId { get; } = operationId;
}
```

使用以下 `BusinessGatewayOperationIdConvention.cs`：

```csharp
using FastEndpoints;

namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

public static class BusinessGatewayOperationIdConvention
{
    public static string Generate(EndpointDefinition context)
    {
        var attribute = context.EndpointType
            .GetCustomAttributes(typeof(BusinessGatewayOperationIdAttribute), inherit: false)
            .OfType<BusinessGatewayOperationIdAttribute>()
            .SingleOrDefault();
        if (attribute is not null)
        {
            return attribute.OperationId;
        }

        return context.EndpointType.Name;
    }
}
```

- [ ] **步骤 7：添加返回 501 的临时 endpoint stub**

为测试中断言的每个 operation ID 创建 endpoint 类。每个类应使用 `[HttpGet]` 或 `[HttpPost]`；`[AllowAnonymous]` 仅可用于本骨架任务；并调用 `ThrowError("not-implemented", StatusCodes.Status501NotImplemented)`。这样可在 auth/proxy 行为落地前保持 OpenAPI 稳定。

以下是 `ListBusinessConsoleSkusEndpoint` 示例：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("listBusinessConsoleSkus")]
[AllowAnonymous]
public sealed class ListBusinessConsoleSkusEndpoint : EndpointWithoutRequest<object>
{
    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.ErrorsAsync(StatusCodes.Status501NotImplemented, ct);
    }
}
```

为以下操作创建具体 endpoint 类，并使用相同的 attribute、路由形状和 `Send.ErrorsAsync(StatusCodes.Status501NotImplemented, ct)` 方法体：

```text
CreateBusinessConsoleSkuEndpoint
GetBusinessConsoleInventoryAvailabilityEndpoint
PostBusinessConsoleInventoryMovementEndpoint
CreateBusinessConsoleInventoryCountTaskEndpoint
ConfirmBusinessConsoleInventoryCountAdjustmentEndpoint
ListBusinessConsoleQualityInspectionPlansEndpoint
CreateBusinessConsoleQualityInspectionRecordEndpoint
ListBusinessConsoleQualityNcrsEndpoint
SubmitBusinessConsoleQualityNcrDispositionEndpoint
CloseBusinessConsoleQualityNcrEndpoint
ListBusinessConsoleMesWorkOrdersEndpoint
CreateBusinessConsoleMesRushWorkOrderEndpoint
RunBusinessConsoleMesScheduleEndpoint
RecordBusinessConsoleMesProductionReportEndpoint
```

- [ ] **步骤 8：再次运行 OpenAPI 测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_gateway_exports_openapi_document_with_stable_business_console_operation_ids
```

预期：通过。

- [ ] **步骤 9：提交后端骨架**

运行：

```powershell
git add backend/gateway/BusinessGateway backend/Nerv.IIP.sln
git commit -m "feat: add business gateway skeleton"
```

## Task 2：添加 BusinessGateway 认证、IAM 授权和内部代理基础

**文件：**
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthentication.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Http/AcceptLanguageForwardingHandler.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- 创建：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayTestTokens.cs`
- 创建：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`

- [ ] **步骤 1：编写失败的授权测试**

创建 `BusinessGatewayAuthorizationTests.cs`：

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayAuthorizationTests
{
    [Fact]
    public async Task Business_console_endpoint_requires_user_authentication()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var factory = CreateFactory(auth);

        var response = await factory.CreateClient().GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(auth.LastRequirement);
    }

    [Fact]
    public async Task Business_console_endpoint_returns_forbidden_when_iam_denies_permission()
    {
        var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var factory = CreateFactory(auth);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(BusinessGatewayPermissions.MasterDataProductsRead, auth.LastRequirement!.PermissionCode);
        Assert.Equal("org-001", auth.LastRequirement.OrganizationId);
        Assert.Equal("env-dev", auth.LastRequirement.EnvironmentId);
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeBusinessGatewayAuthorizationClient auth) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBusinessGatewayAuthorizationClient>();
            services.AddSingleton<IBusinessGatewayAuthorizationClient>(auth);
        }));
}

internal sealed class FakeBusinessGatewayAuthorizationClient(bool allowed) : IBusinessGatewayAuthorizationClient
{
    public BusinessGatewayPermissionRequirement? LastRequirement { get; private set; }

    public static FakeBusinessGatewayAuthorizationClient Allowed() => new(true);

    public static FakeBusinessGatewayAuthorizationClient Forbidden() => new(false);

    public Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        LastRequirement = requirement;
        return Task.FromResult(allowed
            ? BusinessGatewayAuthorizationResult.Allowed("user-admin", "user", "admin")
            : BusinessGatewayAuthorizationResult.Forbidden("forbidden"));
    }
}
```

- [ ] **步骤 2：编写测试 token helper**

创建 `BusinessGatewayTestTokens.cs`：

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

internal static class BusinessGatewayTestTokens
{
    public static string ValidAccessToken(
        string organizationId = "org-001",
        string environmentId = "env-dev")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("business-gateway-test-signing-key-32"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "netcorepal",
            audience: "netcorepal",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, "user-admin"),
                new Claim("principalType", "user"),
                new Claim("loginName", "admin"),
                new Claim("email", "admin@nerv.local"),
                new Claim("organizationId", organizationId),
                new Claim("environmentId", environmentId),
                new Claim("permissionVersion", "7")
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **步骤 3：运行测试以验证失败**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_console_endpoint
```

预期：失败，因为 BusinessGateway auth 类型和 policy 尚不存在。

- [ ] **步骤 4：实现认证和权限类型**

创建 `BusinessGatewayAuthentication.cs`：

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public static class BusinessGatewayPolicies
{
    public const string BusinessConsoleAuthenticated = "BusinessConsoleAuthenticated";
}

public static class BusinessGatewayAuthentication
{
    public static IServiceCollection AddBusinessGatewayAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters.ValidAudience = "netcorepal";
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidIssuer = "netcorepal";
                options.TokenValidationParameters.ValidateIssuer = true;

                var signingKey = configuration["Iam:Jwt:SigningKey"];
                if (!string.IsNullOrWhiteSpace(signingKey))
                {
                    options.TokenValidationParameters.IssuerSigningKey =
                        new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey));
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                }
                else if (environment.IsEnvironment("Testing"))
                {
                    options.TokenValidationParameters.IssuerSigningKey =
                        new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes("business-gateway-test-signing-key-32"));
                    options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                }
            });

        services.AddAuthorization(options =>
            options.AddPolicy(BusinessGatewayPolicies.BusinessConsoleAuthenticated, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme);
            }));

        return services;
    }
}
```

创建包含权限常量的 `BusinessGatewayAuthorization.cs`：

```csharp
using Microsoft.AspNetCore.Authentication;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

public sealed record BusinessGatewayPermissionRequirement(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId);

public sealed record BusinessGatewayAuthorizationResult(
    bool IsAllowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason)
{
    public static BusinessGatewayAuthorizationResult Allowed(string principalId, string principalType, string loginName) =>
        new(true, principalId, principalType, loginName, null);

    public static BusinessGatewayAuthorizationResult Forbidden(string reason) =>
        new(false, null, null, null, reason);
}

public interface IBusinessGatewayAuthorizationClient
{
    Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken);
}

public static class BusinessGatewayPermissions
{
    public const string MasterDataProductsRead = "business.masterdata.products.read";
    public const string MasterDataProductsManage = "business.masterdata.products.manage";
    public const string MasterDataResourcesRead = "business.masterdata.resources.read";
    public const string InventoryLedgerRead = "business.inventory.ledger.read";
    public const string InventoryMovementsCreate = "business.inventory.movements.create";
    public const string InventoryCountsManage = "business.inventory.counts.manage";
    public const string QualityInspectionRecordsRead = "business.quality.inspection-records.read";
    public const string QualityInspectionRecordsCreate = "business.quality.inspection-records.create";
    public const string QualityNcrRead = "business.quality.ncr.read";
    public const string QualityNcrManage = "business.quality.ncr.manage";
    public const string MesWorkOrdersRead = "business.mes.work-orders.read";
    public const string MesWorkOrdersManage = "business.mes.work-orders.manage";
    public const string MesReportingWrite = "business.mes.reporting.write";
    public const string MesSchedulesManage = "business.mes.schedules.manage";
}

public sealed class HttpBusinessGatewayAuthorizationClient(HttpClient httpClient) : IBusinessGatewayAuthorizationClient
{
    public async Task<BusinessGatewayAuthorizationResult> CheckAsync(
        string bearerToken,
        BusinessGatewayPermissionRequirement requirement,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/iam/v1/authorization/check")
        {
            Content = JsonContent.Create(requirement),
        };
        request.Headers.Authorization = new("Bearer", bearerToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseDataEnvelope<BusinessGatewayAuthorizationResult>>(cancellationToken);
        return envelope?.Data ?? BusinessGatewayAuthorizationResult.Forbidden("iam-empty-response");
    }
}

public static class BusinessGatewayAuthorization
{
    public static async Task<string?> RequirePermissionAsync(
        HttpContext context,
        IBusinessGatewayAuthorizationClient auth,
        string permissionCode,
        string organizationId,
        string environmentId,
        CancellationToken cancellationToken)
    {
        var bearerToken = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(bearerToken))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.", cancellationToken);
            return null;
        }

        var principalOrganizationId = FirstClaimValue(context.User, "organizationId");
        var principalEnvironmentId = FirstClaimValue(context.User, "environmentId");
        if (!string.Equals(principalOrganizationId, organizationId, StringComparison.Ordinal)
            || !string.Equals(principalEnvironmentId, environmentId, StringComparison.Ordinal))
        {
            await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status403Forbidden, "Forbidden.", cancellationToken);
            return null;
        }

        var result = await auth.CheckAsync(
            bearerToken,
            new BusinessGatewayPermissionRequirement(permissionCode, organizationId, environmentId, null, null),
            cancellationToken);
        if (!result.IsAllowed)
        {
            await ResponseDataEndpointResults.WriteErrorAsync(context, StatusCodes.Status403Forbidden, "Forbidden.", cancellationToken);
            return null;
        }

        return bearerToken;
    }

    private static string? FirstClaimValue(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}

internal sealed record ResponseDataEnvelope<T>(T? Data, bool Success, string Message, int Code);
```

- [ ] **步骤 5：实现授权代理基类**

创建 `AuthorizedBusinessProxyEndpoint.cs`：

```csharp
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.BusinessGateway.Web.Application.Auth;

[Authorize(Policy = BusinessGatewayPolicies.BusinessConsoleAuthenticated)]
public abstract class AuthorizedBusinessProxyEndpoint<TRequest, TResponse>(
    IBusinessGatewayAuthorizationClient auth,
    string permissionCode) : Endpoint<TRequest, ResponseData<TResponse>>
    where TRequest : notnull
{
    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        var bearerToken = await BusinessGatewayAuthorization.RequirePermissionAsync(
            HttpContext,
            auth,
            permissionCode,
            OrganizationId(req),
            EnvironmentId(req),
            ct);
        if (bearerToken is null)
        {
            return;
        }

        var response = await ForwardAsync(req, ct);
        await ResponseDataEndpointResults.WriteDataAsync(HttpContext, StatusCode, response, ct);
    }

    protected virtual int StatusCode => StatusCodes.Status200OK;

    protected abstract string OrganizationId(TRequest request);

    protected abstract string EnvironmentId(TRequest request);

    protected abstract Task<TResponse> ForwardAsync(TRequest request, CancellationToken cancellationToken);
}
```

- [ ] **步骤 6：添加 HTTP 转发 handler**

创建 `AcceptLanguageForwardingHandler.cs`：

```csharp
namespace Nerv.IIP.BusinessGateway.Web.Application.Http;

public sealed class AcceptLanguageForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var acceptLanguage = httpContextAccessor.HttpContext?.Request.Headers.AcceptLanguage.ToString();
        if (!string.IsNullOrWhiteSpace(acceptLanguage) && !request.Headers.Contains("Accept-Language"))
        {
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
```

- [ ] **步骤 7：在 Program 中接入认证和 IAM client**

更新 `Program.cs`，添加：

```csharp
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Microsoft.Extensions.Http.Resilience;
```

在 `builder.Build()` 前添加服务：

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AcceptLanguageForwardingHandler>();
builder.Services.AddBusinessGatewayAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHttpClient<IBusinessGatewayAuthorizationClient, HttpBusinessGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
```

在 `UseFastEndpoints` 前添加 middleware：

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **步骤 8：替换 stub endpoint 的 auth attribute**

从 business-console endpoint stub 中移除 `[AllowAnonymous]`，改为继承 `AuthorizedBusinessProxyEndpoint<TRequest, TResponse>`，并使用包含 `OrganizationId` 和 `EnvironmentId` 的请求 record。首个测试目标使用：

```csharp
public sealed record BusinessConsoleListSkusRequest(string OrganizationId, string EnvironmentId, bool IncludeDisabled = false, int Take = 100);
public sealed record BusinessConsoleResourceListResponse(IReadOnlyCollection<BusinessConsoleResourceItem> Resources);
public sealed record BusinessConsoleResourceItem(string ResourceType, string Code, string DisplayName, bool Active, string SnapshotVersion);

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("listBusinessConsoleSkus")]
public sealed class ListBusinessConsoleSkusEndpoint(IBusinessGatewayAuthorizationClient auth)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListSkusRequest, BusinessConsoleResourceListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleListSkusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListSkusRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(
        BusinessConsoleListSkusRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(new BusinessConsoleResourceListResponse([]));
}
```

- [ ] **步骤 9：运行授权测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_console_endpoint
```

预期：通过。

- [ ] **步骤 10：提交认证基础**

运行：

```powershell
git add backend/gateway/BusinessGateway
git commit -m "feat: add business gateway authorization foundation"
```

## Task 3：实现业务服务 client 和代理测试

**文件：**
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- 创建：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- 创建：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **步骤 1：为内部 token 转发编写失败的代理测试**

创建 `BusinessGatewayProxyTests.cs`：

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessGatewayProxyTests
{
    [Fact]
    public async Task List_skus_uses_internal_service_token_for_downstream_business_service()
    {
        var masterData = new RecordingMasterDataClient();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBusinessGatewayAuthorizationClient>();
            services.AddSingleton<IBusinessGatewayAuthorizationClient>(FakeBusinessGatewayAuthorizationClient.Allowed());
            services.RemoveAll<IBusinessMasterDataClient>();
            services.AddSingleton<IBusinessMasterDataClient>(masterData);
            services.RemoveAll<IInternalServiceTokenProvider>();
            services.AddSingleton<IInternalServiceTokenProvider>(new TestInternalServiceTokenProvider("internal-test-token"));
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

        var response = await client.GetAsync("/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev&take=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("internal-test-token", masterData.LastInternalToken);
        Assert.Equal(new BusinessConsoleListResourcesRequest("org-001", "env-dev", "sku", false, 25), masterData.LastListResourcesRequest);
    }

    private sealed record TestInternalServiceTokenProvider(string BearerToken) : IInternalServiceTokenProvider;
}

internal sealed class RecordingMasterDataClient : IBusinessMasterDataClient
{
    public string? LastInternalToken { get; private set; }
    public BusinessConsoleListResourcesRequest? LastListResourcesRequest { get; private set; }

    public Task<BusinessConsoleResourceListResponse> ListResourcesAsync(
        string internalBearerToken,
        BusinessConsoleListResourcesRequest request,
        CancellationToken cancellationToken)
    {
        LastInternalToken = internalBearerToken;
        LastListResourcesRequest = request;
        return Task.FromResult(new BusinessConsoleResourceListResponse(
        [
            new BusinessConsoleResourceItem("sku", "SKU-001", "Demo SKU", true, "2026-05-24T00:00:00.0000000Z")
        ]));
    }

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(
        string internalBearerToken,
        BusinessConsoleCreateSkuRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
```

- [ ] **步骤 2：运行代理测试以验证失败**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter List_skus_uses_internal_service_token_for_downstream_business_service
```

预期：失败，因为业务服务 client 抽象尚不存在，或 endpoint 仍返回空响应。

- [ ] **步骤 3：添加 business-console DTO**

创建 `BusinessConsoleModels.cs`，其中包含以下 record：

```csharp
namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleResourceItem(string ResourceType, string Code, string DisplayName, bool Active, string SnapshotVersion);

public sealed record BusinessConsoleResourceListResponse(IReadOnlyCollection<BusinessConsoleResourceItem> Resources);

public sealed record BusinessConsoleListResourcesRequest(
    string OrganizationId,
    string EnvironmentId,
    string ResourceType,
    bool IncludeDisabled = false,
    int Take = 100);

public sealed record BusinessConsoleCreateSkuRequest(
    string OrganizationId,
    string EnvironmentId,
    string Code,
    string Name,
    string BaseUomCode,
    string Category,
    string MaterialType,
    string BatchTrackingPolicy,
    string SerialTrackingPolicy,
    string ShelfLifePolicyCode,
    string StorageConditionCode,
    string DefaultBarcodeRuleCode,
    bool QualityRequired,
    IReadOnlyCollection<string>? ComplianceTags);

public sealed record BusinessConsoleInventoryAvailabilityRequest(
    string OrganizationId,
    string EnvironmentId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string? LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleInventoryAvailabilityResponse(decimal OnHandQuantity, decimal AvailableQuantity, decimal FrozenQuantity);

public sealed record BusinessConsolePostStockMovementRequest(
    string OrganizationId,
    string EnvironmentId,
    string MovementType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string IdempotencyKey,
    string SkuCode,
    string UomCode,
    string SiteCode,
    string LocationCode,
    string? LotNo,
    string? SerialNo,
    string QualityStatus,
    string OwnerType,
    string? OwnerId,
    decimal Quantity);

public sealed record BusinessConsolePostStockMovementResponse(string MovementId, decimal OnHandQuantity, decimal AvailableQuantity);

public sealed record BusinessConsoleCreateStockCountTaskRequest(
    string OrganizationId,
    string EnvironmentId,
    string CountNo,
    string SiteCode,
    string LocationCode,
    string SkuCode,
    string UomCode,
    string? LotNo,
    string? SerialNo,
    string OwnerType,
    string? OwnerId);

public sealed record BusinessConsoleCreateStockCountTaskResponse(string CountTaskId, long ExpectedLedgerVersion);

public sealed record BusinessConsoleConfirmStockCountAdjustmentRequest(
    string OrganizationId,
    string EnvironmentId,
    decimal CountedQuantity,
    string IdempotencyKey);

public sealed record BusinessConsoleConfirmStockCountAdjustmentResponse(string MovementId, decimal VarianceQuantity, decimal OnHandQuantity);

public sealed record BusinessConsoleQualityListRequest(string OrganizationId, string EnvironmentId, string? Status = null, int Take = 100);

public sealed record BusinessConsoleQualityItem(string Id, string Code, string Status, string Summary);

public sealed record BusinessConsoleQualityListResponse(IReadOnlyCollection<BusinessConsoleQualityItem> Items);

public sealed record BusinessConsoleCreateInspectionRecordRequest(
    string OrganizationId,
    string EnvironmentId,
    string InspectionPlanId,
    string SourceDocumentType,
    string SourceDocumentId,
    string SkuCode,
    string Result,
    IReadOnlyCollection<BusinessConsoleInspectionCharacteristicResult> Characteristics);

public sealed record BusinessConsoleInspectionCharacteristicResult(string CharacteristicCode, string Result, string? MeasuredValue);

public sealed record BusinessConsoleCreateInspectionRecordResponse(string InspectionRecordId);

public sealed record BusinessConsoleNcrDispositionRequest(string OrganizationId, string EnvironmentId, string Disposition, string DecisionBy, string ExternalExecutionRef);

public sealed record BusinessConsoleNcrCloseRequest(string OrganizationId, string EnvironmentId, string ClosedBy, string ClosureNote);

public sealed record BusinessConsoleAcceptedResponse(bool Accepted);

public sealed record BusinessConsoleMesListRequest(string OrganizationId, string EnvironmentId, string? Status = null, int Take = 100);

public sealed record BusinessConsoleMesItem(string Id, string Code, string Status, string Summary);

public sealed record BusinessConsoleMesListResponse(IReadOnlyCollection<BusinessConsoleMesItem> Items);

public sealed record BusinessConsoleCreateRushWorkOrderRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderNo,
    string SkuCode,
    string ProductionVersionId,
    string WorkCenterCode,
    decimal PlannedQuantity,
    DateOnly DueDate);

public sealed record BusinessConsoleRunScheduleRequest(string OrganizationId, string EnvironmentId, DateOnly ScheduleDate, string WorkCenterCode);

public sealed record BusinessConsoleRecordProductionReportRequest(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationCode,
    decimal GoodQuantity,
    decimal DefectQuantity,
    decimal LaborHours,
    string ReportedBy);

public sealed record BusinessConsoleRecordProductionReportResponse(string ProductionReportId);
```

- [ ] **步骤 4：添加 client 接口和 HTTP 实现**

创建 `BusinessServiceClients.cs`。使用一个 helper 发送 `ResponseData` 并添加内部 bearer：

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nerv.IIP.Sdk.Core;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public interface IBusinessMasterDataClient
{
    Task<BusinessConsoleResourceListResponse> ListResourcesAsync(string internalBearerToken, BusinessConsoleListResourcesRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleResourceItem> CreateSkuAsync(string internalBearerToken, BusinessConsoleCreateSkuRequest request, CancellationToken cancellationToken);
}

public interface IBusinessInventoryClient
{
    Task<BusinessConsoleInventoryAvailabilityResponse> GetAvailabilityAsync(string internalBearerToken, BusinessConsoleInventoryAvailabilityRequest request, CancellationToken cancellationToken);

    Task<BusinessConsolePostStockMovementResponse> PostMovementAsync(string internalBearerToken, BusinessConsolePostStockMovementRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleCreateStockCountTaskResponse> CreateCountTaskAsync(string internalBearerToken, BusinessConsoleCreateStockCountTaskRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ConfirmCountAdjustmentAsync(string internalBearerToken, string countTaskId, BusinessConsoleConfirmStockCountAdjustmentRequest request, CancellationToken cancellationToken);
}

public interface IBusinessQualityClient
{
    Task<BusinessConsoleQualityListResponse> ListInspectionPlansAsync(string internalBearerToken, BusinessConsoleQualityListRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleCreateInspectionRecordResponse> CreateInspectionRecordAsync(string internalBearerToken, BusinessConsoleCreateInspectionRecordRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleQualityListResponse> ListNcrsAsync(string internalBearerToken, BusinessConsoleQualityListRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> SubmitNcrDispositionAsync(string internalBearerToken, string ncrId, BusinessConsoleNcrDispositionRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleAcceptedResponse> CloseNcrAsync(string internalBearerToken, string ncrId, BusinessConsoleNcrCloseRequest request, CancellationToken cancellationToken);
}

public interface IBusinessMesClient
{
    Task<BusinessConsoleMesListResponse> ListWorkOrdersAsync(string internalBearerToken, BusinessConsoleMesListRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleMesItem> CreateRushWorkOrderAsync(string internalBearerToken, BusinessConsoleCreateRushWorkOrderRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleMesListResponse> RunScheduleAsync(string internalBearerToken, BusinessConsoleRunScheduleRequest request, CancellationToken cancellationToken);

    Task<BusinessConsoleRecordProductionReportResponse> RecordProductionReportAsync(string internalBearerToken, BusinessConsoleRecordProductionReportRequest request, CancellationToken cancellationToken);
}

public abstract class BusinessServiceHttpClient(HttpClient httpClient)
{
    protected async Task<TResponse> SendAsync<TResponse>(
        string internalBearerToken,
        HttpMethod method,
        string requestUri,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", internalBearerToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessServiceProxyException((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }

        try
        {
            return await PlatformApiClient.ReadResponseDataAsync<TResponse>(response, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new BusinessServiceProxyException(StatusCodes.Status502BadGateway, "downstream-invalid-response", ex);
        }
    }
}

public sealed class BusinessServiceProxyException(int statusCode, string reason, Exception? innerException = null)
    : Exception(reason, innerException)
{
    public int StatusCode { get; } = statusCode;
}
```

然后在同一文件中添加具体 client。endpoint 路径必须与现有服务路由一致：

```csharp
public sealed class HttpBusinessMasterDataClient(HttpClient httpClient) : BusinessServiceHttpClient(httpClient), IBusinessMasterDataClient
{
    public Task<BusinessConsoleResourceListResponse> ListResourcesAsync(string internalBearerToken, BusinessConsoleListResourcesRequest request, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResourceListResponse>(
            internalBearerToken,
            HttpMethod.Get,
            $"/api/business/v1/master-data/resources?organizationId={Uri.EscapeDataString(request.OrganizationId)}&environmentId={Uri.EscapeDataString(request.EnvironmentId)}&resourceType={Uri.EscapeDataString(request.ResourceType)}&includeDisabled={request.IncludeDisabled.ToString().ToLowerInvariant()}&take={request.Take}",
            null,
            cancellationToken);

    public Task<BusinessConsoleResourceItem> CreateSkuAsync(string internalBearerToken, BusinessConsoleCreateSkuRequest request, CancellationToken cancellationToken) =>
        SendAsync<BusinessConsoleResourceItem>(internalBearerToken, HttpMethod.Post, "/api/business/v1/master-data/skus", request, cancellationToken);
}
```

- [ ] **步骤 5：注册 client 接口**

在 `Program.cs` 中注册每个下游 client：

```csharp
builder.Services.AddHttpClient<IBusinessMasterDataClient, HttpBusinessMasterDataClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["MasterData:BaseUrl"] ?? "http://localhost:5107");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();

builder.Services.AddHttpClient<IBusinessInventoryClient, HttpBusinessInventoryClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Inventory:BaseUrl"] ?? "http://localhost:5109");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();

builder.Services.AddHttpClient<IBusinessQualityClient, HttpBusinessQualityClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Quality:BaseUrl"] ?? "http://localhost:5110");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();

builder.Services.AddHttpClient<IBusinessMesClient, HttpBusinessMesClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Mes:BaseUrl"] ?? "http://localhost:5111");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
```

- [ ] **步骤 6：更新 SKU 列表 endpoint 以使用 client**

注入 `IBusinessMasterDataClient` 和 `IInternalServiceTokenProvider`：

```csharp
public sealed class ListBusinessConsoleSkusEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListSkusRequest, BusinessConsoleResourceListResponse>(
        auth,
        BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleListSkusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListSkusRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(
        BusinessConsoleListSkusRequest request,
        CancellationToken cancellationToken) =>
        masterData.ListResourcesAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleListResourcesRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "sku",
                request.IncludeDisabled,
                request.Take),
            cancellationToken);
}
```

- [ ] **步骤 7：运行代理测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter List_skus_uses_internal_service_token_for_downstream_business_service
```

预期：通过。

- [ ] **步骤 8：提交 client 基础**

运行：

```powershell
git add backend/gateway/BusinessGateway
git commit -m "feat: add business gateway service clients"
```

## Task 4：实现 MVP BusinessGateway facade endpoint

**文件：**
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Inventory/BusinessConsoleInventoryEndpoints.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Quality/BusinessConsoleQualityEndpoints.cs`
- 修改：`backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- 修改：`backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`

- [ ] **步骤 1：扩展授权测试以覆盖每项权限**

在 `BusinessGatewayAuthorizationTests.cs` 中为每个 endpoint 添加一行断言。在 `[Theory]` 中使用此表：

```csharp
public static TheoryData<string, string> PermissionCases => new()
{
    { "/api/business-console/v1/master-data/skus?organizationId=org-001&environmentId=env-dev", BusinessGatewayPermissions.MasterDataProductsRead },
    { "/api/business-console/v1/master-data/resources?organizationId=org-001&environmentId=env-dev&resourceType=site", BusinessGatewayPermissions.MasterDataResourcesRead },
    { "/api/business-console/v1/inventory/availability?organizationId=org-001&environmentId=env-dev&skuCode=SKU-001&uomCode=EA&siteCode=S1&qualityStatus=available&ownerType=owned", BusinessGatewayPermissions.InventoryLedgerRead },
    { "/api/business-console/v1/quality/ncrs?organizationId=org-001&environmentId=env-dev", BusinessGatewayPermissions.QualityNcrRead },
    { "/api/business-console/v1/mes/work-orders?organizationId=org-001&environmentId=env-dev", BusinessGatewayPermissions.MesWorkOrdersRead },
};
```

测试方法：

```csharp
[Theory]
[MemberData(nameof(PermissionCases))]
public async Task Business_console_get_endpoints_check_expected_permissions(string path, string permissionCode)
{
    var auth = FakeBusinessGatewayAuthorizationClient.Forbidden();
    await using var factory = CreateFactory(auth);
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", BusinessGatewayTestTokens.ValidAccessToken());

    var response = await client.GetAsync(path);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.Equal(permissionCode, auth.LastRequirement!.PermissionCode);
}
```

- [ ] **步骤 2：运行授权测试以验证失败**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_console_get_endpoints_check_expected_permissions
```

预期：失败，直到每个 endpoint 都接入预期权限。

- [ ] **步骤 3：实现 MasterData endpoint**

`BusinessConsoleMasterDataEndpoints.cs` 必须包含：

```csharp
using FastEndpoints;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Application.OpenApi;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.BusinessGateway.Web.Endpoints.MasterData;

public sealed record BusinessConsoleListSkusRequest(string OrganizationId, string EnvironmentId, bool IncludeDisabled = false, int Take = 100);

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("listBusinessConsoleSkus")]
public sealed class ListBusinessConsoleSkusEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListSkusRequest, BusinessConsoleResourceListResponse>(auth, BusinessGatewayPermissions.MasterDataProductsRead)
{
    protected override string OrganizationId(BusinessConsoleListSkusRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListSkusRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(BusinessConsoleListSkusRequest request, CancellationToken cancellationToken) =>
        masterData.ListResourcesAsync(
            tokenProvider.BearerToken,
            new BusinessConsoleListResourcesRequest(request.OrganizationId, request.EnvironmentId, "sku", request.IncludeDisabled, request.Take),
            cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpPost("/api/business-console/v1/master-data/skus")]
[BusinessGatewayOperationId("createBusinessConsoleSku")]
[Microsoft.AspNetCore.Mvc.ProducesResponseType(typeof(NetCorePal.Extensions.Dto.ResponseData<BusinessConsoleResourceItem>), StatusCodes.Status201Created)]
public sealed class CreateBusinessConsoleSkuEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleCreateSkuRequest, BusinessConsoleResourceItem>(auth, BusinessGatewayPermissions.MasterDataProductsManage)
{
    protected override int StatusCode => StatusCodes.Status201Created;

    protected override string OrganizationId(BusinessConsoleCreateSkuRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleCreateSkuRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceItem> ForwardAsync(BusinessConsoleCreateSkuRequest request, CancellationToken cancellationToken) =>
        masterData.CreateSkuAsync(tokenProvider.BearerToken, request, cancellationToken);
}

[Tags("Business Console MasterData")]
[HttpGet("/api/business-console/v1/master-data/resources")]
[BusinessGatewayOperationId("listBusinessConsoleMasterDataResources")]
public sealed class ListBusinessConsoleMasterDataResourcesEndpoint(
    IBusinessGatewayAuthorizationClient auth,
    IBusinessMasterDataClient masterData,
    IInternalServiceTokenProvider tokenProvider)
    : AuthorizedBusinessProxyEndpoint<BusinessConsoleListResourcesRequest, BusinessConsoleResourceListResponse>(auth, BusinessGatewayPermissions.MasterDataResourcesRead)
{
    protected override string OrganizationId(BusinessConsoleListResourcesRequest request) => request.OrganizationId;

    protected override string EnvironmentId(BusinessConsoleListResourcesRequest request) => request.EnvironmentId;

    protected override Task<BusinessConsoleResourceListResponse> ForwardAsync(BusinessConsoleListResourcesRequest request, CancellationToken cancellationToken) =>
        masterData.ListResourcesAsync(tokenProvider.BearerToken, request, cancellationToken);
}
```

- [ ] **步骤 4：实现 Inventory endpoint**

`BusinessConsoleInventoryEndpoints.cs` 必须包含 GET 可用量、POST 移动、POST 盘点任务和 POST 调整 endpoint。每个类均继承 `AuthorizedBusinessProxyEndpoint`，使用匹配的 `IBusinessInventoryClient` 方法并映射权限：

```text
getBusinessConsoleInventoryAvailability -> InventoryLedgerRead
postBusinessConsoleInventoryMovement -> InventoryMovementsCreate
createBusinessConsoleInventoryCountTask -> InventoryCountsManage
confirmBusinessConsoleInventoryCountAdjustment -> InventoryCountsManage
```

调整 endpoint 必须从路由读取 `countTaskId`：

```csharp
protected override Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ForwardAsync(
    BusinessConsoleConfirmStockCountAdjustmentRequest request,
    CancellationToken cancellationToken) =>
    inventory.ConfirmCountAdjustmentAsync(tokenProvider.BearerToken, Route<string>("countTaskId")!, request, cancellationToken);
```

- [ ] **步骤 5：实现 Quality endpoint**

`BusinessConsoleQualityEndpoints.cs` 必须包含：

```text
GET /api/business-console/v1/quality/inspection-plans -> listBusinessConsoleQualityInspectionPlans -> QualityInspectionRecordsRead
POST /api/business-console/v1/quality/inspection-records -> createBusinessConsoleQualityInspectionRecord -> QualityInspectionRecordsCreate
GET /api/business-console/v1/quality/ncrs -> listBusinessConsoleQualityNcrs -> QualityNcrRead
POST /api/business-console/v1/quality/ncrs/{ncrId}/disposition -> submitBusinessConsoleQualityNcrDisposition -> QualityNcrManage
POST /api/business-console/v1/quality/ncrs/{ncrId}/close -> closeBusinessConsoleQualityNcr -> QualityNcrManage
```

路由 endpoint 必须将 `Route<string>("ncrId")!` 传给 `IBusinessQualityClient`。

- [ ] **步骤 6：实现 MES endpoint**

`BusinessConsoleMesEndpoints.cs` 必须包含：

```text
GET /api/business-console/v1/mes/work-orders -> listBusinessConsoleMesWorkOrders -> MesWorkOrdersRead
POST /api/business-console/v1/mes/work-orders/rush -> createBusinessConsoleMesRushWorkOrder -> MesWorkOrdersManage
POST /api/business-console/v1/mes/schedules/run -> runBusinessConsoleMesSchedule -> MesSchedulesManage
POST /api/business-console/v1/mes/production-reports -> recordBusinessConsoleMesProductionReport -> MesReportingWrite
```

- [ ] **步骤 7：运行 BusinessGateway 测试**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
```

预期：通过。

- [ ] **步骤 8：提交 facade endpoint**

运行：

```powershell
git add backend/gateway/BusinessGateway
git commit -m "feat: expose business console facade endpoints"
```

## Task 5：在 Aspire、端口和 OpenAPI 导出中注册 BusinessGateway

**文件：**
- 修改：`infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`
- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`nerv.ps1`
- 修改：`scripts/export-gateway-openapi.ps1`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] **步骤 1：添加 AppHost 项目引用**

添加到 AppHost csproj：

```xml
<ProjectReference Include="..\..\..\backend\gateway\BusinessGateway\src\Nerv.IIP.BusinessGateway.Web\Nerv.IIP.BusinessGateway.Web.csproj" />
```

- [ ] **步骤 2：在端口 5119 注册 BusinessGateway**

在 AppHost `Program.cs` 的 PlatformGateway 之后添加：

```csharp
var businessGateway = builder.AddProject<Projects.Nerv_IIP_BusinessGateway_Web>("business-gateway")
    .WithHttpEndpoint(port: 5119, name: "http")
    .WithEnvironment("Iam__BaseUrl", iam.GetEndpoint("http"))
    .WithEnvironment("Iam__Jwt__SigningKey", iamJwtSigningKey)
    .WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
    .WithEnvironment("Inventory__BaseUrl", businessInventory.GetEndpoint("http"))
    .WithEnvironment("Quality__BaseUrl", businessQuality.GetEndpoint("http"))
    .WithEnvironment("Mes__BaseUrl", businessMes.GetEndpoint("http"))
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otelCollector.GetEndpoint("otlp-http"))
    .WithEnvironment("OpenTelemetry__Protocol", "HttpProtobuf")
    .WithReference(iam)
    .WithReference(businessMasterData)
    .WithReference(businessInventory)
    .WithReference(businessQuality)
    .WithReference(businessMes)
    .WithReference(redis)
    .WaitFor(iam)
    .WaitFor(businessMasterData)
    .WaitFor(businessInventory)
    .WaitFor(businessQuality)
    .WaitFor(businessMes)
    .WaitFor(redis);
```

- [ ] **步骤 3：在 AppHost 中添加 business-console Vite app 注册**

在 console Vite app 之后添加：

```csharp
builder.AddViteApp("business-console", "../../../frontend/apps/business-console")
    .WithHttpEndpoint(port: 5125, name: "http")
    .WithPnpm()
    .WithEnvironment("NERV_IIP_PLATFORM_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("NERV_IIP_BUSINESS_GATEWAY_URL", businessGateway.GetEndpoint("http"))
    .WithReference(gateway)
    .WithReference(businessGateway)
    .WaitFor(gateway)
    .WaitFor(businessGateway);
```

- [ ] **步骤 4：更新端口矩阵**

在 `nerv.ps1` 中添加：

```text
  5118 BusinessERP
  5119 BusinessGateway
  5125 BusinessConsole
```

- [ ] **步骤 5：扩展 OpenAPI 导出脚本**

更新 `scripts/export-gateway-openapi.ps1` 以导出两个 Gateway 文档。保留现有 `platform-gateway.v1.json` 行为，并添加：

```powershell
$businessGatewayUrl = "http://127.0.0.1:58205"
$businessGatewayProject = Join-Path $root "backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj"
$businessOutput = Join-Path $root "frontend/packages/api-client/openapi/business-gateway-console.v1.json"
```

在独立 job 中构建并运行 BusinessGateway：

```powershell
dotnet build $businessGatewayProject
$businessGatewayJob = Start-Job -ScriptBlock {
  param($project, $url)
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  dotnet run --project $project --no-build --no-launch-profile --urls $url
} -ArgumentList $businessGatewayProject, $businessGatewayUrl
Wait-Healthy "$businessGatewayUrl/health"
$businessOpenApiDocument = Invoke-RestMethod -Method Get -Uri "$businessGatewayUrl/swagger/v1/swagger.json"
$businessOpenApiDocument.servers = @([pscustomobject]@{ url = "" })
$businessOpenApiJson = ($businessOpenApiDocument | ConvertTo-Json -Depth 100) + [Environment]::NewLine
[System.IO.File]::WriteAllText($businessOutput, $businessOpenApiJson, $utf8NoBom)
Write-Host "Business Gateway OpenAPI exported to $businessOutput"
```

确保 `finally` 停止两个 job：

```powershell
foreach ($job in @($gatewayJob, $businessGatewayJob)) {
  if ($job) {
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
  }
}
```

- [ ] **步骤 6：运行脚本治理检查**

运行：

```powershell
scripts/check-script-governance.ps1
```

预期：通过。

- [ ] **步骤 7：运行 AppHost 构建**

运行：

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：通过。

- [ ] **步骤 8：提交注册和导出变更**

运行：

```powershell
git add infra/aspire/Nerv.IIP.AppHost nerv.ps1 scripts/export-gateway-openapi.ps1 docs/architecture/implementation-readiness.md
git commit -m "feat: register business gateway and console ports"
```

## Task 6：添加多输入 API client 生成

**文件：**
- 修改：`frontend/packages/api-client/openapi-ts.config.ts`
- 修改：`frontend/packages/api-client/src/transport/client-config.ts`
- 创建：`frontend/packages/api-client/src/business-console.ts`
- 修改：`frontend/packages/api-client/src/index.ts`
- 修改：`frontend/packages/api-client/src/generated-contract.test.ts`
- 修改：`frontend/vite.config.ts`

- [ ] **步骤 1：导出 OpenAPI snapshot**

运行：

```powershell
scripts/export-gateway-openapi.ps1
```

预期：写入：

```text
frontend/packages/api-client/openapi/platform-gateway.v1.json
frontend/packages/api-client/openapi/business-gateway-console.v1.json
```

- [ ] **步骤 2：更新 Hey API 配置以支持两个生成 job**

将 `openapi-ts.config.ts` 替换为数组配置：

```ts
const plugins = [
  '@hey-api/client-fetch',
  '@hey-api/typescript',
  '@hey-api/sdk',
  {
    name: '@pinia/colada',
    includeInEntry: true,
    queryKeys: { tags: true },
    queryOptions: { name: '{{name}}QueryOptions' },
    mutationOptions: { name: '{{name}}MutationOptions' },
  },
] as const

export default [
  {
    input: './openapi/platform-gateway.v1.json',
    output: { path: './src/generated' },
    plugins,
  },
  {
    input: './openapi/business-gateway-console.v1.json',
    output: { path: './src/generated/business-console' },
    plugins,
  },
]
```

- [ ] **步骤 3：运行生成任务**

运行：

```powershell
pnpm -C frontend generate:api
```

预期：生成的文件出现在 `frontend/packages/api-client/src/generated/business-console/` 中。

- [ ] **步骤 4：配置两个生成的 client**

修改 `client-config.ts`，使其导入两个生成的 client：

```ts
import { client as platformClient } from '../generated/client.gen'
import { client as businessConsoleClient } from '../generated/business-console/client.gen'
```

修改 `configureApiClient`，使用相同的 interceptor 配置两个 client。保持公开 `ConfigureApiClientOptions` 接口不变，并使用 helper：

```ts
const clients = [platformClient, businessConsoleClient]
```

为每个生成的 client 应用 base URL、请求 interceptor 和响应 interceptor。使用 interceptor ID 数组，使重复调用可从两个 client 中移除之前的 interceptor。

- [ ] **步骤 5：添加稳定的 business-console 导出**

创建 `business-console.ts`：

```ts
export {
  closeBusinessConsoleQualityNcrMutationOptions,
  confirmBusinessConsoleInventoryCountAdjustmentMutationOptions,
  createBusinessConsoleInventoryCountTaskMutationOptions,
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  createBusinessConsoleQualityInspectionRecordMutationOptions,
  createBusinessConsoleSkuMutationOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  listBusinessConsoleQualityInspectionPlansQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
  submitBusinessConsoleQualityNcrDispositionMutationOptions,
} from './generated/business-console/@pinia/colada.gen'

export {
  closeBusinessConsoleQualityNcr,
  confirmBusinessConsoleInventoryCountAdjustment,
  createBusinessConsoleInventoryCountTask,
  createBusinessConsoleMesRushWorkOrder,
  createBusinessConsoleQualityInspectionRecord,
  createBusinessConsoleSku,
  getBusinessConsoleInventoryAvailability,
  listBusinessConsoleMasterDataResources,
  listBusinessConsoleMesWorkOrders,
  listBusinessConsoleQualityInspectionPlans,
  listBusinessConsoleQualityNcrs,
  listBusinessConsoleSkus,
  postBusinessConsoleInventoryMovement,
  recordBusinessConsoleMesProductionReport,
  runBusinessConsoleMesSchedule,
  submitBusinessConsoleQualityNcrDisposition,
} from './generated/business-console/sdk.gen'

export type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleCreateRushWorkOrderRequest,
  BusinessConsoleCreateSkuRequest,
  BusinessConsoleCreateStockCountTaskRequest,
  BusinessConsoleInventoryAvailabilityResponse,
  BusinessConsoleMesItem,
  BusinessConsoleMesListResponse,
  BusinessConsolePostStockMovementRequest,
  BusinessConsolePostStockMovementResponse,
  BusinessConsoleQualityItem,
  BusinessConsoleQualityListResponse,
  BusinessConsoleRecordProductionReportRequest,
  BusinessConsoleResourceItem,
  BusinessConsoleResourceListResponse,
} from './generated/business-console/types.gen'
```

如果生成的类型名称包含 namespace 前缀，请使用准确的生成名称创建别名导出，并保留上面的公开别名。

- [ ] **步骤 6：从 index 重新导出**

添加：

```ts
export * from './business-console'
```

- [ ] **步骤 7：添加生成契约测试**

在 `generated-contract.test.ts` 中添加：

```ts
import {
  createBusinessConsoleSkuMutationOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
} from './business-console'

it('exports Business Console generated operations through stable api-client entry points', () => {
  expect(listBusinessConsoleSkusQueryOptions).toBeTypeOf('function')
  expect(createBusinessConsoleSkuMutationOptions).toBeTypeOf('function')
  expect(getBusinessConsoleInventoryAvailabilityQueryOptions).toBeTypeOf('function')
  expect(postBusinessConsoleInventoryMovementMutationOptions).toBeTypeOf('function')
  expect(listBusinessConsoleQualityNcrsQueryOptions).toBeTypeOf('function')
  expect(listBusinessConsoleMesWorkOrdersQueryOptions).toBeTypeOf('function')
})
```

- [ ] **步骤 8：更新前端 workspace 生成任务的输入**

在 `frontend/vite.config.ts` 中，将新的 OpenAPI 文件添加到 `workspace:generate-api` 的输入和输出：

```ts
input: [
  'packages/api-client/openapi-ts.config.ts',
  'packages/api-client/openapi/platform-gateway.v1.json',
  'packages/api-client/openapi/business-gateway-console.v1.json',
],
output: ['packages/api-client/src/generated/**'],
```

- [ ] **步骤 9：运行 api-client 测试**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

预期：通过。

- [ ] **步骤 10：提交 api-client 生成变更**

运行：

```powershell
git add frontend/packages/api-client frontend/vite.config.ts
git commit -m "feat: generate business console api client"
```

## Task 7：创建 Business Console app shell 和认证

**文件：**
- 创建：`frontend/apps/business-console` 下的所有基础文件
- 修改：`frontend/package.json`
- 修改：`frontend/vite.config.ts`
- 修改：当 Task 5 的 app 注册保留在同一实施分支中时，修改 `infra/aspire/Nerv.IIP.AppHost/Program.cs`

- [ ] **步骤 1：创建 business-console package**

创建 `frontend/apps/business-console/package.json`：

```json
{
  "name": "@nerv-iip/business-console",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vp dev --host 127.0.0.1 --port 5125",
    "build": "vue-tsc --noEmit -p tsconfig.json && vp build .",
    "e2e": "playwright test",
    "test": "vp test run src",
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
    "vue-i18n": "^11.4.4",
    "vue-router": "5.0.7"
  },
  "devDependencies": {
    "@playwright/test": "^1.60.0"
  }
}
```

- [ ] **步骤 2：添加 app 配置文件**

通过复制 console app 文件创建 `tsconfig.json`、`index.html`、`vite.config.ts`、`src/App.vue`、`src/main.ts` 和 `src/assets/main.css`，然后进行以下修改：

1. Package 名称和文档标题使用 `Nerv-IIP Business Console`。
2. Vite 开发服务器端口为 `5125`。
3. 将 `/api/business-console` 代理到 `process.env.NERV_IIP_BUSINESS_GATEWAY_URL ?? 'http://127.0.0.1:5119'`。
4. 将 `/api/console` 代理到 `process.env.NERV_IIP_PLATFORM_GATEWAY_URL ?? 'http://127.0.0.1:5100'`。
5. auth store 中的本地存储 key 为 `nerv-iip.business-console.auth`。

- [ ] **步骤 3：添加 router 和 auth 文件**

从 `frontend/apps/console/src` 复制以下内容，并且只调整 import：

```text
api/auth.ts
api/unauthorized.ts
router/index.ts
router/document-title.ts
router/redirects.ts
router/guards/auth.ts
stores/auth.ts
components/auth/LoginForm.vue
pages/login.vue
test/setup.ts
```

复制的 auth 仍使用来自 `@nerv-iip/api-client` 的 PlatformGateway Console Auth 生成操作。

- [ ] **步骤 4：添加 BusinessLayout 导航**

使用带业务导航的 `AppShell` 创建 `BusinessLayout.vue`：

```vue
<script setup lang="ts">
import type { NavItem } from '@nerv-iip/app-shell'
import { AppShell } from '@nerv-iip/app-shell'
import { BoxesIcon, ClipboardCheckIcon, FactoryIcon, PackageSearchIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const navItems = computed<NavItem[]>(() => [
  {
    title: 'MasterData',
    icon: BoxesIcon,
    items: [
      { title: 'SKUs', to: { path: '/master-data/skus' } },
    ],
  },
  {
    title: 'Inventory',
    icon: PackageSearchIcon,
    items: [
      { title: 'Availability', to: { path: '/inventory/availability' } },
      { title: 'Movements', to: { path: '/inventory/movements' } },
      { title: 'Counts', to: { path: '/inventory/counts' } },
    ],
  },
  {
    title: 'Quality',
    icon: ClipboardCheckIcon,
    items: [
      { title: 'Inspections', to: { path: '/quality/inspections' } },
      { title: 'NCRs', to: { path: '/quality/ncrs' } },
    ],
  },
  {
    title: 'MES',
    icon: FactoryIcon,
    items: [
      { title: 'Work orders', to: { path: '/mes/work-orders' } },
      { title: 'Schedules', to: { path: '/mes/schedules' } },
    ],
  },
])

const auth = useAuthStore()
const router = useRouter()

async function signOut() {
  await auth.logout()
  await router.push('/login')
}
</script>

<template>
  <AppShell
    title="Nerv-IIP Business"
    :nav-items="navItems"
    nav-label="Business"
    sign-out-label="Sign out"
    :user="auth.principal ? { name: auth.principal.loginName, email: auth.principal.email } : undefined"
    @sign-out="signOut"
  >
    <slot />
  </AppShell>
</template>
```

- [ ] **步骤 5：添加 dashboard 页面**

创建 `pages/index.vue`，其中包含指向 8 个 MVP 页面的链接，并设置 `requiresAuth: true`。

- [ ] **步骤 6：更新根 workspace 任务**

在 `frontend/package.json` 中，将测试脚本改为：

```json
"test": "vp run -w workspace:test"
```

在 `frontend/vite.config.ts` 中添加：

```ts
'workspace:test': {
  command: 'pnpm -r --if-present test',
  input: [
    'apps/**/src/**',
    'packages/**/src/**',
    'apps/**/vite.config.ts',
    'packages/**/tsconfig.json',
    'tsconfig.base.json'
  ],
}
```

更新 `workspace:build` 命令：

```ts
command: 'pnpm --filter @nerv-iip/console build && pnpm --filter @nerv-iip/business-console build',
```

将 `apps/business-console/dist/**` 和 `apps/business-console/typed-router.d.ts` 添加到 fmt/lint 忽略项。

- [ ] **步骤 7：运行 app typecheck**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
```

预期：通过。

- [ ] **步骤 8：提交 app shell**

运行：

```powershell
git add frontend/apps/business-console frontend/package.json frontend/vite.config.ts infra/aspire/Nerv.IIP.AppHost/Program.cs
git commit -m "feat: add business console app shell"
```

## Task 8：添加 Business Console composable

**文件：**
- 创建：`frontend/apps/business-console/src/composables/useBusinessMasterData.ts`
- 创建：`frontend/apps/business-console/src/composables/useBusinessInventory.ts`
- 创建：`frontend/apps/business-console/src/composables/useBusinessQuality.ts`
- 创建：`frontend/apps/business-console/src/composables/useBusinessMes.ts`
- 创建：匹配的 `*.test.ts` 文件

- [ ] **步骤 1：为 MasterData 编写 composable 测试**

创建使用模拟 `@nerv-iip/api-client` 的 `useBusinessMasterData.test.ts`。断言 `useBusinessSkus()` 调用 `listBusinessConsoleSkusQueryOptions({ query: { organizationId, environmentId, take: 100 } })` 并公开 `skus`。

- [ ] **步骤 2：实现 `useBusinessMasterData.ts`**

使用 Pinia Colada：

```ts
import {
  createBusinessConsoleSkuMutationOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  type BusinessConsoleCreateSkuRequest,
  type BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'

export interface BusinessContextFilters {
  organizationId: string
  environmentId: string
}

const defaultContext = () => reactive<BusinessContextFilters>({ organizationId: 'org-001', environmentId: 'env-dev' })

export function useBusinessSkus() {
  const filters = defaultContext()
  const queryCache = useQueryCache()
  const skusQuery = useQuery(() =>
    listBusinessConsoleSkusQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        take: 100,
      },
    }),
  )
  const createSkuMutation = useMutation({
    ...createBusinessConsoleSkuMutationOptions(),
    onSuccess() {
      void queryCache.invalidateQueries({ predicate: (entry) => JSON.stringify(entry.key).includes('listBusinessConsoleSkus') })
    },
  })

  return {
    createSku: (body: BusinessConsoleCreateSkuRequest) => createSkuMutation.mutateAsync({ body }),
    createSkuPending: createSkuMutation.isLoading,
    filters,
    refreshSkus: skusQuery.refetch,
    skus: computed<BusinessConsoleResourceItem[]>(() => skusQuery.data.value?.data?.resources ?? []),
    skusPending: skusQuery.isLoading,
  }
}

export function useBusinessMasterDataResources(resourceType: string) {
  const filters = defaultContext()
  const resourcesQuery = useQuery(() =>
    listBusinessConsoleMasterDataResourcesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        resourceType,
        take: 100,
      },
    }),
  )

  return {
    filters,
    resources: computed<BusinessConsoleResourceItem[]>(() => resourcesQuery.data.value?.data?.resources ?? []),
    resourcesPending: resourcesQuery.isLoading,
  }
}
```

- [ ] **步骤 3：为 Inventory 添加聚焦测试和实现**

`useBusinessInventory.ts` 公开：

```text
useInventoryAvailability()
useInventoryMovement()
useInventoryCounts()
```

每个函数封装生成的 query 或 mutation option，并返回 pending/error 状态和提交函数。

使用以下核心实现结构：

```ts
import {
  createBusinessConsoleInventoryCountTaskMutationOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
  type BusinessConsoleCreateStockCountTaskRequest,
  type BusinessConsolePostStockMovementRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

export function useInventoryAvailability() {
  const filters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skuCode: 'SKU-001',
    uomCode: 'EA',
    siteCode: 'S1',
    qualityStatus: 'available',
    ownerType: 'owned',
  })
  const availabilityQuery = useQuery(() =>
    getBusinessConsoleInventoryAvailabilityQueryOptions({ query: filters }),
  )

  return {
    availability: computed(() => availabilityQuery.data.value?.data),
    availabilityPending: availabilityQuery.isLoading,
    filters,
    refreshAvailability: availabilityQuery.refetch,
  }
}

export function useInventoryMovement() {
  const movementMutation = useMutation(postBusinessConsoleInventoryMovementMutationOptions())
  return {
    postMovement: (body: BusinessConsolePostStockMovementRequest) =>
      movementMutation.mutateAsync({ body }),
    postMovementPending: movementMutation.isLoading,
  }
}

export function useInventoryCounts() {
  const createCountTaskMutation = useMutation(createBusinessConsoleInventoryCountTaskMutationOptions())
  return {
    createCountTask: (body: BusinessConsoleCreateStockCountTaskRequest) =>
      createCountTaskMutation.mutateAsync({ body }),
    createCountTaskPending: createCountTaskMutation.isLoading,
  }
}
```

测试必须模拟 3 个生成的 option 函数，并断言可用量 query 默认值包含 `organizationId: 'org-001'` 和 `environmentId: 'env-dev'`。

- [ ] **步骤 4：为 Quality 添加聚焦测试和实现**

`useBusinessQuality.ts` 公开：

```text
useQualityInspectionPlans()
useQualityNcrs()
```

NCR composable 包含 `submitDisposition` 和 `closeNcr` mutation，并使 `listBusinessConsoleQualityNcrs` 失效。

使用以下核心实现结构：

```ts
import {
  closeBusinessConsoleQualityNcrMutationOptions,
  createBusinessConsoleQualityInspectionRecordMutationOptions,
  listBusinessConsoleQualityInspectionPlansQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  submitBusinessConsoleQualityNcrDispositionMutationOptions,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'

export function useQualityInspectionPlans() {
  const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', take: 100 })
  const plansQuery = useQuery(() =>
    listBusinessConsoleQualityInspectionPlansQueryOptions({ query: filters }),
  )
  const createRecordMutation = useMutation(createBusinessConsoleQualityInspectionRecordMutationOptions())

  return {
    createInspectionRecord: createRecordMutation.mutateAsync,
    createInspectionRecordPending: createRecordMutation.isLoading,
    filters,
    inspectionPlans: computed(() => plansQuery.data.value?.data?.items ?? []),
    inspectionPlansPending: plansQuery.isLoading,
  }
}

export function useQualityNcrs() {
  const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', take: 100 })
  const queryCache = useQueryCache()
  const ncrsQuery = useQuery(() => listBusinessConsoleQualityNcrsQueryOptions({ query: filters }))
  const submitDispositionMutation = useMutation({
    ...submitBusinessConsoleQualityNcrDispositionMutationOptions(),
    onSuccess: () => queryCache.invalidateQueries({ predicate: (entry) => JSON.stringify(entry.key).includes('listBusinessConsoleQualityNcrs') }),
  })
  const closeNcrMutation = useMutation({
    ...closeBusinessConsoleQualityNcrMutationOptions(),
    onSuccess: () => queryCache.invalidateQueries({ predicate: (entry) => JSON.stringify(entry.key).includes('listBusinessConsoleQualityNcrs') }),
  })

  return {
    closeNcr: closeNcrMutation.mutateAsync,
    closeNcrPending: closeNcrMutation.isLoading,
    filters,
    ncrs: computed(() => ncrsQuery.data.value?.data?.items ?? []),
    ncrsPending: ncrsQuery.isLoading,
    submitDisposition: submitDispositionMutation.mutateAsync,
    submitDispositionPending: submitDispositionMutation.isLoading,
  }
}
```

测试必须断言未成功的 envelope 公开空数组，并且 NCR query option 收到 `take: 100`。

- [ ] **步骤 5：为 MES 添加聚焦测试和实现**

`useBusinessMes.ts` 公开：

```text
useMesWorkOrders()
useMesSchedules()
```

工单 composable 包含 `createRushWorkOrder` 和 `recordProductionReport`。排程 composable 包含 `runSchedule`。

使用以下核心实现结构：

```ts
import {
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive } from 'vue'

export function useMesWorkOrders() {
  const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', take: 100 })
  const queryCache = useQueryCache()
  const workOrdersQuery = useQuery(() => listBusinessConsoleMesWorkOrdersQueryOptions({ query: filters }))
  const createRushMutation = useMutation({
    ...createBusinessConsoleMesRushWorkOrderMutationOptions(),
    onSuccess: () => queryCache.invalidateQueries({ predicate: (entry) => JSON.stringify(entry.key).includes('listBusinessConsoleMesWorkOrders') }),
  })
  const reportMutation = useMutation(recordBusinessConsoleMesProductionReportMutationOptions())

  return {
    createRushWorkOrder: createRushMutation.mutateAsync,
    createRushWorkOrderPending: createRushMutation.isLoading,
    filters,
    recordProductionReport: reportMutation.mutateAsync,
    recordProductionReportPending: reportMutation.isLoading,
    workOrders: computed(() => workOrdersQuery.data.value?.data?.items ?? []),
    workOrdersPending: workOrdersQuery.isLoading,
  }
}

export function useMesSchedules() {
  const runScheduleMutation = useMutation(runBusinessConsoleMesScheduleMutationOptions())
  return {
    runSchedule: runScheduleMutation.mutateAsync,
    runSchedulePending: runScheduleMutation.isLoading,
  }
}
```

测试必须断言：生成的 query 返回 `{ success: false }` 时，工单默认值为空数组。

- [ ] **步骤 6：运行 composable 测试**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console test -- src/composables
```

预期：通过。

- [ ] **步骤 7：提交 composable**

运行：

```powershell
git add frontend/apps/business-console/src/composables
git commit -m "feat: add business console data composables"
```

## Task 9：构建 MasterData 和 Inventory 页面

**文件：**
- 创建：`frontend/apps/business-console/src/pages/master-data/skus/index.vue`
- 创建：`frontend/apps/business-console/src/pages/inventory/availability/index.vue`
- 创建：`frontend/apps/business-console/src/pages/inventory/movements/index.vue`
- 创建：`frontend/apps/business-console/src/pages/inventory/counts/index.vue`

- [ ] **步骤 1：创建 SKU 页面**

使用来自 `@nerv-iip/ui` 的 `BusinessLayout`、`Table`、`Button`、`Input`、`Dialog`、`Select`、`Checkbox`、`Badge` 和 `Empty`。该页面列出 `skus`，打开创建 dialog，并调用 `createSku`。

- [ ] **步骤 2：创建可用量页面**

该页面提供紧凑的 organization、environment、SKU、UOM、site、location、lot 和 serial 筛选项，然后在紧凑的指标单元格中显示 `onHandQuantity`、`availableQuantity` 和 `frozenQuantity`。

- [ ] **步骤 3：创建移动页面**

该页面包含 movement type、source service、source document、idempotency key、SKU、UOM、site、location、quality status、owner 和 quantity 表单。提交时调用 `postBusinessConsoleInventoryMovement`。

- [ ] **步骤 4：创建盘点页面**

该页面包含两个部分：创建盘点任务和确认调整。调整表单要求填写盘点任务 ID、盘点数量和 idempotency key。

- [ ] **步骤 5：运行聚焦的前端检查**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
```

预期：通过。

- [ ] **步骤 6：提交 MasterData 和 Inventory 页面**

运行：

```powershell
git add frontend/apps/business-console/src/pages/master-data frontend/apps/business-console/src/pages/inventory
git commit -m "feat: add business master data and inventory pages"
```

## Task 10：构建 Quality 和 MES 页面

**文件：**
- 创建：`frontend/apps/business-console/src/pages/quality/inspections/index.vue`
- 创建：`frontend/apps/business-console/src/pages/quality/ncrs/index.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/work-orders/index.vue`
- 创建：`frontend/apps/business-console/src/pages/mes/schedules/index.vue`
- 创建：`frontend/apps/business-console/e2e/business-console.spec.ts`

- [ ] **步骤 1：创建 Quality 检验页面**

列出检验计划，并提供创建检验记录的表单。特性录入使用紧凑的重复行，包含特性代码、结果和测量值。

- [ ] **步骤 2：创建 Quality NCR 页面**

列出 NCR，在 `Sheet` 中打开选中的 NCR，并提供带确认的处置和关闭操作。不得从该页面直接修改 Inventory 或 WMS 状态。

- [ ] **步骤 3：创建 MES 工单页面**

列出工单、创建急单并记录生产报工。当可通过 BFF 获取生成数据时，成品入库请求仅作为只读信息显示。

- [ ] **步骤 4：创建 MES 排程页面**

为排程日期和工作中心提供规则排程运行控件。以表格/列表状态展示结果。不得渲染 Gantt 视图。

- [ ] **步骤 5：添加 Playwright 冒烟测试**

创建 `business-console.spec.ts`，模拟 auth 和 `/api/business-console/v1/**` 响应，然后访问：

```text
/master-data/skus
/inventory/availability
/quality/ncrs
/mes/work-orders
```

断言页面标题在桌面端 `1366x900` 和移动端 `390x844` 下可见。

- [ ] **步骤 6：运行聚焦检查**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

预期：通过。

- [ ] **步骤 7：浏览器可执行文件可用时运行 Playwright 冒烟测试**

运行：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

预期：通过。如果 Playwright 浏览器可执行文件不可用，请记录缺少可执行文件的准确消息，并在重新运行前将 `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` 设置为已安装的 Chromium。

- [ ] **步骤 8：提交 Quality 和 MES 页面**

运行：

```powershell
git add frontend/apps/business-console/src/pages/quality frontend/apps/business-console/src/pages/mes frontend/apps/business-console/e2e
git commit -m "feat: add business quality and mes pages"
```

## Task 11：最终验证和文档更新

**文件：**
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/frontend-structure.md`
- 修改：如果最终路径与本计划不同，则修改 `docs/architecture/repo-layout.md`

- [ ] **步骤 1：运行后端聚焦验证**

运行：

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：通过。

- [ ] **步骤 2：运行前端生成和检查**

运行：

```powershell
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

预期：通过。

- [ ] **步骤 3：运行脚本治理**

运行：

```powershell
scripts/check-script-governance.ps1
```

预期：通过。

- [ ] **步骤 4：时间允许时运行完整后端测试**

运行：

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
```

预期：通过。如果速度过慢或受本地环境问题阻塞，请运行聚焦的 BusinessGateway 测试和受影响业务服务测试，并报告未运行的准确命令。

- [ ] **步骤 5：依据实际差异更新就绪文档**

仅在读取 `git diff` 后更新 `implementation-readiness.md`。添加：

```text
BusinessGateway is available on local port 5119 and exposes Business Console OpenAPI for MasterData, Inventory, Quality and MES facade routes. Business Console is available on local port 5125 and consumes generated api-client business-console exports. #166 to #169 have first MVP pages for SKU, inventory availability/movement/counts, inspection/NCR and MES work orders/schedules without Gantt.
```

同时在命令列表中添加：

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

- [ ] **步骤 6：自行审核生成产物**

运行：

```powershell
git diff --stat
git diff --check
git status --short
```

预期：

1. 不存在空白错误。
2. 生成文件仅因 OpenAPI 发生变化而变化。
3. BusinessGateway 不直接引用 `backend/services/Business/*` 项目。
4. 除非用户明确要求处理，否则现有无关的 `skills-lock.json` 保持不变。

- [ ] **步骤 7：最终提交**

运行：

```powershell
git add backend/gateway/BusinessGateway backend/Nerv.IIP.sln infra/aspire/Nerv.IIP.AppHost nerv.ps1 scripts/export-gateway-openapi.ps1 frontend docs/architecture/implementation-readiness.md docs/architecture/api-contract-and-codegen.md docs/architecture/frontend-structure.md
git commit -m "feat: deliver business console mvp"
```

## 自查

规格覆盖：

1. 专用 `frontend/apps/business-console`：Task 7 至 10。
2. 专用 `backend/gateway/BusinessGateway`：Task 1 至 5。
3. `/api/business-console/v1/**` facade 和 OpenAPI：Task 1、4、5 和 6。
4. 生成的 api-client 稳定导出：Task 6。
5. #166 MasterData 页面：Task 9。
6. #167 Inventory 页面：Task 9。
7. #168 Quality 页面：Task 10。
8. #169 不含 Gantt 的 MES 页面：Task 10。
9. 验证和文档：Task 11。

类型一致性：

1. 后端文档、OpenAPI 测试和 api-client 导出中的 operation ID 均使用 `BusinessConsole` 前缀。
2. BusinessGateway 使用与 `docs/architecture/authorization-matrix.md` 匹配的 `BusinessGatewayPermissions` 常量。
3. BusinessGateway 下游调用使用 `IInternalServiceTokenProvider.BearerToken`；用户 bearer token 保持在 BFF/IAM 边界。
4. Business console app 使用 `@nerv-iip/api-client` 的稳定导出，不对生成文件进行 deep import。

边界检查：

1. BusinessGateway 不引用业务服务的 Web、Domain 或 Infrastructure 项目。
2. 不修改 PlatformGateway 以暴露业务 facade 路由。
3. 不修改 `frontend/apps/console` 以承载业务 CRUD 页面。
