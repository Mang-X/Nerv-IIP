using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Contracts.EquipmentRuntime;
using NetCorePal.Extensions.Dto;
using static Nerv.IIP.Business.Scheduling.Web.Tests.RightShiftCandidateGeneratorTests;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// PublicContract: #4246, approved #3629 r1. Real local HTTP host / EF InMemory / controlled upstream HTTP.
public sealed partial class SchedulingEndpointContractTests
{
    [Fact]
    public async Task Downtime_query_uses_saved_baseline_raw_source_http_and_scope_without_writing()
    {
        var problem = Problem(Order("A", Operation("a", "R1") with { EligibleResourceIds = ["R1", "R2"] }));
        var baseline = Input(problem, [Assignment("A", "a", "R1", 0, 60)], []).Baseline;
        var source = new DowntimeSourceHandler(problem.HorizonStartUtc);
        await using var baseFactory = new SchedulingLiveHttpTestFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(problem.HorizonStartUtc));
            services.AddScoped<ISchedulingEquipmentAvailabilityProvider, HttpSchedulingEquipmentAvailabilityProvider>();
            foreach (var name in new[] { HttpSchedulingMaterialReadinessProvider.MesClientName,
                HttpSchedulingEquipmentAvailabilityProvider.MaintenanceClientName, HttpSchedulingEquipmentAvailabilityProvider.IndustrialTelemetryClientName })
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => source);
        }));
        using var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ScheduleProblems.Add(new ScheduleProblemSnapshot(problem.ProblemId, 1, "org", "env", baseline.ProblemFingerprint,
                JsonSerializer.Serialize(problem, SchedulingJson.Options), problem.HorizonStartUtc, problem.HorizonEndUtc, baseline.GeneratedAtUtc));
            db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan("org", "env", SchedulePlanContractMapper.ToDomainSnapshot(baseline)));
            await db.SaveChangesAsync();
        }
        var route = $"/api/business/v1/scheduling/plans/{baseline.PlanId}/downtime-impact?organizationId=org&environmentId=env";
        using var anonymous = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");
        var first = (await client.GetFromJsonAsync<ResponseData<SchedulingDowntimeImpactResponse>>(route, SchedulingJson.Options))!.Data;
        Assert.Equal(baseline.PlanId, first.BaselinePlanId);
        Assert.Equal(problem.ProblemId, first.ProblemId);
        Assert.Equal(problem.HorizonStartUtc, first.ObservedAtUtc);
        Assert.Equal(2, first.Items.Count);
        Assert.All(first.Items, item =>
        {
            Assert.Equal(problem.HorizonStartUtc.AddDays(-3), item.Fact.StartedAtUtc);
            Assert.Null(item.Fact.RecoveredAtUtc);
            Assert.Equal(1, item.OperationsWithAlternativesCount);
            var operation = Assert.Single(item.AffectedOperations);
            Assert.Equal(("A", "a"), (operation.WorkOrderId, operation.OperationId));
            Assert.Equal(new[] { "R2" }, operation.AvailableAlternativeResourceIds);
        });
        var maintenance = first.Items.Single(x => x.Fact.Source == "maintenance").Fact;
        Assert.Equal(problem.HorizonStartUtc.AddHours(1), maintenance.ExpectedRestoreAtUtc);
        Assert.Equal("alarm", maintenance.OriginSourceType);
        Assert.Equal("alarm-1", maintenance.OriginSourceReferenceId);
        Assert.True(source.ReadSecondMesPage);
        source.Restore = problem.HorizonStartUtc.AddHours(2);
        var updated = (await client.GetFromJsonAsync<ResponseData<SchedulingDowntimeImpactResponse>>(route, SchedulingJson.Options))!.Data;
        Assert.Equal(source.Restore, updated.Items.Single(x => x.Fact.Source == "maintenance").Fact.ExpectedRestoreAtUtc);
        source.Restore = null;
        var cleared = (await client.GetFromJsonAsync<ResponseData<SchedulingDowntimeImpactResponse>>(route, SchedulingJson.Options))!.Data;
        Assert.Null(cleared.Items.Single(x => x.Fact.Source == "maintenance").Fact.ExpectedRestoreAtUtc);
        source.Recovered = problem.HorizonStartUtc.AddMinutes(10);
        var recovered = (await client.GetFromJsonAsync<ResponseData<SchedulingDowntimeImpactResponse>>(route, SchedulingJson.Options))!.Data;
        Assert.Equal(source.Recovered, recovered.Items.Single(x => x.Fact.Source == "maintenance").Fact.RecoveredAtUtc);
        var requests = source.Requests;
        using var wrongOrg = await client.GetAsync(route.Replace("organizationId=org", "organizationId=other"));
        using var wrongEnv = await client.GetAsync(route.Replace("environmentId=env", "environmentId=other"));
        Assert.Contains("未找到排程方案", await wrongOrg.Content.ReadAsStringAsync());
        Assert.Contains("未找到排程方案", await wrongEnv.Content.ReadAsStringAsync());
        Assert.Equal(requests, source.Requests);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, db.SchedulePlans.Count());
            Assert.Equal(1, db.ScheduleProblems.Count());
            Assert.Empty(db.ScheduleWorkingDrafts);
            Assert.Empty(db.ChangeTracker.Entries());
            var saved = await scope.ServiceProvider.GetRequiredService<MediatR.ISender>()
                .Send(new GetSchedulePlanDetailQuery(baseline.PlanId, "org", "env"));
            Assert.Equal(JsonSerializer.Serialize(baseline.Assignments, SchedulingJson.Options),
                JsonSerializer.Serialize(saved.Assignments, SchedulingJson.Options));
        }
    }

    private sealed class DowntimeSourceHandler(DateTimeOffset at) : HttpMessageHandler
    {
        public DateTimeOffset? Restore { get; set; } = at.AddHours(1);
        public DateTimeOffset? Recovered { get; set; }
        public bool ReadSecondMesPage { get; private set; }
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal("test-internal-token", request.Headers.Authorization!.Parameter);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            object payload;
            if (request.RequestUri.AbsolutePath.EndsWith("downtime-events", StringComparison.Ordinal))
            {
                Assert.Equal("org", query["organizationId"].ToString());
                Assert.Equal("env", query["environmentId"].ToString());
                Assert.Equal(DateTimeOffset.MinValue, DateTimeOffset.Parse(query["windowStartUtc"].ToString()));
                var second = query["skip"] == "100";
                ReadSecondMesPage |= second;
                payload = new { items = second ? Array.Empty<object>() : new object[] {
                    new { downtimeEventId = "mes-1", deviceAssetId = "R1", workCenterId = "WC-R1",
                        startedAtUtc = at.AddDays(-3), recoveredAtUtc = (DateTimeOffset?)null } }, total = 101 };
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("downtime-facts/query", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                var input = await request.Content!.ReadFromJsonAsync<Nerv.IIP.Contracts.Maintenance.MaintenanceDowntimeFactsRequest>(SchedulingJson.Options, ct);
                Assert.Equal("org", input!.OrganizationId);
                Assert.Equal("env", input.EnvironmentId);
                Assert.Equal(new[] { "R1", "R2", "R3" }, input.DeviceAssetIds);
                payload = new { data = new { items = new[] { new { deviceAssetId = "R1", workOrderId = "mw-1", source = "work-order",
                    sourceType = "alarm", sourceReferenceId = "alarm-1", unavailableFromUtc = at.AddDays(-3), releasedAtUtc = Recovered,
                    expectedRestoreAtUtc = Restore, predictedRestoreAtUtc = Restore, restorePredictionSource = "maintenance", restorePredictionSourceVersion = "v1" } } }, success = true };
            }
            else
            {
                Assert.True(request.RequestUri.AbsolutePath.EndsWith("runtime-availability", StringComparison.Ordinal)
                    || request.RequestUri.AbsolutePath.EndsWith("availability-windows", StringComparison.Ordinal));
                var window = new EquipmentRuntimeAvailabilityWindowContract("R2", "WC-R2", EquipmentRuntimeAvailabilityStatus.Available,
                    "available", EquipmentRuntimeSeverity.Blocked, at, at.AddMinutes(1), EquipmentRuntimeSourceType.StaleSource, "state", "state", []);
                payload = new { data = new EquipmentRuntimeAvailabilityResponse(1, "org", "env", at, at.AddMinutes(1), [window]), success = true };
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(payload, options: SchedulingJson.Options) };
        }
    }
}
