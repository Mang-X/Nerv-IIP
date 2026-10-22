using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

// PublicContract: #4122 / ADR 0032 §3. Gateway preserves producer facts, including old null.
public sealed class SchedulingFreezeContextFacadeTests
{
    [Theory]
    [InlineData("create", false)]
    [InlineData("detail", false)]
    [InlineData("revision", false)]
    [InlineData("create", true)]
    [InlineData("detail", true)]
    [InlineData("revision", true)]
    public async Task Http_client_and_host_preserve_original_freeze_snapshot(string operation, bool legacy)
    {
        var plan = JsonSerializer.Deserialize<SchedulePlanContract>(PlanJson, SchedulingJson.Options)!;
        if (legacy) plan = plan with { FreezeContext = null };
        using var handler = new ProducerHandler(plan);
        await using var lease = BusinessGatewayTestHost.Lease(FakeBusinessGatewayAuthorizationClient.Allowed(), services =>
        {
            services.RemoveAll<IBusinessSchedulingClient>();
            services.AddSingleton<IBusinessSchedulingClient>(new HttpBusinessSchedulingClient(
                new HttpClient(handler) { BaseAddress = new Uri("http://scheduling.test") }));
        });
        using var client = BusinessGatewayTestHost.Authenticated(lease.CreateClient());
        using var response = operation switch
        {
            "create" => await client.PostAsJsonAsync("/api/business-console/v1/scheduling/workbench/plans", new
            {
                organizationId = "org-001", environmentId = "env-dev",
                horizonStartUtc = "2026-10-07T08:00:00Z", horizonEndUtc = "2026-10-08T08:00:00Z",
                orders = new[] { new { workOrderId = "order-001", priority = 1, isRush = true } }
            }),
            "revision" => await client.PostAsJsonAsync("/api/business-console/v1/scheduling/plans/plan-001/revisions", new
            {
                organizationId = "org-001", environmentId = "env-dev",
                includedOrderIds = new[] { "order-001" }, lockedAssignments = Array.Empty<object>()
            }),
            _ => await client.GetAsync("/api/business-console/v1/scheduling/plans/plan-001?organizationId=org-001&environmentId=env-dev")
        };
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        if (operation == "revision") data = data.GetProperty("candidate");
        var expected = JsonSerializer.SerializeToElement(plan.FreezeContext, SchedulingJson.Options);
        Assert.True(JsonElement.DeepEquals(expected, data.GetProperty("freezeContext")));
    }

    private sealed class ProducerHandler(SchedulePlanContract plan) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var revision = request.RequestUri!.AbsolutePath.EndsWith("/revisions", StringComparison.Ordinal);
            var data = revision
                ? JsonSerializer.SerializeToElement(new { candidate = plan, impact = new { }, comparison = new { } }, SchedulingJson.Options)
                : JsonSerializer.SerializeToElement(plan, SchedulingJson.Options);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, data }, options: SchedulingJson.Options)
            });
        }
    }

    private const string PlanJson = """
    {
      "contractVersion": 1, "planId": "plan-001", "problemId": "problem-001",
      "problemFingerprint": "fingerprint", "algorithmVersion": "v1", "status": "generated",
      "generatedAtUtc": "2026-10-07T08:00:00Z", "metrics": {},
      "assignments": [], "resourceLoads": [], "conflicts": [], "unscheduledOperations": [], "changeSummary": [], "ganttItems": [],
      "freezeContext": {
        "asOfUtc": "2026-10-07T08:00:00Z", "defaultWindowEndUtc": "2026-10-07T10:00:00Z",
        "workCenterWindows": [{"workCenterId": "wc-override", "endUtc": "2026-10-07T08:00:00Z"}],
        "assignments": [{
          "assignment": {
            "assignmentId": "a-1", "orderId": "order-001", "operationId": "op-1", "operationSequence": 1,
            "resourceId": "original-resource", "workCenterId": "wc-001", "startUtc": "2026-10-07T09:00:00Z",
            "endUtc": "2026-10-07T12:00:00Z", "isLocked": true, "explanationCode": "locked",
            "segments": [{"startUtc": "2026-10-07T09:00:00Z", "endUtc": "2026-10-07T10:00:00Z"},
                         {"startUtc": "2026-10-07T11:00:00Z", "endUtc": "2026-10-07T12:00:00Z"}]
          }, "reasons": ["started", "manualLock", "stableWindow"]
        }]
      }
    }
    """;
}
