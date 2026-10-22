using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

// PublicContract：#4106，当前执行信息来自权威读面，不能改写保存时方案风险。
public sealed class ScheduleAssignmentCurrentExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Read_uses_work_order_progress_for_each_operation_and_preserves_saved_risks(bool knownArrival)
    {
        var handler = new SourceHandler(knownArrival);
        var factory = new ClientFactory(handler);
        var material = new HttpSchedulingMaterialReadinessProvider(factory, null, NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance);
        var reader = new ScheduleAssignmentCurrentExecutionReader(factory, material, null, new FixedTime(), NullLogger<ScheduleAssignmentCurrentExecutionReader>.Instance);
        var plan = Plan();
        var result = await reader.ReadAsync(plan, "org", "env", CancellationToken.None);
        Assert.Equal(1, handler.WorkOrderRequests);
        Assert.All(result.Assignments, assignment =>
        {
            var current = Assert.IsType<ScheduleAssignmentCurrentExecutionContract>(assignment.CurrentExecution);
            Assert.Equal(new ScheduleWorkOrderProgressContract(25m, 100m), current.WorkOrderProgress);
            Assert.Equal(knownArrival ? Now.AddDays(2) : null, current.MaterialReadyUtc);
            Assert.False(current.IsMaterialReady);
            Assert.Null(current.EquipmentState);
            Assert.False(current.IsEquipmentSourceFresh);
            Assert.Equal(Now, current.ObservedAtUtc);
        });
        Assert.Same(plan.MaterialRisks, result.MaterialRisks);
        Assert.Null(plan.Assignments.First().CurrentExecution);
        Assert.Equal(Now.AddDays(9), result.MaterialRisks!.Single().MaterialReadyUtc);
    }

    [Fact]
    public async Task Read_unavailable_sources_return_unknown_without_inventing_progress_or_availability()
    {
        var factory = new ClientFactory(new SourceHandler(false, unavailable: true));
        var reader = new ScheduleAssignmentCurrentExecutionReader(factory,
            new HttpSchedulingMaterialReadinessProvider(factory, null, NullLogger<HttpSchedulingMaterialReadinessProvider>.Instance),
            null, new FixedTime(), NullLogger<ScheduleAssignmentCurrentExecutionReader>.Instance);
        var result = await reader.ReadAsync(Plan(), "org", "env", CancellationToken.None);
        Assert.All(result.Assignments, assignment =>
        {
            var current = assignment.CurrentExecution!;
            Assert.Null(current.WorkOrderProgress);
            Assert.Null(current.MaterialReadyUtc);
            Assert.Null(current.IsMaterialReady);
            Assert.Null(current.EquipmentState);
            Assert.Null(current.IsEquipmentSourceFresh);
        });
    }

    private static SchedulePlanContract Plan() => new(1, "plan", "problem", "fingerprint", "algorithm",
        SchedulePlanStatusContract.Released, Now.AddDays(-1), new(2, 0, 60, 60, 0, 0, 1m, 1m),
        [new("a1", "wo", "op1", 1, "device", "wc", Now, Now.AddMinutes(30), false, "scheduled"),
         new("a2", "wo", "op2", 2, "device", "wc", Now.AddMinutes(30), Now.AddHours(1), false, "scheduled")],
        [], [], [], [], [], MaterialRisks: [new("wo", "op1", ["saved-risk"], [], "保存时缺料", Now.AddDays(9))]);

    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("http://source") };
    }
    private sealed class SourceHandler(bool knownArrival, bool unavailable = false) : HttpMessageHandler
    {
        public int WorkOrderRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Contains("organizationId=org", request.RequestUri!.Query + (request.Method == HttpMethod.Post ? "organizationId=org" : ""));
            if (unavailable) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            object body;
            if (request.Method == HttpMethod.Post)
                body = new { items = new[] { new { workOrderId = "wo", readinessStatus = "Shortage", blockingReasons = new[] { "shortage" }, items = new[] { new { materialId = "m", requiredQuantity = 5m, availableQuantity = 0m, shortageQuantity = 5m, expectedAvailableAtUtc = knownArrival ? Now.AddDays(2) : (DateTimeOffset?)null } } } } };
            else if (request.RequestUri.AbsolutePath.Contains("/mes/"))
            { WorkOrderRequests++; body = new { quantity = 100m, completedQuantity = 25m }; }
            else body = new { data = new { currentState = (string?)null, stateOccurredAtUtc = (DateTimeOffset?)null, isSourceFresh = false } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
