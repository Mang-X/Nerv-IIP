using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Endpoints.Erp;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Erp;
using Nerv.IIP.ServiceAuth;

namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class ErpWorkOrderCostGatewaySeamTests
{
    private const string Token = "issue-2278-finance-token";
    private static readonly DateTimeOffset CompletedAt = DateTimeOffset.Parse("2026-08-31T08:00:00Z");

    // PublicContract (#2278): real ERP query and both HTTP endpoints; EF InMemory is not SQL/CAP evidence.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Real_erp_labor_facts_cross_both_http_endpoints_unchanged(bool hasOutputBasis)
    {
        await using var erp = CreateErpFactory();
        using var erpClient = erp.CreateClient();
        await SeedAsync(erp.Services, hasOutputBasis);
        using var directRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/business/v1/erp/finance/work-order-costs/WO-2278?pageNumber=1&pageSize=25");
        directRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        directRequest.Headers.Add("X-Organization-Id", "org-2278");
        directRequest.Headers.Add("X-Environment-Id", "env-2278");
        using var direct = await erpClient.SendAsync(directRequest);
        await using var gateway = CreateGatewayFactory(erp);
        using var browser = gateway.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-user");
        using var forwarded = await browser.GetAsync(
            "/api/business-console/v1/erp/finance/work-order-costs/WO-2278?organizationId=org-2278&environmentId=env-2278&pageNumber=1&pageSize=25");

        var directBody = await direct.Content.ReadAsStringAsync();
        var forwardedBody = await forwarded.Content.ReadAsStringAsync();
        Assert.True(direct.StatusCode == HttpStatusCode.OK, directBody);
        Assert.True(forwarded.StatusCode == HttpStatusCode.OK, forwardedBody);
        var expected = JsonNode.Parse(directBody)!["data"]!;
        var actual = JsonNode.Parse(forwardedBody)!["data"]!;
        Assert.True(JsonNode.DeepEquals(expected, actual), forwardedBody);
        Assert.Equal(2.5m, actual["actualLaborHours"]!.GetValue<decimal>());
        Assert.Equal(125m, actual["actualLaborCost"]!.GetValue<decimal>());
        Assert.Equal(0m, actual["capitalizationVarianceAmount"]!.GetValue<decimal>());
        Assert.Equal("notApplicable", actual["laborRateVarianceStatus"]!.GetValue<string>());
        Assert.Equal("actual_payroll_rate_not_modeled", actual["laborRateVarianceReason"]!.GetValue<string>());
        Assert.Equal(hasOutputBasis ? "available" : "unavailable", actual["laborVarianceStatus"]!.GetValue<string>());
        if (hasOutputBasis)
        {
            Assert.Equal(2m, actual["standardLaborHours"]!.GetValue<decimal>());
            Assert.Equal(100m, actual["standardLaborCost"]!.GetValue<decimal>());
            Assert.Equal(25m, actual["laborEfficiencyVarianceAmount"]!.GetValue<decimal>());
            Assert.Equal("REPORT-2278", actual["operations"]![0]!["coveredReports"]![0]!["reportNo"]!.GetValue<string>());
        }
        else
        {
            Assert.Equal("missing_output_basis", actual["unavailableReason"]!.GetValue<string>());
            Assert.Null(actual["standardLaborCost"]);
            Assert.Null(actual["laborEfficiencyVarianceAmount"]);
        }
    }

    private static WebApplicationFactory<GetWorkOrderCostVarianceEndpoint> CreateErpFactory()
    {
        var databaseName = $"erp-cost-seam-{Guid.CreateVersion7():N}";
        return new WebApplicationFactory<GetWorkOrderCostVarianceEndpoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Persistence:AutoMigrate"] = "false",
                    ["InternalService:BearerToken"] = "general-internal-test-token",
                    ["ConnectionStrings:PostgreSQL"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                    ["Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:0:Name"] = "finance-seam",
                    ["Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:0:BearerToken"] = Token,
                    ["Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:0:Subject"] = "business-gateway",
                    ["Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:0:OrganizationId"] = "org-2278",
                    ["Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:0:EnvironmentId"] = "env-2278",
                    ["Erp:MachineOverheadReconciliation:ScopedCallers:Profiles:0:Permissions:0"] = "business.erp.finance.read",
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            });
        });
    }

    private static WebApplicationFactory<GetBusinessConsoleErpWorkOrderCostVarianceEndpoint> CreateGatewayFactory(
        WebApplicationFactory<GetWorkOrderCostVarianceEndpoint> erp) =>
        new WebApplicationFactory<GetBusinessConsoleErpWorkOrderCostVarianceEndpoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.UseSetting("Iam:Jwt:JwksJson", "{\"keys\":[]}");
            builder.UseSetting("Iam:Jwt:Issuer", "cost-seam");
            builder.UseSetting("Iam:Jwt:Audience", "cost-seam");
            builder.UseSetting("Security:Cors:AllowedOrigins:0", "http://browser.test");
            foreach (var service in new[] { "Iam", "MasterData", "Inventory", "Quality", "ProductEngineering",
                "DemandPlanning", "Erp", "Wms", "Approval", "BarcodeLabel", "Notification", "FileStorage",
                "Mes", "Scheduling", "IndustrialTelemetry", "Maintenance", "AppHub" })
                builder.UseSetting($"{service}:BaseUrl", $"http://{service.ToLowerInvariant()}.test");
            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<TestAuthenticationHandler>();
                services.Configure<AuthenticationOptions>(options =>
                    options.SchemeMap["Bearer"].HandlerType = typeof(TestAuthenticationHandler));
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddSingleton<IBusinessGatewayAuthorizationClient, FinanceAuthorization>();
                services.RemoveAll<IInternalServiceTokenProvider>();
                services.AddSingleton<IInternalServiceTokenProvider, InternalToken>();
                services.RemoveAll<IBusinessErpClient>();
                services.AddHttpClient<IBusinessErpClient, HttpBusinessErpClient>(client =>
                    client.BaseAddress = new Uri("http://erp.test"))
                    .ConfigurePrimaryHttpMessageHandler(() => erp.Server.CreateHandler());
            });
        });

    private static async Task SeedAsync(IServiceProvider services, bool hasOutputBasis)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cost = WorkOrderCost.Open("org-2278", "env-2278", "WO-2278", "FG-2278");
        var settlement = OperationLaborSettlement.Create("org-2278", "env-2278", "WO-2278", "OP-2278",
            "WC-2278", 3, CompletedAt, 90_000_000_000L, new WorkCenterCostRateId(Guid.CreateVersion7()),
            7, "CNY", 50m, "event-2278", new string('a', 64));
        var state = OperationLaborSettlementState.Open("org-2278", "env-2278", "OP-2278");
        state.ApplySettlement(3);
        cost.RecordActualLabor(settlement);
        cost.Complete(24m, 1, 0, CompletedAt);
        cost.Capitalize("receipt-2278", 1m, 125m, CompletedAt);
        db.AddRange(cost, settlement, state);
        if (hasOutputBasis)
        {
            db.Add(OperationLaborCoveredReport.Create("org-2278", "env-2278", "WO-2278", "OP-2278", 3, "REPORT-2278"));
            db.Add(OperationLaborReportSnapshot.Create("org-2278", "env-2278", "WO-2278", "OP-2278", "WC-2278",
                "REPORT-2278", 24m, 0m, 0m, "PCS", 12m, CompletedAt, false, null, "report-event-2278"));
        }
        await db.SaveChangesAsync();
    }

    private sealed class FinanceAuthorization : IBusinessGatewayAuthorizationClient
    {
        public Task<BusinessGatewayAuthorizationResult> CheckAsync(string bearerToken,
            BusinessGatewayPermissionRequirement requirement, CancellationToken cancellationToken)
        {
            Assert.Equal("business.erp.finance.read", requirement.PermissionCode);
            Assert.Equal("org-2278", requirement.OrganizationId);
            Assert.Equal("env-2278", requirement.EnvironmentId);
            return Task.FromResult(BusinessGatewayAuthorizationResult.Allowed(
                "user-2278", "user", "finance", "org-2278", "env-2278"));
        }
    }

    private sealed class InternalToken : IInternalServiceTokenProvider
    {
        public string BearerToken => Token;
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "test-user" }]);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-2278"),
                    new Claim("organizationId", "org-2278"), new Claim("environmentId", "env-2278")],
                    Scheme.Name)), properties, Scheme.Name)));
        }
    }
}
