using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.MasterData;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract: #4448 / #4405 r1. HTTP fake owners; not real IAM/provider evidence.
public sealed class SchedulingTimeContextFacadeTests
{
    [Theory]
    [InlineData("org-001", "env-dev", "plan-001", true)]
    [InlineData("org-other", "env-dev", "plan-001", false)]
    [InlineData("org-001", "env-other", "plan-001", false)]
    [InlineData("org-001", "env-dev", "plan-denied", false)]
    public async Task Only_authorized_plan_references_are_signed(string organization, string environment, string planId, bool allowed)
    {
        var auth = new FakeBusinessGatewayAuthorizationClient(r => r.PermissionCode == BusinessGatewayPermissions.SchedulingPlansRead
            && r.OrganizationId == "org-001" && r.EnvironmentId == "env-dev" && r.ResourceType == "scheduling-plan" && r.ResourceId == "plan-001");
        await VerifyAsync(auth, organization, environment, planId, allowed);
        if (organization == "org-001" && environment == "env-dev")
            Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
    }

    [Fact]
    public Task Missing_scheduling_permission_does_not_read_any_owner() => VerifyAsync(
        FakeBusinessGatewayAuthorizationClient.AllowOnly(BusinessGatewayPermissions.MasterDataResourcesRead), "org-001", "env-dev", "plan-001", false);

    private static async Task VerifyAsync(FakeBusinessGatewayAuthorizationClient auth, string organization, string environment, string planId, bool allowed)
    {
        var planOwner = new OwnerHandler(false);
        var timeOwner = new OwnerHandler(true);
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(new HttpBusinessSchedulingClient(new HttpClient(planOwner) { BaseAddress = new Uri("http://scheduling.test") }));
            services.RemoveAll<SchedulingTimeContextClient>();
            services.AddSingleton(new SchedulingTimeContextClient(new HttpClient(timeOwner) { BaseAddress = new Uri("http://masterdata.test") },
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [SchedulingTimeContextAuthorization.ConfigurationKey] = "test-key" }).Build()));
        });
        using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
        using var response = await client.GetAsync($"/api/business-console/v1/scheduling/plans/{planId}/time-context?organizationId={organization}&environmentId={environment}&workCenterCodes=arbitrary-device");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(allowed ? 1 : 0, planOwner.Calls);
        Assert.Equal(allowed ? 1 : 0, timeOwner.Calls);
        if (!allowed) return;
        Assert.Equal(["WC-2"], timeOwner.Request!.WorkCenterCodes);
        Assert.Equal("plan-001", timeOwner.Request.PlanId);
        Assert.Equal("user-admin", timeOwner.Request.PrincipalId);
        Assert.True(SchedulingTimeContextAuthorization.Verify("test-key", timeOwner.Request, timeOwner.Proof!, DateTimeOffset.UtcNow));
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Europe/London", json);
        Assert.DoesNotContain("arbitrary-device", json);
    }

    private sealed class OwnerHandler(bool timeContext) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public SchedulingTimeContextRequest? Request { get; private set; }
        public string? Proof { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (timeContext)
            {
                Assert.Equal("/api/business/v1/master-data/scheduling-time-context/resolve", request.RequestUri!.AbsolutePath);
                Request = await request.Content!.ReadFromJsonAsync<SchedulingTimeContextRequest>(ct);
                Proof = request.Headers.GetValues(SchedulingTimeContextAuthorization.HeaderName).Single();
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new[] { new SchedulingTimeContextItem("WC-2", "SITE-2", "伦敦工厂", "Europe/London", null) } }) };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(PlanJson, System.Text.Encoding.UTF8, "application/json") };
        }
    }
    private const string PlanJson = """
    {"success":true,"data":{"contractVersion":1,"planId":"plan-001","problemId":"problem-1","problemFingerprint":"fp","algorithmVersion":"v1","status":"generated","generatedAtUtc":"2026-10-16T08:00:00Z","metrics":{},"assignments":[{"assignmentId":"a-1","orderId":"WO-1","operationId":"OP-1","operationSequence":1,"resourceId":"arbitrary-device","workCenterId":"WC-2","startUtc":"2026-10-16T08:00:00Z","endUtc":"2026-10-16T09:00:00Z","isLocked":false,"explanationCode":"normal"}],"resourceLoads":[],"conflicts":[],"unscheduledOperations":[],"changeSummary":[],"ganttItems":[]}}
    """;
}
