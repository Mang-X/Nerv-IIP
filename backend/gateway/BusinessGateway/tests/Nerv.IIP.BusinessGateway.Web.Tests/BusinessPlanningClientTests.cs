using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nerv.IIP.Contracts.DemandPlanning;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Text;
using System.Text.Json;
using Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

namespace Nerv.IIP.BusinessGateway.Web.Tests;

public sealed class BusinessPlanningClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Material_deliveries_check_independent_sources_concurrently_and_wait_before_owner(bool denySupply)
    {
        var auth = new GatedSourceAuthorizationClient();
        var handler = new StubHandler("""{"data":{"runId":"run-1","planId":"plan-1","evaluatedAtUtc":"2026-10-01T00:00:00Z","supplyCoverageScope":"independent-per-net-requirement","items":[],"unknownRequirementSuggestions":[]}}""");
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessPlanningClient>();
            services.AddSingleton<IBusinessPlanningClient>(PlanningClient(handler));
        }, BusinessGatewayTestHostProfile.ServiceBaseUrls);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var responseTask = client.GetAsync("/api/business-console/v1/planning/mrp-runs/11111111-1111-1111-1111-111111111111/material-deliveries?organizationId=org-001&environmentId=env-dev&planId=plan-1");
        try
        {
            // 授权结果均未返回前八项必须已进入；此边沿证明并发，不用耗时断言推断 RTT。
            await auth.AllSourcesEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(handler.RequestUri);
            foreach (var (permission, gate) in auth.Gates)
            {
                if (permission != BusinessGatewayPermissions.MasterDataResourcesRead)
                    gate.SetResult(!(denySupply && permission == BusinessGatewayPermissions.ErpProcurementRead));
            }
            Assert.False(responseTask.IsCompleted);
            Assert.Null(handler.RequestUri);
            auth.Gates[BusinessGatewayPermissions.MasterDataResourcesRead].SetResult(true);
            var response = await responseTask;
            Assert.Equal(denySupply ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(!denySupply, handler.RequestUri is not null);
        }
        finally
        {
            // Red 的串行实现也必须解除已登记检查，避免遗留挂起请求。
            auth.ReleaseRemaining();
            await responseTask;
        }
    }

    // 来源：#4096 冻结 C 结果，四日期/数量/来源/历史未知不能由代理重算或裁剪。
    [Theory]
    [InlineData(MaterialDeliveryStatus.Yellow)]
    [InlineData(MaterialDeliveryStatus.Green)]
    [InlineData(MaterialDeliveryStatus.Red)]
    public async Task Material_deliveries_preserve_owner_facts_and_explicit_plan(MaterialDeliveryStatus status)
    {
        var date = new DateOnly(2026, 10, 9);
        var utc = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        var net = new MaterialDeliveryNetRequirementSource(30, 2, 1, 1, 3, 0, 26, 30, .1m, .9m, "net", "pcs");
        var suggestion = new MaterialDeliverySuggestionSource("suggestion-1", "Accepted", 30, 30, "shortage", "BusinessMES", "WorkOrder", "wo-1");
        var demand = new MaterialDeliveryDemandSource("SO-1", "line-1", "sales-order", "assembly", "component", 30,
            "demand-1", "sales-1", 2, date, "pv-1", "bom-1", "route-1");
        var owner = new MaterialDeliveriesResponse("11111111-1111-1111-1111-111111111111", "plan /1", utc,
            "independent-per-net-requirement", [new MaterialDeliveryResponse("net-1", "run-1", "planned-purchase",
                "component", "pcs", "site-1", date, 26, date.AddDays(-4), utc.AddDays(-4), date.AddDays(-2), utc.AddDays(-2),
                utc.AddDays(-1), utc, 20, 6, status, ["supply-insufficient"], net, [demand],
                [new MaterialDeliverySupplySource("PO-1", "1", "site-1", "component", "pcs", date.AddDays(-2), 20,
                    [new MaterialDeliveryPurchaseSource("PR-1", "1", 20, "suggestion-1")])],
                [new MaterialDeliveryOrderSourceContract("suggestion-1", "wo-1", "scheduled", utc.AddDays(-1), utc,
                    "demand-1", [new MaterialDeliveryOperationSourceContract("op-1", 1, "started", 4, 26, 90,
                        utc.AddDays(-3), utc.AddDays(-1), "scheduled", [], "route-1")],
                    [new MaterialDeliveryBoundContract("demand-1", utc.AddMinutes(90), 90, utc, ["op-1"])])], [suggestion])],
            [new MaterialDeliveryUnknownRequirementSource("net-requirement-identity-unknown", "run-old", "planned-purchase",
                "component", "pcs", "site-1", date, date.AddDays(-4), suggestion, net, [demand])]);
        var handler = new StubHandler(JsonSerializer.Serialize(new { data = owner }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var auth = FakeBusinessGatewayAuthorizationClient.Allowed();
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessPlanningClient>();
            services.AddSingleton<IBusinessPlanningClient>(PlanningClient(handler));
        }, BusinessGatewayTestHostProfile.ServiceBaseUrls);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync("/api/business-console/v1/planning/mrp-runs/11111111-1111-1111-1111-111111111111/material-deliveries?organizationId=org-001&environmentId=env-dev&planId=plan%20%2F1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["data"];
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(owner, options), actual), actual!.ToJsonString());
        Assert.Equal("/api/business/v1/planning/mrp-runs/11111111-1111-1111-1111-111111111111/material-deliveries", handler.RequestUri!.AbsolutePath);
        Assert.Equal("?organizationId=org-001&environmentId=env-dev&planId=plan%20%2F1", handler.RequestUri.Query);
        Assert.All(auth.Requirements, x => Assert.Equal("org-001", x.OrganizationId));
        Assert.Equal(9, auth.Requirements.Count);
        Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, auth.LastContinuityMode);
    }

    [Fact]
    public async Task Material_deliveries_without_plan_do_not_require_or_select_scheduling_sources()
    {
        var handler = new StubHandler("""{"data":{"runId":"run-1","planId":null,"evaluatedAtUtc":"2026-10-01T00:00:00Z","supplyCoverageScope":"independent-per-net-requirement","items":[],"unknownRequirementSuggestions":[]}}""");
        var auth = FakeBusinessGatewayAuthorizationClient.AllowOnly("business.planning.mrp.read", "business.planning.demands.read", "business.erp.procurement.read");
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessPlanningClient>();
            services.AddSingleton<IBusinessPlanningClient>(PlanningClient(handler));
        }, BusinessGatewayTestHostProfile.ServiceBaseUrls);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);
        var response = await client.GetAsync("/api/business-console/v1/planning/mrp-runs/11111111-1111-1111-1111-111111111111/material-deliveries?organizationId=org-001&environmentId=env-dev");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?organizationId=org-001&environmentId=env-dev", handler.RequestUri!.Query);
        Assert.Equal(3, auth.Requirements.Count);
    }

    // #4096：任何来源无权时不得查询已经聚合敏感来源的 owner。
    [Theory]
    [InlineData("business.planning.mrp.read")]
    [InlineData("business.planning.demands.read")]
    [InlineData("business.erp.procurement.read")]
    [InlineData("business.scheduling.plans.read")]
    [InlineData("business.mes.work-orders.read")]
    [InlineData("business.mes.reporting.read")]
    [InlineData("business.engineering.production-versions.read")]
    [InlineData("business.engineering.routings.read")]
    [InlineData("business.masterdata.resources.read")]
    public async Task Material_deliveries_deny_each_missing_source_before_querying_owner(string deniedPermission)
    {
        var auth = new FakeBusinessGatewayAuthorizationClient(requirement => requirement.PermissionCode != deniedPermission);
        var handler = new StubHandler("""{"data":null}""");
        await using var lease = BusinessGatewayTestHost.Lease(auth, services =>
        {
            services.RemoveAll<IBusinessPlanningClient>();
            services.AddSingleton<IBusinessPlanningClient>(PlanningClient(handler));
        }, BusinessGatewayTestHostProfile.ServiceBaseUrls);
        var client = lease.CreateClient();
        BusinessGatewayTestHost.Authenticated(client);

        var response = await client.GetAsync("/api/business-console/v1/planning/mrp-runs/11111111-1111-1111-1111-111111111111/material-deliveries?organizationId=org-001&environmentId=env-dev&planId=plan-1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(handler.RequestUri);
        Assert.Contains(auth.Requirements, x => x.PermissionCode == deniedPermission &&
            x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev");
    }

    [Fact]
    public async Task List_demands_forwards_keyword_skip_and_take_to_downstream_query()
    {
        var handler = new StubHandler("""{"data":[]}""");
        var client = new HttpBusinessPlanningClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://demand-planning.local"),
        });
        var request = new BusinessConsoleDemandSourceListRequest(
            "org-001",
            "env-dev",
            " pump/line ",
            7,
            23);

        await client.ListDemandSourcesAsync("internal-token", request, CancellationToken.None);

        Assert.Equal(
            "?organizationId=org-001&environmentId=env-dev&keyword=%20pump%2Fline%20&skip=7&take=23",
            handler.RequestUri!.Query);
    }

    [Fact]
    public async Task Create_forecast_returns_the_reference_allocated_by_the_downstream_service()
    {
        var client = new HttpBusinessPlanningClient(new HttpClient(new StubHandler(
            """{"data":{"forecastInputId":"forecast-1","forecastReference":"FC20260823000001"}}"""))
        {
            BaseAddress = new Uri("http://demand-planning.local"),
        });
        var request = new BusinessConsoleCreateOrUpdateForecastInputRequest(
            "org-001",
            "env-dev",
            null,
            "SKU-001",
            "pcs",
            "SITE-01",
            new DateOnly(2026, 8, 23),
            new DateOnly(2026, 9, 22),
            120m,
            IdempotencyKey: "forecast-create-1");

        var response = await client.CreateOrUpdateForecastInputAsync(
            "internal-token",
            request,
            CancellationToken.None);

        Assert.Equal("FC20260823000001", response.ForecastReference);
    }

    [Fact]
    public async Task List_suggestions_preserves_superseded_status_and_successor_run()
    {
        const string successorRunId = "2ee1a0a9-861c-4a3a-b580-133756a92711";
        var client = PlanningClient(new StubHandler($$"""
            {"data":[
              {"suggestionId":"old","mrpRunId":"11111111-1111-1111-1111-111111111111","status":4,"supersededByRunId":"{{successorRunId}}"},
              {"suggestionId":"current","mrpRunId":"{{successorRunId}}","status":0,"supersededByRunId":null},
              {"suggestionId":"accepted","mrpRunId":"{{successorRunId}}","status":1,"supersededByRunId":null}
            ]}
            """));

        var response = await client.ListSuggestionsAsync(
            "internal-token",
            new BusinessConsolePlanningSuggestionListRequest("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Collection(response.Items,
            item => { Assert.Equal("Superseded", item.Status); Assert.Equal(successorRunId, item.SupersededByRunId); },
            item => { Assert.Equal("Open", item.Status); Assert.Null(item.SupersededByRunId); },
            item => { Assert.Equal("Accepted", item.Status); Assert.Null(item.SupersededByRunId); });
    }

    [Fact]
    public async Task List_mrp_runs_preserves_latest_completed_demand_change_count_and_existing_run_facts()
    {
        var client = PlanningClient(new StubHandler("""
            {"data":[
              {"runId":"latest","horizonStart":"2026-10-01","horizonEnd":"2026-10-31","status":2,"demandChangeCount":3,"failureReason":null},
              {"runId":"failed","horizonStart":"2026-09-01","horizonEnd":"2026-09-30","status":3,"demandChangeCount":0,"failureReason":"upstream failure"}
            ]}
            """));

        var response = await client.ListMrpRunsAsync(
            "internal-token",
            new BusinessConsolePlanningContextRequest("org-001", "env-dev"),
            CancellationToken.None);

        Assert.Collection(response.Items,
            run =>
            {
                Assert.Equal("latest", run.RunId);
                Assert.Equal("Completed", run.Status);
                Assert.Equal(new DateOnly(2026, 10, 1), run.HorizonStart);
                Assert.Equal(new DateOnly(2026, 10, 31), run.HorizonEnd);
                Assert.Equal(3, run.DemandChangeCount);
            },
            run =>
            {
                Assert.Equal("failed", run.RunId);
                Assert.Equal("Failed", run.Status);
                Assert.Equal(0, run.DemandChangeCount);
                Assert.Equal("upstream failure", run.FailureReason);
            });
    }

    [Fact]
    public async Task Mps_review_and_release_forward_the_gateway_supplied_actor_to_demand_planning()
    {
        const string trustedActor = "trusted-client-actor-77";
        var reviewHandler = new StubHandler(MpsResponse("Reviewed", reviewedBy: trustedActor));
        var releaseHandler = new StubHandler(MpsResponse("Released", releasedBy: trustedActor));
        var reviewClient = PlanningClient(reviewHandler);
        var releaseClient = PlanningClient(releaseHandler);
        var reviewRequest = new BusinessConsoleReviewMpsBucketRequest("mps-001", "org-001", "env-dev", "forged-reviewer");
        var releaseRequest = new BusinessConsoleReleaseMpsBucketRequest("mps-001", "org-001", "env-dev", "forged-releaser");

        await reviewClient.ReviewMpsBucketAsync(
            "internal-token",
            "mps-001",
            trustedActor,
            reviewRequest,
            CancellationToken.None);
        await releaseClient.ReleaseMpsBucketAsync(
            "internal-token",
            "mps-001",
            trustedActor,
            releaseRequest,
            CancellationToken.None);

        using var reviewBody = JsonDocument.Parse(reviewHandler.RequestBody!);
        using var releaseBody = JsonDocument.Parse(releaseHandler.RequestBody!);
        Assert.Equal(trustedActor, reviewBody.RootElement.GetProperty("reviewedBy").GetString());
        Assert.Equal(trustedActor, releaseBody.RootElement.GetProperty("releasedBy").GetString());
    }

    private static HttpBusinessPlanningClient PlanningClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://demand-planning.local"),
        });

    private static string MpsResponse(string status, string? reviewedBy = null, string? releasedBy = null) =>
        JsonSerializer.Serialize(new
        {
            data = new
            {
                mpsId = "mps-001",
                skuCode = "SKU-001",
                uomCode = "pcs",
                siteCode = "SITE-01",
                bucketDate = "2026-06-15",
                quantity = 120m,
                status,
                reviewedBy,
                reviewedAtUtc = reviewedBy is null ? null : "2026-06-01T08:00:00Z",
                releasedBy,
                releasedAtUtc = releasedBy is null ? null : "2026-06-01T09:00:00Z",
            },
        });

    private sealed class GatedSourceAuthorizationClient : IBusinessGatewayAuthorizationClient
    {
        public TaskCompletionSource AllSourcesEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentDictionary<string, TaskCompletionSource<bool>> Gates { get; } = new();
        private volatile bool releaseRemaining;

        public Task<BusinessGatewayAuthorizationResult> CheckAsync(string token, BusinessGatewayPermissionRequirement requirement,
            CancellationToken ct) => CheckAsync(token, requirement, BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, ct);

        public async Task<BusinessGatewayAuthorizationResult> CheckAsync(string token, BusinessGatewayPermissionRequirement requirement,
            BusinessGatewayAuthorizationContinuityMode mode, CancellationToken ct)
        {
            Assert.Equal(BusinessGatewayAuthorizationContinuityMode.RealtimeRequired, mode);
            if (requirement.PermissionCode == BusinessGatewayPermissions.PlanningMrpRead)
                return await FakeBusinessGatewayAuthorizationClient.Allowed().CheckAsync(token, requirement, mode, ct);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Gates[requirement.PermissionCode] = gate;
            if (releaseRemaining) gate.TrySetResult(true);
            if (Gates.Count == 8) AllSourcesEntered.TrySetResult();
            var allowed = await gate.Task;
            return await new FakeBusinessGatewayAuthorizationClient(_ => allowed).CheckAsync(token, requirement, mode, ct);
        }

        public void ReleaseRemaining()
        {
            releaseRemaining = true;
            foreach (var gate in Gates.Values) gate.TrySetResult(true);
        }
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
