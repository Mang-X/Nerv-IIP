using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract: #4246 / #3629 r1. Gateway reads and preserves Scheduling facts under PlansRead scope.
public sealed class SchedulingDowntimeImpactFacadeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Public_query_checks_read_permission_and_preserves_source_result(bool allowed)
    {
        var at = new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
        var expected = new SchedulingDowntimeImpactResponse("plan-001", "saved-problem", at,
            [new(new("maintenance", "mw", "device", null, at.AddDays(-3), null, at.AddMinutes(-1), "work-order", "alarm", "alarm-1"),
                [new("order", "operation", ["qualified-device"])], 1)],
            [new("order", "operation", ["qualified-device"])], 1);
        using var handler = new SchedulingProducerHandler(expected);
        var authorization = allowed ? FakeBusinessGatewayAuthorizationClient.AllowOnly("business.scheduling.plans.read")
            : FakeBusinessGatewayAuthorizationClient.Forbidden();
        await using var lease = BusinessGatewayTestHost.Lease(authorization, services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(new HttpBusinessSchedulingClient(new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.test") }));
        });
        using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
        using var response = await client.GetAsync("/api/business-console/v1/scheduling/plans/plan-001/downtime-impact?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(allowed ? 1 : 0, handler.Requests);
        if (!allowed) return;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected, SchedulingJson.Options), document.RootElement.GetProperty("data")));
        var requirement = Assert.Single(authorization.Requirements);
        Assert.Equal("business.scheduling.plans.read", requirement.PermissionCode);
        Assert.Equal("scheduling-plan", requirement.ResourceType);
        Assert.Equal("plan-001", requirement.ResourceId);
    }

    private sealed class SchedulingProducerHandler(SchedulingDowntimeImpactResponse data) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/business/v1/scheduling/plans/plan-001/downtime-impact", request.RequestUri!.AbsolutePath);
            Assert.Equal("?organizationId=org-001&environmentId=env-dev", request.RequestUri.Query);
            Assert.NotNull(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { data, success = true }, options: SchedulingJson.Options) });
        }
    }
}
