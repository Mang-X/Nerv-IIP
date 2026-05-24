# Business Console MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the #166 to #169 Business Console MVP with a dedicated BusinessGateway, generated api-client entry, and `frontend/apps/business-console`.

**Architecture:** `frontend/apps/business-console` consumes generated `@nerv-iip/api-client` business-console exports. `backend/gateway/BusinessGateway` exposes `/api/business-console/v1/**`, checks IAM permissions with the user bearer token, and calls BusinessMasterData, Inventory, Quality, and MES with the internal service token. PlatformGateway and `frontend/apps/console` remain platform-control-plane only.

**Tech Stack:** .NET 10, FastEndpoints, FastEndpoints.Swagger, Microsoft.AspNetCore.Mvc.Testing, Vue 3, Vite, Vue Router file routes, Pinia, Pinia Colada, Hey API, shadcn-vue through `@nerv-iip/ui`.

---

## Source Documents

Read these before implementation:

1. `docs/architecture/implementation-readiness.md`
2. `docs/adr/0012-business-platform-domain-layering.md`
3. `docs/architecture/api-contract-and-codegen.md`
4. `docs/architecture/business-platform-domain-architecture.md`
5. `docs/architecture/frontend-structure.md`
6. `docs/superpowers/specs/2026-05-24-business-console-mvp-design.md`

## File Structure

Create these backend files:

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

Modify these backend and infrastructure files:

```text
backend/Nerv.IIP.sln
infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
infra/aspire/Nerv.IIP.AppHost/Program.cs
nerv.ps1
scripts/export-gateway-openapi.ps1
```

Create and modify these frontend files:

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

Modify these frontend package and workspace files:

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

Modify these docs after code is real:

```text
docs/architecture/api-contract-and-codegen.md
docs/architecture/frontend-structure.md
docs/architecture/implementation-readiness.md
```

## Task 1: Create BusinessGateway Skeleton And Failing Contract Tests

**Files:**
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/ResponseDataEndpointResults.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Health/HealthEndpoint.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Properties/launchSettings.json`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/appsettings.json`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/appsettings.Development.json`
- Create: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj`
- Create: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create project directories and SDK projects**

Run:

```powershell
New-Item -ItemType Directory -Force backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web | Out-Null
New-Item -ItemType Directory -Force backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests | Out-Null
dotnet new web -n Nerv.IIP.BusinessGateway.Web -o backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web
dotnet new xunit -n Nerv.IIP.BusinessGateway.Web.Tests -o backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests
dotnet sln backend/Nerv.IIP.sln add backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
```

Expected: the two projects are created and added to the backend solution.

- [ ] **Step 2: Replace the BusinessGateway web csproj**

Use this content:

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

- [ ] **Step 3: Replace the BusinessGateway test csproj**

Use this content:

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

- [ ] **Step 4: Write the first failing OpenAPI test**

Create `BusinessGatewayOpenApiTests.cs`:

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

- [ ] **Step 5: Run the failing OpenAPI test**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_gateway_exports_openapi_document_with_stable_business_console_operation_ids
```

Expected: FAIL because `/swagger/v1/swagger.json` or the business-console paths are not implemented.

- [ ] **Step 6: Add minimal Program, response writer and health endpoint**

Use this `Program.cs`:

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

Use this `ResponseDataEndpointResults.cs`:

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

Use this `HealthEndpoint.cs`:

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

Use this `BusinessGatewayOperationIdAttribute.cs`:

```csharp
namespace Nerv.IIP.BusinessGateway.Web.Application.OpenApi;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BusinessGatewayOperationIdAttribute(string operationId) : Attribute
{
    public string OperationId { get; } = operationId;
}
```

Use this `BusinessGatewayOperationIdConvention.cs`:

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

- [ ] **Step 7: Add temporary endpoint stubs that return 501**

Create endpoint classes for every operation ID asserted in the test. Each class should use `[HttpGet]` or `[HttpPost]`, `[AllowAnonymous]` only for this skeleton task, and `ThrowError("not-implemented", StatusCodes.Status501NotImplemented)`. This keeps OpenAPI stable before auth/proxy behavior lands.

Example for `ListBusinessConsoleSkusEndpoint`:

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

Create concrete endpoint classes with the same attributes, route shape and `Send.ErrorsAsync(StatusCodes.Status501NotImplemented, ct)` body for:

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

- [ ] **Step 8: Run the OpenAPI test again**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_gateway_exports_openapi_document_with_stable_business_console_operation_ids
```

