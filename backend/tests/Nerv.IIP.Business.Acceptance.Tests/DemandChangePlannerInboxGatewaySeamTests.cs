using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.BusinessGateway.Web.Endpoints.Notifications;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Iam.Web.Endpoints.Authorization;
using Nerv.IIP.Notification.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Acceptance.Tests;

[Collection(BusinessAcceptanceCollection.Name)]
public sealed class DemandChangePlannerInboxGatewaySeamTests
{
    private const string OrganizationId = "org-001";
    private const string EnvironmentId = "env-dev";
    private const string InternalToken = "local-internal-service-token";

    [Fact]
    public async Task Published_changes_reach_both_planners_through_iam_authorized_gateway_personal_inboxes()
    {
        await using var iam = new WebApplicationFactory<AuthorizationCheckEndpoint>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddFastEndpoints(options =>
            {
                options.Assemblies = [typeof(AuthorizationCheckEndpoint).Assembly];
                options.DisableAutoDiscovery = true;
            })));
        using var iamClient = iam.CreateClient();
        var adminToken = await LoginAsync(iamClient, "admin", "Admin123!");
        var firstId = await CreateUserAsync(iamClient, adminToken, "demand-planner-first", "role-production-planner");
        var secondId = await CreateUserAsync(iamClient, adminToken, "demand-planner-second", "role-production-planner");
        var outsiderId = await CreateUserAsync(iamClient, adminToken, "demand-planner-outsider", "role-erp-sales");
        var firstToken = await LoginAsync(iamClient, "demand-planner-first", "Operator123!");
        var secondToken = await LoginAsync(iamClient, "demand-planner-second", "Operator123!");
        var outsiderToken = await LoginAsync(iamClient, "demand-planner-outsider", "Operator123!");

        await using var notification = CreateNotificationFactory(iam);
        using var notificationClient = notification.CreateClient();
        await PublishAsync(notification, cancelled: false);
        await PublishAsync(notification, cancelled: true);

        await using var gateway = CreateGatewayFactory(iam, notification);
        using var browser = gateway.CreateClient();
        foreach (var (userId, token) in new[] { (firstId, firstToken), (secondId, secondToken) })
        {
            foreach (var path in new[] { "messages", "tasks" })
            {
                using var response = await GetInboxAsync(browser, token, path, $"user:{outsiderId}");
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var items = JsonNode.Parse(body)!["data"]!["items"]!.AsArray();
                Assert.Equal(2, items.Count);
                Assert.All(items, item => Assert.Equal($"user:{userId}", item!["recipientRef"]!.GetValue<string>()));
                if (path == "messages")
                {
                    Assert.Contains(items, item => item!["summary"]!.GetValue<string>().Contains("changed", StringComparison.OrdinalIgnoreCase));
                    Assert.Contains(items, item => item!["summary"]!.GetValue<string>().Contains("cancelled", StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        foreach (var path in new[] { "messages", "tasks" })
        {
            using var outsider = await GetInboxAsync(browser, outsiderToken, path, $"user:{firstId}");
            Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
            using var admin = await GetInboxAsync(browser, adminToken, path, $"user:{firstId}");
            var body = await admin.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
            Assert.Empty(JsonNode.Parse(body)!["data"]!["items"]!.AsArray());
        }
    }

    private static WebApplicationFactory<Nerv.IIP.Notification.Web.Endpoints.Notifications.ListNotificationMessagesEndpoint> CreateNotificationFactory(
        WebApplicationFactory<AuthorizationCheckEndpoint> iam) =>
        new WebApplicationFactory<Nerv.IIP.Notification.Web.Endpoints.Notifications.ListNotificationMessagesEndpoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
                builder.UseSetting("Persistence:Provider", "InMemory");
                builder.UseSetting("Persistence:InMemoryDatabaseName", $"planner-inbox-{Guid.CreateVersion7():N}");
                builder.UseSetting("InternalService:BearerToken", InternalToken);
                builder.UseSetting("Iam:BaseUrl", "http://iam.test");
                builder.ConfigureTestServices(services =>
                {
                    services.AddFastEndpoints(options =>
                    {
                        options.Assemblies = [typeof(Nerv.IIP.Notification.Web.Endpoints.Notifications.ListNotificationMessagesEndpoint).Assembly];
                        options.DisableAutoDiscovery = true;
                    });
                    services.RemoveAll<IProductionPlannerMemberDirectory>();
                    services.AddHttpClient<IProductionPlannerMemberDirectory, HttpProductionPlannerMemberDirectory>(client =>
                        client.BaseAddress = new Uri("http://iam.test"))
                        .ConfigurePrimaryHttpMessageHandler(() => iam.Server.CreateHandler());
                });
            });

    private static WebApplicationFactory<ListBusinessConsoleNotificationMessagesEndpoint> CreateGatewayFactory(
        WebApplicationFactory<AuthorizationCheckEndpoint> iam,
        WebApplicationFactory<Nerv.IIP.Notification.Web.Endpoints.Notifications.ListNotificationMessagesEndpoint> notification) =>
        new WebApplicationFactory<ListBusinessConsoleNotificationMessagesEndpoint>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("FastEndpoints:RestrictDiscoveryToEntryAssembly", "true");
            builder.UseSetting("Iam:Jwt:JwksJson", "{\"keys\":[]}");
            builder.UseSetting("Iam:Jwt:Issuer", "planner-inbox");
            builder.UseSetting("Iam:Jwt:Audience", "planner-inbox");
            builder.UseSetting("Security:Cors:AllowedOrigins:0", "http://browser.test");
            foreach (var service in new[] { "Iam", "MasterData", "Inventory", "Quality", "ProductEngineering",
                "DemandPlanning", "Erp", "Wms", "Approval", "BarcodeLabel", "Notification", "FileStorage",
                "Mes", "Scheduling", "IndustrialTelemetry", "Maintenance", "AppHub" })
                builder.UseSetting($"{service}:BaseUrl", $"http://{service.ToLowerInvariant()}.test");
            builder.UseSetting("InternalService:BearerToken", InternalToken);
            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<BearerPassThroughAuthenticationHandler>();
                services.Configure<AuthenticationOptions>(options =>
                    options.SchemeMap["Bearer"].HandlerType = typeof(BearerPassThroughAuthenticationHandler));
                services.RemoveAll<IBusinessGatewayAuthorizationClient>();
                services.AddHttpClient<IBusinessGatewayAuthorizationClient, HttpBusinessGatewayAuthorizationClient>(client =>
                    client.BaseAddress = new Uri("http://iam.test"))
                    .ConfigurePrimaryHttpMessageHandler(() => iam.Server.CreateHandler());
                services.RemoveAll<IBusinessNotificationClient>();
                services.AddHttpClient<IBusinessNotificationClient, HttpBusinessNotificationClient>(client =>
                    client.BaseAddress = new Uri("http://notification.test"))
                    .ConfigurePrimaryHttpMessageHandler(() => notification.Server.CreateHandler());
            });
        });

    private static async Task PublishAsync(
        WebApplicationFactory<Nerv.IIP.Notification.Web.Endpoints.Notifications.ListNotificationMessagesEndpoint> notification,
        bool cancelled)
    {
        using var scope = notification.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<SalesOrderDemandChangedForWorkOrderIntegrationEventHandlerForNotification>();
        await handler.HandleAsync(new SalesOrderDemandChangedForWorkOrderIntegrationEvent(
            cancelled ? "event-cancelled" : "event-changed",
            DemandPlanningIntegrationEventTypes.SalesOrderDemandChangedForWorkOrder,
            DemandPlanningIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-09-29T08:00:00Z"),
            DemandPlanningIntegrationEventSources.BusinessDemandPlanning,
            "demand-change-001", "sales-order-001", OrganizationId, EnvironmentId,
            "system:business-demand-planning", cancelled ? "demand-cancelled-001" : "demand-changed-001",
            new SalesOrderDemandChangedForWorkOrderPayload("suggestion-001", "WO-001", "sales-order:SO-001", "SO-001", 2, cancelled)),
            CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> GetInboxAsync(HttpClient browser, string token, string path, string forgedRecipient)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/business-console/v1/notifications/{path}?organizationId={OrganizationId}&environmentId={EnvironmentId}&recipientRef={Uri.EscapeDataString(forgedRecipient)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await browser.SendAsync(request);
    }

    private static async Task<string> CreateUserAsync(HttpClient iam, string adminToken, string loginName, string roleId)
    {
        iam.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var created = await iam.PostAsJsonAsync("/api/iam/v1/users", new
        {
            loginName,
            email = $"{loginName}@nerv-iip.local",
            password = "Operator123!",
        });
        created.EnsureSuccessStatusCode();
        var userId = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["data"]!["userId"]!.GetValue<string>();
        using var assigned = await iam.PutAsJsonAsync($"/api/iam/v1/users/{userId}/membership", new { roleIds = new[] { roleId } });
        assigned.EnsureSuccessStatusCode();
        iam.DefaultRequestHeaders.Authorization = null;
        return userId;
    }

    private static async Task<string> LoginAsync(HttpClient iam, string loginName, string password)
    {
        using var response = await iam.PostAsJsonAsync("/api/iam/v1/auth/login", new { loginName, password });
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"]!["accessToken"]!.GetValue<string>();
    }

    private sealed class BearerPassThroughAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal);
            var properties = new AuthenticationProperties();
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = token }]);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "iam-user"),
                new Claim("organizationId", OrganizationId),
                new Claim("environmentId", EnvironmentId),
            ], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name)));
        }
    }
}
