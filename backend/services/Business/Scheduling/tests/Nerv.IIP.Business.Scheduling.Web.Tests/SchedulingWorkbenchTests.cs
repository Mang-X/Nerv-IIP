using System.Net;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Web.Endpoints.Scheduling;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingWorkbenchTests
{
    [Fact]
    public async Task Source_provider_accepts_one_authoritative_batch_of_100_distinct_work_orders()
    {
        var start = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var orders = Enumerable.Range(1, 100).Select(index => new
        {
            workOrderId = $"WO-{index:000}",
            skuId = "SKU-001",
            skuCode = "SKU-001",
            productionVersionId = "pv-001",
            quantity = 10,
            priority = 10,
            dueUtc = start.AddDays(2),
            status = "released",
            workOrderNo = $"MO-{index:000}",
            operationTasks = new[] { new { earliestStartUtc = start } },
        }).ToArray();
        var handler = new StubHandler(_ => Json(new { data = new { items = orders, total = orders.Length }, success = true }));
        var provider = new HttpSchedulingWorkbenchSourceProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://mes") },
            new StubProductEngineeringClient());

        var result = await provider.ResolveOrdersAsync(
            "org-001",
            "env-dev",
            start,
            orders.Select((x, index) => new SchedulingWorkbenchOrderSelection(x.workOrderId, index, index == 0)).ToArray(),
            CancellationToken.None);

        Assert.Equal(100, result.Count);
        Assert.Equal(100, result.Select(x => x.OrderId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("ROUTE-001:A", result.First().RoutingVersionId);
        Assert.True(result.First().IsRush);
    }

    [Fact]
    public async Task Source_provider_fails_closed_for_a_terminal_work_order()
    {
        var handler = new StubHandler(_ => Json(new
        {
            data = new
            {
                items = new[]
                {
                    new
                    {
                        workOrderId = "WO-DONE",
                        skuId = "SKU-001",
                        skuCode = "SKU-001",
                        productionVersionId = "pv-001",
                        quantity = 1,
                        priority = 10,
                        dueUtc = DateTimeOffset.UtcNow.AddDays(1),
                        status = "completed",
                        workOrderNo = "MO-DONE",
                        operationTasks = new[] { new { earliestStartUtc = DateTimeOffset.UtcNow } },
                    }
                },
                total = 1,
            },
            success = true,
        }));
        var provider = new HttpSchedulingWorkbenchSourceProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://mes") },
            new StubProductEngineeringClient());

        var exception = await Assert.ThrowsAsync<KnownException>(() => provider.ResolveOrdersAsync(
            "org-001",
            "env-dev",
            DateTimeOffset.UtcNow,
            [new("WO-DONE", 10, false)],
            CancellationToken.None));

        Assert.Contains("terminal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Source_provider_scans_authoritative_pages_before_reporting_a_selected_order_missing()
    {
        var start = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        var requests = 0;
        var handler = new StubHandler(request =>
        {
            requests++;
            var isSecondPage = request.RequestUri!.Query.Contains("skip=500", StringComparison.Ordinal);
            var items = isSecondPage
                ? new[] { WorkOrder("WO-501", start) }
                : Array.Empty<object>();
            return Json(new { data = new { items, total = 501 }, success = true });
        });
        var provider = new HttpSchedulingWorkbenchSourceProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://mes") },
            new StubProductEngineeringClient());

        var result = await provider.ResolveOrdersAsync(
            "org-001", "env-dev", start, [new("WO-501", 10, false)], CancellationToken.None);

        Assert.Equal(2, requests);
        Assert.Equal("WO-501", Assert.Single(result).OrderId);
    }

    [Fact]
    public void Endpoint_registry_declares_workbench_generation_and_revision()
    {
        var contracts = SchedulingEndpointContracts.All;

        Assert.Contains(contracts, x =>
            x.Route == "/api/business/v1/scheduling/workbench/plans" &&
            x.OperationId == "createSchedulingWorkbenchPlan");
        Assert.Contains(contracts, x =>
            x.Route == "/api/business/v1/scheduling/plans/{planId}/revisions" &&
            x.OperationId == "createSchedulingPlanRevision");
    }

    [Fact]
    public async Task Revision_preserves_explicit_lock_and_returns_latest_invalidation_impact_and_comparison()
    {
        await using var db = CreateDbContext();
        var problem = ShockAbsorberSchedulingFixture.CreateProblem();
        var basePlan = SchedulePlanContractMapper.WithStatus(
            new FiniteCapacityScheduler().Schedule(problem, "plan-base", problem.HorizonStartUtc),
            SchedulePlanStatusContract.Generated);
        db.SchedulePlans.Add(SchedulePlan.FromGeneratedPlan(
            problem.OrganizationId,
            problem.EnvironmentId,
            SchedulePlanContractMapper.ToDomainSnapshot(basePlan)));
        db.ScheduleProblems.Add(new ScheduleProblemSnapshot(
            problem.ProblemId,
            problem.ContractVersion,
            problem.OrganizationId,
            problem.EnvironmentId,
            "fingerprint",
            JsonSerializer.Serialize(problem, SchedulingJson.Options),
            problem.HorizonStartUtc,
            problem.HorizonEndUtc,
            problem.HorizonStartUtc));
        var affected = basePlan.Assignments.Take(2).ToArray();
        foreach (var assignment in affected)
        {
            db.SchedulePlanInvalidations.Add(SchedulePlanInvalidation.Create(
                problem.OrganizationId,
                problem.EnvironmentId,
                basePlan.PlanId,
                "event-latest",
                "maintenance.AssetUnavailable",
                "maintenance",
                "equipmentUnavailable",
                assignment.ResourceId,
                assignment.OrderId,
                assignment.OperationId,
                null,
                problem.HorizonStartUtc.AddHours(2),
                problem.HorizonStartUtc.AddHours(2)));
        }
        await db.SaveChangesAsync();
        var original = basePlan.Assignments.First();
        var movedStart = original.StartUtc.AddMinutes(1);
        var movedLock = new SchedulingLockedAssignmentContract(
            original.AssignmentId,
            original.OrderId,
            original.OperationId,
            original.OperationSequence,
            original.ResourceId,
            original.WorkCenterId,
            movedStart,
            original.EndUtc.AddMinutes(1),
            "ui");
        var sender = new SchedulingCreateSender(problem.HorizonStartUtc);
        var handler = new CreateSchedulePlanRevisionCommandHandler(db, sender);

        var result = await handler.Handle(new CreateSchedulePlanRevisionCommand(
            basePlan.PlanId,
            problem.OrganizationId,
            problem.EnvironmentId,
            problem.Orders.Select(x => x.OrderId).ToArray(),
            [movedLock]), CancellationToken.None);

        var locked = Assert.Single(result.Candidate.Assignments, x => x.OperationId == original.OperationId);
        Assert.True(locked.IsLocked);
        Assert.Equal(movedStart, locked.StartUtc);
        Assert.True(result.Comparison.MovedOperationCount >= 1);
        Assert.True(result.Impact.IsInvalidated);
        Assert.Equal(2, result.Impact.AffectedOperationIds.Count);
        Assert.Equal("event-latest", result.Impact.SourceEventId);
    }

    private sealed class StubProductEngineeringClient : ISchedulingProblemProductEngineeringClient
    {
        public Task<SchedulingProblemRoutingSnapshot> GetRoutingAsync(
            string organizationId,
            string environmentId,
            string routingVersionId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SchedulingProblemProductionVersionSnapshot> GetProductionVersionRoutingAsync(
            string organizationId,
            string environmentId,
            string productionVersionId,
            CancellationToken cancellationToken) => Task.FromResult(
                new SchedulingProblemProductionVersionSnapshot(productionVersionId, "SKU-001", "ROUTE-001:A"));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-workbench-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class SchedulingCreateSender(DateTimeOffset generatedAtUtc) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateSchedulePlanCommand command)
            {
                var plan = SchedulePlanContractMapper.WithStatus(
                    new FiniteCapacityScheduler().Schedule(command.Problem, "plan-revision", generatedAtUtc),
                    SchedulePlanStatusContract.Generated);
                return Task.FromResult((TResponse)(object)plan);
            }
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static object WorkOrder(string workOrderId, DateTimeOffset start) => new
    {
        workOrderId,
        skuId = "SKU-001",
        skuCode = "SKU-001",
        productionVersionId = "pv-001",
        quantity = 1,
        priority = 10,
        dueUtc = start.AddDays(1),
        status = "released",
        workOrderNo = workOrderId,
        operationTasks = new[] { new { earliestStartUtc = start } },
    };
}