Expected: PASS.

- [ ] **Step 9: Commit the backend skeleton**

Run:

```powershell
git add backend/gateway/BusinessGateway backend/Nerv.IIP.sln
git commit -m "feat: add business gateway skeleton"
```

## Task 2: Add BusinessGateway Auth, IAM Authorization And Internal Proxy Foundation

**Files:**
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthentication.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/AuthorizedBusinessProxyEndpoint.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Http/AcceptLanguageForwardingHandler.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- Create: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayTestTokens.cs`
- Create: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`

- [ ] **Step 1: Write failing authorization tests**

Create `BusinessGatewayAuthorizationTests.cs`:

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

- [ ] **Step 2: Write test token helper**

Create `BusinessGatewayTestTokens.cs`:

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

- [ ] **Step 3: Run tests to verify failure**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_console_endpoint
```

Expected: FAIL because BusinessGateway auth types and policies do not exist.

- [ ] **Step 4: Implement authentication and permission types**

Create `BusinessGatewayAuthentication.cs`:

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

Create `BusinessGatewayAuthorization.cs` with permission constants:

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

- [ ] **Step 5: Implement authorized proxy base class**

Create `AuthorizedBusinessProxyEndpoint.cs`:

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

- [ ] **Step 6: Add HTTP forwarding handler**

Create `AcceptLanguageForwardingHandler.cs`:

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

- [ ] **Step 7: Wire Program authentication and IAM client**

Update `Program.cs` to add:

```csharp
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.Http;
using Microsoft.Extensions.Http.Resilience;
```

Add services before `builder.Build()`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<AcceptLanguageForwardingHandler>();
builder.Services.AddBusinessGatewayAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHttpClient<IBusinessGatewayAuthorizationClient, HttpBusinessGatewayAuthorizationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102");
}).AddHttpMessageHandler<AcceptLanguageForwardingHandler>().AddStandardResilienceHandler();
```

Add middleware before `UseFastEndpoints`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

- [ ] **Step 8: Replace stub endpoint auth attributes**

Remove `[AllowAnonymous]` from business-console endpoint stubs and inherit from `AuthorizedBusinessProxyEndpoint<TRequest, TResponse>` with a request record containing `OrganizationId` and `EnvironmentId`. For the first test target, use:

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

- [ ] **Step 9: Run authorization tests**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_console_endpoint
```

Expected: PASS.

- [ ] **Step 10: Commit auth foundation**

Run:

```powershell
git add backend/gateway/BusinessGateway
git commit -m "feat: add business gateway authorization foundation"
```

## Task 3: Implement Business Service Clients And Proxy Tests

**Files:**
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- Create: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **Step 1: Write failing proxy test for internal token forwarding**

Create `BusinessGatewayProxyTests.cs`:

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

- [ ] **Step 2: Run proxy test to verify failure**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter List_skus_uses_internal_service_token_for_downstream_business_service
```

Expected: FAIL because the business service client abstractions do not exist or the endpoint still returns an empty response.

- [ ] **Step 3: Add business-console DTOs**

Create `BusinessConsoleModels.cs` containing these records:

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

- [ ] **Step 4: Add client interfaces and HTTP implementation**

Create `BusinessServiceClients.cs`. Use one helper to send `ResponseData` and add internal bearer:

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

Then add concrete clients in the same file. The endpoint paths must match existing service routes:

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

- [ ] **Step 5: Register client interfaces**

In `Program.cs`, register each downstream client:

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

- [ ] **Step 6: Update List SKUs endpoint to use the client**

Inject `IBusinessMasterDataClient` and `IInternalServiceTokenProvider`:

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

