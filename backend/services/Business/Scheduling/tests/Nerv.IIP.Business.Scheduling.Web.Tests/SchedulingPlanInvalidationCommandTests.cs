using Microsoft.EntityFrameworkCore;
using MediatR;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Domain.DomainEvents;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class SchedulingPlanInvalidationCommandTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Record_invalidations_for_resource_adds_domain_events_for_unit_of_work_outbox_publish()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SchedulePlans.Add(CreatePlan("plan-generated", SchedulePlanStatusContract.Generated));
        var released = CreatePlan("plan-released", SchedulePlanStatusContract.Generated);
        released.Release(FixedNow);
        dbContext.SchedulePlans.Add(released);
        await dbContext.SaveChangesAsync();

        var handler = new RecordSchedulePlanInvalidationsCommandHandler(dbContext, new FixedTimeProvider(FixedNow));

        await handler.Handle(new RecordSchedulePlanInvalidationsCommand(
            OrganizationId: "org-001",
            EnvironmentId: "env-dev",
            SourceEventId: "evt-maint-001",
            SourceEventType: "maintenance.AssetUnavailable",
            SourceService: "maintenance",
            OccurredAtUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
            ReasonCode: SchedulingPlanInvalidationReasons.EquipmentUnavailable,
            Scope: SchedulePlanInvalidationScope.Resource,
            ScopeValue: "ASSET-CNC-01",
            AffectedWorkOrderId: null,
            AffectedSkuCode: null), CancellationToken.None);

        var invalidations = dbContext.SchedulePlanInvalidations.Local.OrderBy(x => x.PlanId).ToArray();
        Assert.Equal(["plan-generated", "plan-released"], invalidations.Select(x => x.PlanId));
        var domainEvents = invalidations
            .SelectMany(x => x.GetDomainEvents().OfType<SchedulePlanInvalidatedDomainEvent>())
            .OrderBy(x => x.Invalidation.PlanId)
            .ToArray();
        Assert.Equal(2, domainEvents.Length);
        Assert.All(domainEvents, domainEvent =>
        {
            Assert.Equal("ASSET-CNC-01", domainEvent.Invalidation.AffectedResourceId);
            Assert.Equal(SchedulingPlanInvalidationReasons.EquipmentUnavailable, domainEvent.Invalidation.ReasonCode);
            Assert.Equal(SchedulePlanLifecycleStatus.Released, domainEvents.Single(x => x.Invalidation.PlanId == "plan-released").Plan.Status);
        });
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scheduling-invalidation-command-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static SchedulePlan CreatePlan(string planId, SchedulePlanStatusContract status)
    {
        return SchedulePlan.FromGeneratedPlan(
            "org-001",
            "env-dev",
            SchedulePlanContractMapper.ToDomainSnapshot(new SchedulePlanContract(
                ContractVersion: 1,
                PlanId: planId,
                ProblemId: "problem-001",
                ProblemFingerprint: $"fingerprint-{planId}",
                AlgorithmVersion: "aps-lite-v1",
                Status: status,
                GeneratedAtUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                Metrics: new SchedulePlanMetricsContract(
                    ScheduledOperationCount: 1,
                    UnscheduledOperationCount: 0,
                    AssignedMinutes: 60,
                    MakespanMinutes: 60,
                    TotalTardinessMinutes: 0,
                    LateOperationCount: 0,
                    OnTimeRate: 1m,
                    AverageResourceUtilization: 0m),
                Assignments:
                [
                    new ScheduleAssignmentContract(
                        AssignmentId: $"assign-{planId}",
                        OrderId: "WO-001",
                        OperationId: "OP-001",
                        OperationSequence: 10,
                        ResourceId: "ASSET-CNC-01",
                        WorkCenterId: "WC-CNC",
                        StartUtc: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
                        EndUtc: new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero),
                        IsLocked: false,
                        ExplanationCode: "scheduled")
                ],
                ResourceLoads: [],
                Conflicts: [],
                UnscheduledOperations: [],
                ChangeSummary: [],
                GanttItems: [])));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            return Task.CompletedTask;
        }
    }
}
