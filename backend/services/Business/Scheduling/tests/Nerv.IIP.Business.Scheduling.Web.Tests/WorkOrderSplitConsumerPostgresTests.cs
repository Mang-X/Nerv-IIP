using System.Collections.Immutable;
using NetCorePal.Extensions.DistributedTransactions;
using Nerv.IIP.Contracts.Scheduling;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.SchedulePlanAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleOperationOverrideAggregate;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed partial class RecordSchedulePlanInvalidationsPostgresProfileTests
{
    // DomainInvariant: #4141 approved split lock rules and precise mother-order impact.
    [SchedulingPostgresFact]
    public async Task Postgres_split_invalidates_only_mother_operations_migrates_locks_and_deduplicates()
    {
        await AssertSplitFlowAsync(clearFirst: null);
        await AssertSplitFlowAsync(clearFirst: true);
        await AssertSplitFlowAsync(clearFirst: false);
    }

    private static async Task AssertSplitFlowAsync(bool? clearFirst)
    {
        await SchedulingPostgresLaneDatabase.ResetSchemaAsync();
        var source = new SplitSourceProvider();
        var publisher = new SplitPublisher();
        await using var provider = BuildEventHandlerProvider(source, publisher);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        SchedulingPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        var original = SchedulePlanContractMapper.ToContract(CreatePlanWithAssignment("plan-split-generated", "DEV-1"));
        var otherAssignment = original.Assignments.Single() with { AssignmentId = "assign-other", OrderId = "WO-OTHER", OperationId = "OP-OTHER" };
        var generated = SchedulePlan.FromGeneratedPlan("org-001", "env-dev", SchedulePlanContractMapper.ToDomainSnapshot(
            original with { Assignments = original.Assignments.Append(otherAssignment).ToArray() }));
        var released = CreatePlanWithAssignment("plan-split-released", "DEV-1");
        released.Release(FixedNow, 1);
        // Another order in the same plan must not become an affected operation.
        var mixed = CreatePlanWithAssignmentIdentity("plan-split-other", "WO-OTHER", "OP-OTHER", "problem-other");
        db.SchedulePlans.AddRange(generated, released, mixed);
        var parent = ScheduleOperationOverride.Create(
            "org-001", "env-dev", "WO-001", "OP-001", 10, "DEV-1", "WC-CNC",
            FixedNow, FixedNow.AddTicks(1000), "manual-override", "scheduling-api", null,
            "user:test", FixedNow, FixedNow);
        if (clearFirst.HasValue)
        {
            Assert.True(parent.TryApplyMesDispatch("DEV-1", "WC-CNC", FixedNow, FixedNow.AddTicks(1000),
                "dispatch-1", "user:test", 1, FixedNow, FixedNow));
        }
        db.ScheduleOperationOverrides.Add(parent);
        await db.SaveChangesAsync();
        var integrationEvent = SplitEvent("WO-001", "split-locked");
        var consumer = new WorkOrderSplitIntegrationEventHandlerForApplySplit(db,
            new InMemoryIntegrationEventDeadLetterStore(), scope.ServiceProvider.GetRequiredService<MediatR.ISender>());
        var clearConsumer = new MesOperationTaskManualDispatchClearedIntegrationEventHandlerForClearOverride(
            db, new InMemoryIntegrationEventDeadLetterStore());
        var clear = new MesOperationTaskManualDispatchClearedIntegrationEvent(
            "split-clear", MesIntegrationEventTypes.OperationTaskManualDispatchCleared, 1, FixedNow,
            MesIntegrationEventSources.BusinessMes, "corr-split", "cause-split", "org-001", "env-dev",
            "user:test", "split-clear", new OperationTaskManualDispatchClearedPayload(
                "WO-001", "OP-001", 10, "DEV-1", "WC-CNC", FixedNow, FixedNow.AddTicks(1000),
                2, MesManualDispatchClearReasonCodes.OperationCancelled, FixedNow));
        if (clearFirst == true) await clearConsumer.HandleAsync(clear, default);
        await consumer.HandleAsync(integrationEvent, default);
        if (clearFirst == false) await clearConsumer.HandleAsync(clear, default);
        db.ChangeTracker.Clear();
        await consumer.HandleAsync(integrationEvent, default);
        var invalidations = await db.SchedulePlanInvalidations.ToArrayAsync();
        Assert.Equal(2, invalidations.Length);
        Assert.All(invalidations, invalidation =>
        {
            Assert.Equal("WO-001", invalidation.AffectedWorkOrderId);
        });
        var locks = await db.ScheduleOperationOverrides.OrderBy(x => x.WorkOrderId).ToArrayAsync();
        Assert.Equal(3, locks.Length);
        Assert.All(publisher.Invalidations, published =>
        {
            var operation = Assert.Single(published.Payload.AffectedOperations);
            Assert.Equal("WO-001", operation.WorkOrderId);
            Assert.Equal("OP-001", operation.OperationId);
        });
        Assert.Equal(2, publisher.Invalidations.Count);
        Assert.False(locks.Single(x => x.WorkOrderId == "WO-001").IsActive);
        Assert.Equal(["CHILD-A", "CHILD-B"], locks.Where(x => x.IsActive).Select(x => x.WorkOrderId).Order());
        var children = locks.Where(x => x.IsActive).OrderBy(x => x.WorkOrderId).ToArray();
        Assert.Equal(FixedNow, children[0].StartUtc);
        Assert.Equal(FixedNow.AddTicks(330), children[0].EndUtc);
        Assert.Equal(children[0].EndUtc, children[1].StartUtc);
        Assert.Equal(FixedNow.AddTicks(1000), children[1].EndUtc);
        Assert.Equal(["CHILD-A-OP", "CHILD-B-OP"], children.Select(x => x.OperationId));
        Assert.All(children, child => { Assert.Equal("DEV-1", child.ResourceId); Assert.Equal("WC-CNC", child.WorkCenterId); });
        Assert.Equal(clearFirst.HasValue ? 2 : 1, await db.ProcessedIntegrationEvents.CountAsync());
        Assert.Equal(1, source.ReadCount);

        // A split without a parent override does not invent any child lock or read MES operations.
        await consumer.HandleAsync(SplitEvent("WO-NO-LOCK", "split-unlocked"), default);
        Assert.Equal(3, await db.ScheduleOperationOverrides.CountAsync());
        Assert.Equal(1, source.ReadCount);

        // Earlier cancellations and ordinary device clears must remain revoked, even if a later split arrives.
        db.ScheduleOperationOverrides.AddRange(
            ScheduleOperationOverride.CreateClearedMesDispatch("org-001", "env-dev", "WO-CANCELLED", "OP-CANCELLED",
                10, "DEV-1", "WC-CNC", FixedNow, FixedNow.AddTicks(1000), "earlier-clear", "user:test", 2,
                FixedNow.AddMinutes(-1), MesManualDispatchClearReasonCodes.OperationCancelled, FixedNow.AddMinutes(-1)),
            ScheduleOperationOverride.CreateClearedMesDispatch("org-001", "env-dev", "WO-DEVICE-CLEARED", "OP-DEVICE-CLEARED",
                10, "DEV-1", "WC-CNC", FixedNow, FixedNow.AddTicks(1000), "device-clear", "user:test", 2,
                FixedNow, MesManualDispatchClearReasonCodes.DeviceCleared, FixedNow));
        await db.SaveChangesAsync();
        await consumer.HandleAsync(SplitEvent("WO-CANCELLED", "split-earlier-cancelled"), default);
        await consumer.HandleAsync(SplitEvent("WO-DEVICE-CLEARED", "split-device-cleared"), default);
        Assert.Equal(5, await db.ScheduleOperationOverrides.CountAsync());
        Assert.Equal(1, source.ReadCount);
    }

    private static WorkOrderSplitIntegrationEvent SplitEvent(string sourceId, string eventId) => new(
        eventId, MesIntegrationEventTypes.WorkOrderSplit, 1, FixedNow,
        MesIntegrationEventSources.BusinessMes, "corr-split", "cause-split", "org-001", "env-dev",
        "user:test", eventId, new WorkOrderTransformationPayload(Guid.Parse("00000000-0000-0000-0000-000000000041"), "split",
            ImmutableArray.Create(
                new WorkOrderTransformationLinePayload(sourceId, "CHILD-B", 2m, 3m, 2m, "EA", "split", "released", 2, 1),
                new WorkOrderTransformationLinePayload(sourceId, "CHILD-A", 1m, 3m, 1m, "EA", "split", "released", 2, 1))));

    private sealed class SplitPublisher : IIntegrationEventPublisher
    {
        public List<SchedulePlanInvalidatedIntegrationEvent> Invalidations { get; } = [];
        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            if (integrationEvent is SchedulePlanInvalidatedIntegrationEvent invalidation) Invalidations.Add(invalidation);
            return Task.CompletedTask;
        }
    }

    private sealed class SplitSourceProvider : ISchedulingWorkbenchSourceProvider
    {
        public int ReadCount { get; private set; }
        public Task<IReadOnlyCollection<SchedulingWorkbenchProblemSourceOrder>> ResolveOrdersAsync(
            string organizationId, string environmentId, DateTimeOffset earliestStartFallbackUtc,
            IReadOnlyCollection<SchedulingWorkbenchOrderSelection> selections, CancellationToken cancellationToken)
        {
            ReadCount++;
            IReadOnlyCollection<SchedulingWorkbenchProblemSourceOrder> result = selections.Select(x =>
                new SchedulingWorkbenchProblemSourceOrder(new SchedulingProblemSourceOrder(
                    x.WorkOrderId, "SKU-001", 1m, FixedNow.AddDays(1), 50, true, FixedNow, "ROUTE-001"),
                    [new SchedulingWorkbenchOperationSource(x.WorkOrderId + "-OP", 10)])).ToArray();
            return Task.FromResult(result);
        }
    }
}