- [ ] **Step 7: Run proxy tests**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter List_skus_uses_internal_service_token_for_downstream_business_service
```

Expected: PASS.

- [ ] **Step 8: Commit client foundation**

Run:

```powershell
git add backend/gateway/BusinessGateway
git commit -m "feat: add business gateway service clients"
```

## Task 4: Implement MVP BusinessGateway Facade Endpoints

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Inventory/BusinessConsoleInventoryEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Quality/BusinessConsoleQualityEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`

- [ ] **Step 1: Extend authorization tests for every permission**

Add one assertion row per endpoint in `BusinessGatewayAuthorizationTests.cs`. Use this table in a `[Theory]`:

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

The test method:

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

- [ ] **Step 2: Run authorization test to verify failure**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter Business_console_get_endpoints_check_expected_permissions
```

Expected: FAIL until every endpoint is wired with the expected permission.

- [ ] **Step 3: Implement MasterData endpoints**

`BusinessConsoleMasterDataEndpoints.cs` must include:

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

- [ ] **Step 4: Implement Inventory endpoints**

`BusinessConsoleInventoryEndpoints.cs` must include GET availability, POST movement, POST count task and POST adjustment endpoints. Each class inherits `AuthorizedBusinessProxyEndpoint`, uses the matching `IBusinessInventoryClient` method, and maps permissions:

```text
getBusinessConsoleInventoryAvailability -> InventoryLedgerRead
postBusinessConsoleInventoryMovement -> InventoryMovementsCreate
createBusinessConsoleInventoryCountTask -> InventoryCountsManage
confirmBusinessConsoleInventoryCountAdjustment -> InventoryCountsManage
```

The adjustment endpoint must read `countTaskId` from the route:

```csharp
protected override Task<BusinessConsoleConfirmStockCountAdjustmentResponse> ForwardAsync(
    BusinessConsoleConfirmStockCountAdjustmentRequest request,
    CancellationToken cancellationToken) =>
    inventory.ConfirmCountAdjustmentAsync(tokenProvider.BearerToken, Route<string>("countTaskId")!, request, cancellationToken);
```

- [ ] **Step 5: Implement Quality endpoints**

`BusinessConsoleQualityEndpoints.cs` must include:

```text
GET /api/business-console/v1/quality/inspection-plans -> listBusinessConsoleQualityInspectionPlans -> QualityInspectionRecordsRead
POST /api/business-console/v1/quality/inspection-records -> createBusinessConsoleQualityInspectionRecord -> QualityInspectionRecordsCreate
GET /api/business-console/v1/quality/ncrs -> listBusinessConsoleQualityNcrs -> QualityNcrRead
POST /api/business-console/v1/quality/ncrs/{ncrId}/disposition -> submitBusinessConsoleQualityNcrDisposition -> QualityNcrManage
POST /api/business-console/v1/quality/ncrs/{ncrId}/close -> closeBusinessConsoleQualityNcr -> QualityNcrManage
```

Route endpoints must pass `Route<string>("ncrId")!` to `IBusinessQualityClient`.

- [ ] **Step 6: Implement MES endpoints**

`BusinessConsoleMesEndpoints.cs` must include:

```text
GET /api/business-console/v1/mes/work-orders -> listBusinessConsoleMesWorkOrders -> MesWorkOrdersRead
POST /api/business-console/v1/mes/work-orders/rush -> createBusinessConsoleMesRushWorkOrder -> MesWorkOrdersManage
POST /api/business-console/v1/mes/schedules/run -> runBusinessConsoleMesSchedule -> MesSchedulesManage
POST /api/business-console/v1/mes/production-reports -> recordBusinessConsoleMesProductionReport -> MesReportingWrite
```

- [ ] **Step 7: Run BusinessGateway tests**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
```

Expected: PASS.

- [ ] **Step 8: Commit facade endpoints**

Run:

```powershell
git add backend/gateway/BusinessGateway
git commit -m "feat: expose business console facade endpoints"
```

## Task 5: Register BusinessGateway In Aspire, Ports And OpenAPI Export

