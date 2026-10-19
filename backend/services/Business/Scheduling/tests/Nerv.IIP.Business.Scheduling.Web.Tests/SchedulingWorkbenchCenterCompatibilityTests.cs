using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using NetCorePal.Extensions.Dto;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed partial class SchedulingEndpointContractTests
{
    // ApprovedDecision: #4514 / #4454 r1. HTTP host + EF InMemory, upstream HTTP fixtures;
    // this proves Scheduling behavior, not real MES consumption or browser acceptance.
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public async Task Workbench_http_rejects_task_center_difference_without_creating_problem_or_plan(int mismatchedSequence)
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var upstream = new HttpClient(new CenterCompatibilityUpstream(mismatchedSequence)) { BaseAddress = new Uri("http://mes") };
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureCenterCompatibility(services, upstream)));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        using var response = await client.PostAsJsonAsync("/api/business/v1/scheduling/workbench/plans",
            CenterCompatibilityRequest("WO-REJECTED"), SchedulingJson.Options);

        var envelope = await response.Content.ReadFromJsonAsync<ResponseData<SchedulePlanContract>>(SchedulingJson.Options);
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Contains("WO-REJECTED", envelope.Message);
        Assert.Contains(mismatchedSequence.ToString(), envelope.Message);
        Assert.Contains("WC-ROD-01", envelope.Message);
        Assert.Contains("WC-TUB-01", envelope.Message);
        Assert.Contains("未恢复", envelope.Message);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.ScheduleProblems.ToArrayAsync());
        Assert.Empty(await db.SchedulePlans.ToArrayAsync());
        Assert.Empty(await db.Set<SchedulePlanAssignment>().ToArrayAsync());
    }

    [Fact]
    public async Task Workbench_http_compatible_three_operation_order_can_generate_release_and_revoke()
    {
        await using var factory = new SchedulingLiveHttpTestFactory();
        using var upstream = new HttpClient(new CenterCompatibilityUpstream(null)) { BaseAddress = new Uri("http://mes") };
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            ConfigureCenterCompatibility(services, upstream)));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-internal-token");

        using var response = await client.PostAsJsonAsync("/api/business/v1/scheduling/workbench/plans",
            CenterCompatibilityRequest("WO-COMPATIBLE"), SchedulingJson.Options);
        var envelope = await response.Content.ReadFromJsonAsync<ResponseData<SchedulePlanContract>>(SchedulingJson.Options);
        var plan = Assert.IsType<SchedulePlanContract>(envelope?.Data);
        Assert.Equal(SchedulePlanStatusContract.Generated, plan.Status);
        Assert.Empty(plan.UnscheduledOperations);
        var assignments = plan.Assignments.OrderBy(x => x.OperationSequence).ToArray();
        Assert.Equal([10, 20, 30], assignments.Select(x => x.OperationSequence));
        Assert.All(assignments, x => Assert.Equal("WC-TUB-01", x.WorkCenterId));
        Assert.Equal(["WO-COMPATIBLE-OP10", "WO-COMPATIBLE-OP20", "WO-COMPATIBLE-OP30"],
            assignments.Select(x => x.OperationId));
        Assert.True(assignments[0].EndUtc <= assignments[1].StartUtc);
        Assert.True(assignments[1].EndUtc <= assignments[2].StartUtc);

        using var release = await client.PostAsJsonAsync($"/api/business/v1/scheduling/plans/{plan.PlanId}/release?organizationId=org-001&environmentId=prod",
            new ReleaseSchedulePlanRequest(plan.PlanId, "org-001", "prod"), SchedulingJson.Options);
        var released = await release.Content.ReadFromJsonAsync<ResponseData<ReleaseSchedulePlanResponse>>(SchedulingJson.Options);
        Assert.True(released!.Success);
        Assert.Equal(SchedulePlanStatusContract.Released, released.Data!.Status);
        using var revoke = await client.PostAsJsonAsync($"/api/business/v1/scheduling/plans/{plan.PlanId}/revoke?organizationId=org-001&environmentId=prod",
            new RevokeSchedulePlanRequest(plan.PlanId, "org-001", "prod"), SchedulingJson.Options);
        var revoked = await revoke.Content.ReadFromJsonAsync<ResponseData<RevokeSchedulePlanResponse>>(SchedulingJson.Options);
        Assert.True(revoked!.Success);
        Assert.Equal(SchedulePlanStatusContract.Revoked, revoked.Data!.Status);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.ScheduleProblems.ToArrayAsync());
        Assert.Equal(plan.PlanId, Assert.Single(await db.SchedulePlans.ToArrayAsync()).PlanId);
    }

    private static CreateSchedulingWorkbenchPlanRequest CenterCompatibilityRequest(string orderId) =>
        new("org-001", "prod", FixedNow, FixedNow.AddHours(8), [new(orderId, 1, true)]);

    private static void ConfigureCenterCompatibility(IServiceCollection services, HttpClient upstream)
    {
        services.RemoveAll<ISchedulingWorkbenchSourceProvider>();
        services.RemoveAll<ISchedulingProblemProductEngineeringClient>();
        services.RemoveAll<ISchedulingProblemMasterDataClient>();
        var engineering = new CenterCompatibilityEngineering();
        services.AddSingleton<ISchedulingWorkbenchSourceProvider>(new HttpSchedulingWorkbenchSourceProvider(upstream, engineering));
        services.AddSingleton<ISchedulingProblemProductEngineeringClient>(engineering);
        services.AddSingleton<ISchedulingProblemMasterDataClient, CenterCompatibilityMasterData>();
    }

    private sealed class CenterCompatibilityUpstream(int? mismatchedSequence) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var id = mismatchedSequence.HasValue ? "WO-REJECTED" : "WO-COMPATIBLE";
            Assert.True(request.Method == HttpMethod.Get || request.RequestUri!.AbsolutePath.EndsWith("/assembly-children/batch", StringComparison.Ordinal));
            object payload = request.Method == HttpMethod.Get
                ? new { items = new[] { new { workOrderId = id, workOrderNo = id, skuId = "SKU-001", skuCode = "SKU-001",
                    productionVersionId = "pv-001", quantity = 1, priority = 1, isRush = true,
                    dueUtc = FixedNow.AddHours(8), status = "created",
                    // Out of order: compatibility must match the sequence, not collection position.
                    operationTasks = new[] { 30, 10, 20 }.Select(sequence => new {
                        operationTaskId = $"{id}-OP{sequence}", operationSequence = sequence,
                        workCenterId = sequence == mismatchedSequence ? "WC-ROD-01" : "WC-TUB-01",
                        earliestStartUtc = FixedNow }).ToArray() } }, total = 1 }
                : new { items = new[] { new { workOrderId = id, assemblyChildWorkOrderIds = Array.Empty<string>() } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        }
    }

    private sealed class CenterCompatibilityEngineering : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemProductionVersionSnapshot> GetProductionVersionRoutingAsync(string organizationId,
            string environmentId, string productionVersionId, CancellationToken cancellationToken) =>
            Task.FromResult(new SchedulingProblemProductionVersionSnapshot(productionVersionId, "SKU-001", "ROUTE-001:A"));

        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(string organizationId, string environmentId,
            string routingVersionId, CancellationToken cancellationToken) => Task.FromResult(new SchedulingProblemRoutingSnapshot(
                "ROUTE-001", "A", "SKU-001", new[] { 10, 20, 30 }.Select(sequence =>
                    new SchedulingProblemRoutingOperationSnapshot(sequence, "WC-TUB-01", "cutting", $"OP{sequence}", 0, 10, 0)).ToArray()));
    }

    private sealed class CenterCompatibilityMasterData : ISchedulingProblemMasterDataClient
    {
        public Task<SchedulingProblemWorkCenterSnapshot> GetWorkCenterAsync(string organizationId, string environmentId,
            string workCenterCode, CancellationToken cancellationToken) =>
            Task.FromResult(new SchedulingProblemWorkCenterSnapshot(workCenterCode, "CAL-001", 1, ["cutting"]));
        public Task<SchedulingProblemCalendarSnapshot> GetCalendarAsync(string organizationId, string environmentId,
            string calendarCode, DateTimeOffset horizonStartUtc, DateTimeOffset horizonEndUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new SchedulingProblemCalendarSnapshot(calendarCode, [new(horizonStartUtc, horizonEndUtc, "shift")]));
        public Task<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>> ListDeviceAssetsAsync(string organizationId,
            string environmentId, string workCenterCode, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SchedulingProblemDeviceAssetSnapshot>>([]);
        public Task<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>> ResolveToolingFactsAsync(string organizationId,
            string environmentId, IReadOnlyCollection<SchedulingProblemToolingTransitionSnapshot> transitions, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SchedulingProblemToolingFactSnapshot>>([]);
    }
}