**Files:**
- Modify: `infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `nerv.ps1`
- Modify: `scripts/export-gateway-openapi.ps1`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Add AppHost project reference**

Add to AppHost csproj:

```xml
<ProjectReference Include="..\..\..\backend\gateway\BusinessGateway\src\Nerv.IIP.BusinessGateway.Web\Nerv.IIP.BusinessGateway.Web.csproj" />
```

- [ ] **Step 2: Register BusinessGateway on port 5119**

In AppHost `Program.cs`, add after PlatformGateway:

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

- [ ] **Step 3: Add business-console Vite app registration in AppHost**

After the console Vite app, add:

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

- [ ] **Step 4: Update port matrix**

In `nerv.ps1`, add:

```text
  5118 BusinessERP
  5119 BusinessGateway
  5125 BusinessConsole
```

- [ ] **Step 5: Extend OpenAPI export script**

Update `scripts/export-gateway-openapi.ps1` to export both gateway documents. Preserve the existing `platform-gateway.v1.json` behavior and add:

```powershell
$businessGatewayUrl = "http://127.0.0.1:58205"
$businessGatewayProject = Join-Path $root "backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Nerv.IIP.BusinessGateway.Web.csproj"
$businessOutput = Join-Path $root "frontend/packages/api-client/openapi/business-gateway-console.v1.json"
```

Build and run BusinessGateway in its own job:

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

Ensure `finally` stops both jobs:

```powershell
foreach ($job in @($gatewayJob, $businessGatewayJob)) {
  if ($job) {
    Stop-Job $job -ErrorAction SilentlyContinue
    Remove-Job $job -Force -ErrorAction SilentlyContinue
  }
}
```

- [ ] **Step 6: Run script governance check**

Run:

```powershell
scripts/check-script-governance.ps1
```

Expected: PASS.

- [ ] **Step 7: Run AppHost build**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 8: Commit registration and export**

Run:

```powershell
git add infra/aspire/Nerv.IIP.AppHost nerv.ps1 scripts/export-gateway-openapi.ps1 docs/architecture/implementation-readiness.md
git commit -m "feat: register business gateway and console ports"
```

## Task 6: Add Multi-Input API Client Generation

**Files:**
- Modify: `frontend/packages/api-client/openapi-ts.config.ts`
- Modify: `frontend/packages/api-client/src/transport/client-config.ts`
- Create: `frontend/packages/api-client/src/business-console.ts`
- Modify: `frontend/packages/api-client/src/index.ts`
- Modify: `frontend/packages/api-client/src/generated-contract.test.ts`
- Modify: `frontend/vite.config.ts`

- [ ] **Step 1: Export OpenAPI snapshots**

Run:

```powershell
scripts/export-gateway-openapi.ps1
```

Expected: writes:

```text
frontend/packages/api-client/openapi/platform-gateway.v1.json
frontend/packages/api-client/openapi/business-gateway-console.v1.json
```

- [ ] **Step 2: Update Hey API config for two generation jobs**

Replace `openapi-ts.config.ts` with an array config:

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

- [ ] **Step 3: Run generation**

Run:

```powershell
pnpm -C frontend generate:api
```

Expected: generated files appear in `frontend/packages/api-client/src/generated/business-console/`.

- [ ] **Step 4: Configure both generated clients**

Modify `client-config.ts` so it imports both generated clients:

```ts
import { client as platformClient } from '../generated/client.gen'
import { client as businessConsoleClient } from '../generated/business-console/client.gen'
```

Change `configureApiClient` to configure both clients with the same interceptors. Keep the public `ConfigureApiClientOptions` interface unchanged, and use a helper:

```ts
const clients = [platformClient, businessConsoleClient]
```

Apply base URL, request interceptors and response interceptors to each generated client. Use arrays for interceptor IDs so repeated calls eject previous interceptors on both clients.

- [ ] **Step 5: Add stable business-console export**

Create `business-console.ts`:

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

If generated type names include namespace prefixes, create alias exports with the exact generated names and keep the public aliases above.

- [ ] **Step 6: Re-export from index**

Add:

```ts
export * from './business-console'
```

- [ ] **Step 7: Add generated contract tests**

In `generated-contract.test.ts`, add:

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

- [ ] **Step 8: Update frontend workspace generation task inputs**

In `frontend/vite.config.ts`, add the new OpenAPI file to `workspace:generate-api` input and output:

```ts
input: [
  'packages/api-client/openapi-ts.config.ts',
  'packages/api-client/openapi/platform-gateway.v1.json',
  'packages/api-client/openapi/business-gateway-console.v1.json',
],
output: ['packages/api-client/src/generated/**'],
```

- [ ] **Step 9: Run api-client tests**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

Expected: PASS.

- [ ] **Step 10: Commit api-client generation**

Run:

```powershell
git add frontend/packages/api-client frontend/vite.config.ts
git commit -m "feat: generate business console api client"
```

## Task 7: Create Business Console App Shell And Auth

**Files:**
- Create: all base files under `frontend/apps/business-console`
- Modify: `frontend/package.json`
- Modify: `frontend/vite.config.ts`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs` when the Task 5 app registration is kept in the same implementation branch

- [ ] **Step 1: Create business-console package**

Create `frontend/apps/business-console/package.json`:

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

- [ ] **Step 2: Add app config files**

Create `tsconfig.json`, `index.html`, `vite.config.ts`, `src/App.vue`, `src/main.ts` and `src/assets/main.css` by copying the console app files, then make these changes:

1. Package name and document title use `Nerv-IIP Business Console`.
2. Vite dev server port is `5125`.
3. Proxy `/api/business-console` to `process.env.NERV_IIP_BUSINESS_GATEWAY_URL ?? 'http://127.0.0.1:5119'`.
4. Proxy `/api/console` to `process.env.NERV_IIP_PLATFORM_GATEWAY_URL ?? 'http://127.0.0.1:5100'`.
5. Local storage key in auth store is `nerv-iip.business-console.auth`.

- [ ] **Step 3: Add router and auth files**

Copy these from `frontend/apps/console/src` and adjust imports only:

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

The copied auth still uses PlatformGateway Console Auth generated operations from `@nerv-iip/api-client`.

- [ ] **Step 4: Add BusinessLayout navigation**

Create `BusinessLayout.vue` using `AppShell` with business nav:

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

- [ ] **Step 5: Add dashboard page**

Create `pages/index.vue` with links to the eight MVP pages and `requiresAuth: true`.

- [ ] **Step 6: Update root workspace tasks**

In `frontend/package.json`, change test script to:

```json
"test": "vp run -w workspace:test"
```

In `frontend/vite.config.ts`, add:

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

Update `workspace:build` command:

```ts
command: 'pnpm --filter @nerv-iip/console build && pnpm --filter @nerv-iip/business-console build',
```

Add `apps/business-console/dist/**` and `apps/business-console/typed-router.d.ts` to fmt/lint ignores.

- [ ] **Step 7: Run app typecheck**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
```

Expected: PASS.

- [ ] **Step 8: Commit app shell**

Run:

```powershell
git add frontend/apps/business-console frontend/package.json frontend/vite.config.ts infra/aspire/Nerv.IIP.AppHost/Program.cs
git commit -m "feat: add business console app shell"
```

## Task 8: Add Business Console Composables

**Files:**
- Create: `frontend/apps/business-console/src/composables/useBusinessMasterData.ts`
- Create: `frontend/apps/business-console/src/composables/useBusinessInventory.ts`
- Create: `frontend/apps/business-console/src/composables/useBusinessQuality.ts`
- Create: `frontend/apps/business-console/src/composables/useBusinessMes.ts`
- Create: matching `*.test.ts` files

- [ ] **Step 1: Write composable test for MasterData**

Create `useBusinessMasterData.test.ts` with a mocked `@nerv-iip/api-client`. Assert that `useBusinessSkus()` calls `listBusinessConsoleSkusQueryOptions({ query: { organizationId, environmentId, take: 100 } })` and exposes `skus`.

- [ ] **Step 2: Implement `useBusinessMasterData.ts`**

Use Pinia Colada:

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

- [ ] **Step 3: Add focused tests and implementation for Inventory**

`useBusinessInventory.ts` exposes:

```text
useInventoryAvailability()
useInventoryMovement()
useInventoryCounts()
```

Each function wraps the generated query or mutation options and returns pending/error state plus submit functions.

Use this core implementation shape:

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

The test must mock the three generated option functions and assert that availability query defaults include `organizationId: 'org-001'` and `environmentId: 'env-dev'`.

- [ ] **Step 4: Add focused tests and implementation for Quality**

`useBusinessQuality.ts` exposes:

```text
useQualityInspectionPlans()
useQualityNcrs()
```

The NCR composable includes `submitDisposition` and `closeNcr` mutations and invalidates `listBusinessConsoleQualityNcrs`.

Use this core implementation shape:

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

The test must assert that unsuccessful envelopes expose empty arrays and that the NCR query option receives `take: 100`.

- [ ] **Step 5: Add focused tests and implementation for MES**

`useBusinessMes.ts` exposes:

```text
useMesWorkOrders()
useMesSchedules()
```

The work-order composable includes `createRushWorkOrder` and `recordProductionReport`. The schedule composable includes `runSchedule`.

Use this core implementation shape:

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

The test must assert that work orders default to an empty array when the generated query returns `{ success: false }`.

- [ ] **Step 6: Run composable tests**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console test -- src/composables
```

Expected: PASS.

- [ ] **Step 7: Commit composables**

Run:

```powershell
git add frontend/apps/business-console/src/composables
git commit -m "feat: add business console data composables"
```

## Task 9: Build MasterData And Inventory Pages

**Files:**
- Create: `frontend/apps/business-console/src/pages/master-data/skus/index.vue`
- Create: `frontend/apps/business-console/src/pages/inventory/availability/index.vue`
- Create: `frontend/apps/business-console/src/pages/inventory/movements/index.vue`
- Create: `frontend/apps/business-console/src/pages/inventory/counts/index.vue`

- [ ] **Step 1: Create SKU page**

Use `BusinessLayout`, `Table`, `Button`, `Input`, `Dialog`, `Select`, `Checkbox`, `Badge` and `Empty` from `@nerv-iip/ui`. The page lists `skus`, opens a create dialog and calls `createSku`.

- [ ] **Step 2: Create availability page**

The page has compact filters for organization, environment, SKU, UOM, site, location, lot and serial, then shows `onHandQuantity`, `availableQuantity` and `frozenQuantity` in dense metric cells.

- [ ] **Step 3: Create movements page**

The page has a form for movement type, source service, source document, idempotency key, SKU, UOM, site, location, quality status, owner and quantity. Submit calls `postBusinessConsoleInventoryMovement`.

- [ ] **Step 4: Create counts page**

The page has two sections: count task creation and adjustment confirmation. Adjustment form requires count task ID, counted quantity and idempotency key.

- [ ] **Step 5: Run focused frontend checks**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
```

Expected: PASS.

- [ ] **Step 6: Commit MasterData and Inventory pages**

Run:

```powershell
git add frontend/apps/business-console/src/pages/master-data frontend/apps/business-console/src/pages/inventory
git commit -m "feat: add business master data and inventory pages"
```

## Task 10: Build Quality And MES Pages

**Files:**
- Create: `frontend/apps/business-console/src/pages/quality/inspections/index.vue`
- Create: `frontend/apps/business-console/src/pages/quality/ncrs/index.vue`
- Create: `frontend/apps/business-console/src/pages/mes/work-orders/index.vue`
- Create: `frontend/apps/business-console/src/pages/mes/schedules/index.vue`
- Create: `frontend/apps/business-console/e2e/business-console.spec.ts`

- [ ] **Step 1: Create Quality inspections page**

List inspection plans and provide a create inspection record form. Keep characteristic entry to a compact repeated row with characteristic code, result and measured value.

- [ ] **Step 2: Create Quality NCR page**

List NCRs, open selected NCR in a `Sheet`, and provide disposition and close actions with confirmation. Do not directly mutate inventory or WMS state from this page.

- [ ] **Step 3: Create MES work-orders page**

List work orders, create rush work order, and record production report. Keep finished-goods receipt request as read-only visibility when generated data is available through the BFF.

- [ ] **Step 4: Create MES schedules page**

Provide rule schedule run controls for schedule date and work center. Show results as table/list state. Do not render a Gantt view.

- [ ] **Step 5: Add Playwright smoke test**

Create `business-console.spec.ts` that mocks auth and `/api/business-console/v1/**` responses, then visits:

```text
/master-data/skus
/inventory/availability
/quality/ncrs
/mes/work-orders
```

Assert the page heading is visible at desktop `1366x900` and mobile `390x844`.

- [ ] **Step 6: Run focused checks**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

Expected: PASS.

- [ ] **Step 7: Run Playwright smoke when browser executable is available**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

Expected: PASS. If Playwright browser executable is unavailable, record the exact missing executable message and set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to an installed Chromium before rerunning.

- [ ] **Step 8: Commit Quality and MES pages**

Run:

```powershell
git add frontend/apps/business-console/src/pages/quality frontend/apps/business-console/src/pages/mes frontend/apps/business-console/e2e
git commit -m "feat: add business quality and mes pages"
```

## Task 11: Final Verification And Documentation

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/frontend-structure.md`
- Modify: `docs/architecture/repo-layout.md` if final paths differ from this plan

- [ ] **Step 1: Run backend focused verification**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 2: Run frontend generation and checks**

Run:

```powershell
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

Expected: PASS.

- [ ] **Step 3: Run script governance**

Run:

```powershell
scripts/check-script-governance.ps1
```

Expected: PASS.

- [ ] **Step 4: Run full backend tests when time permits**

Run:

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
```

Expected: PASS. If this is too slow or blocked by a local environment issue, run the focused BusinessGateway and affected business service tests and report the exact command not run.

- [ ] **Step 5: Update readiness docs from actual diff**

Update `implementation-readiness.md` only after reading `git diff`. Add:

```text
BusinessGateway is available on local port 5119 and exposes Business Console OpenAPI for MasterData, Inventory, Quality and MES facade routes. Business Console is available on local port 5125 and consumes generated api-client business-console exports. #166 to #169 have first MVP pages for SKU, inventory availability/movement/counts, inspection/NCR and MES work orders/schedules without Gantt.
```

Also update the command list with:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

- [ ] **Step 6: Self-review generated artifacts**

Run:

```powershell
git diff --stat
git diff --check
git status --short
```

Expected:

1. No whitespace errors.
2. Generated files changed only because OpenAPI changed.
3. No direct references from BusinessGateway to `backend/services/Business/*` projects.
4. Existing unrelated `skills-lock.json` remains untouched unless the user explicitly asks to handle it.

- [ ] **Step 7: Final commit**

Run:

```powershell
git add backend/gateway/BusinessGateway backend/Nerv.IIP.sln infra/aspire/Nerv.IIP.AppHost nerv.ps1 scripts/export-gateway-openapi.ps1 frontend docs/architecture/implementation-readiness.md docs/architecture/api-contract-and-codegen.md docs/architecture/frontend-structure.md
git commit -m "feat: deliver business console mvp"
```

## Self-Review

Spec coverage:

1. Dedicated `frontend/apps/business-console`: Tasks 7 to 10.
2. Dedicated `backend/gateway/BusinessGateway`: Tasks 1 to 5.
3. `/api/business-console/v1/**` facade and OpenAPI: Tasks 1, 4, 5 and 6.
4. Generated api-client stable export: Task 6.
5. #166 MasterData pages: Task 9.
6. #167 Inventory pages: Task 9.
7. #168 Quality pages: Task 10.
8. #169 MES pages without Gantt: Task 10.
9. Verification and docs: Task 11.

Type consistency:

1. Operation IDs use the `BusinessConsole` prefix throughout backend docs, OpenAPI tests and api-client exports.
2. BusinessGateway uses `BusinessGatewayPermissions` constants matching `docs/architecture/authorization-matrix.md`.
3. BusinessGateway downstream calls use `IInternalServiceTokenProvider.BearerToken`; user bearer tokens stay at the BFF/IAM boundary.
4. Business console app consumes `@nerv-iip/api-client` stable exports and does not deep import generated files.

Boundary checks:

1. BusinessGateway does not reference business service Web, Domain or Infrastructure projects.
2. PlatformGateway is not modified to expose business facade routes.
3. `frontend/apps/console` is not modified to host business CRUD pages.
