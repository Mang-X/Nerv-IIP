using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Mes;
using Microsoft.EntityFrameworkCore;
using NetCorePal.Extensions.Primitives;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Approvals;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Errors;
using Nerv.IIP.Business.Mes.Web.Application.Queries.WorkOrders;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class WorkOrderTransformationApplicationTests
{
    // #3469 narrow pipeline contract only: the real provider race is covered separately.
    [Fact]
    public async Task Reversal_version_conflict_is_bounded_and_becomes_a_lifecycle_conflict()
    {
        await using var db = CreateContext();
        var behavior = new WorkOrderConcurrencyRetryBehavior<ReverseProductionReportCommand, ReverseProductionReportCommandResult>(db);
        var command = new ReverseProductionReportCommand("org-001", "env-dev", "PR-001", "更正报工",
            DateTimeOffset.UnixEpoch, "operator-001", "reverse-conflict");
        var attempts = 0;
        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() => behavior.Handle(command, _ =>
        {
            Assert.Empty(db.ChangeTracker.Entries());
            attempts++;
            var order = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-001", "PV-001",
                10m, 1, DateTimeOffset.UnixEpoch.AddDays(1));
            var entry = db.WorkOrders.Attach(order);
            throw new WorkOrderVersionConflict(entry);
        }, CancellationToken.None));
        Assert.Equal(3, attempts);
        Assert.Equal("concurrent-update", exception.CurrentStatus);
        Assert.DoesNotContain("provider-private-diagnostics", exception.Message);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Split_persists_lineage_and_replays_the_same_idempotency_key()
    {
        await using var db = CreateContext();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T02:00:00Z");
        db.WorkOrders.Add(WorkOrder.Create(
            "org-001", "env-dev", "WO-SPLIT-PARENT", "SKU-001", "PV-001", 10m, 10,
            occurredAtUtc.AddHours(4), "PCS",
            new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-CHILD", "SO-1",
                assemblyParentSuggestionIds: ["SUG-ASSEMBLY"])));
        db.OperationTasks.Add(OperationTask.Queue("org-001", "env-dev", "WO-SPLIT-PARENT", "SOURCE-OP",
            10, "WC-1", ["WC-2"], occurredAtUtc, TimeSpan.FromMinutes(20), "SKU-001", "PCS", 20m, true, "CUT", "SKILL-1"));
        await db.SaveChangesAsync();

        var command = new SplitWorkOrderCommand(
            "org-001",
            "env-dev",
            "WO-SPLIT-PARENT",
            [
                new("WO-SPLIT-CHILD-1", 4m),
                new("WO-SPLIT-CHILD-2", 6m),
            ],
            "按客户批次拆分",
            "split-application-001",
            "user:planner-001",
            occurredAtUtc);
        var handler = new SplitWorkOrderCommandHandler(db);

        var first = await handler.Handle(command, CancellationToken.None);
        var fact = db.WorkOrderTransformations.Local.Single();
        var splitEvent = new WorkOrderSplitIntegrationEventConverter(new TransformationEventContextAccessor()).Convert(
            Assert.IsType<WorkOrderSplitDomainEvent>(Assert.Single(fact.GetDomainEvents())));
        Assert.Equal(fact.Id.Id, splitEvent.Payload.TransformationId);
        Assert.Equal(command.OccurredAtUtc, splitEvent.OccurredAtUtc);
        Assert.Equal(command.Actor, splitEvent.Actor);
        Assert.Equal(command.Reason, splitEvent.Payload.Reason);
        Assert.Equal(command.OrganizationId, splitEvent.OrganizationId);
        Assert.Equal(command.EnvironmentId, splitEvent.EnvironmentId);
        Assert.Equal(MesIntegrationEventTypes.WorkOrderSplit, splitEvent.EventType);
        Assert.Equal(MesIntegrationEventVersions.V1, splitEvent.EventVersion);
        Assert.Equal(MesIntegrationEventSources.BusinessMes, splitEvent.SourceService);
        Assert.Equal("corr-transformation", splitEvent.CorrelationId);
        Assert.Equal("cause-transformation", splitEvent.CausationId);
        Assert.Equal(["WO-SPLIT-PARENT", "WO-SPLIT-PARENT"], splitEvent.Payload.Lines.Select(x => x.SourceWorkOrderId));
        Assert.Equal(["WO-SPLIT-CHILD-1", "WO-SPLIT-CHILD-2"], splitEvent.Payload.Lines.Select(x => x.TargetWorkOrderId));
        Assert.Equal([4m, 6m], splitEvent.Payload.Lines.Select(x => x.Quantity));
        Assert.All(splitEvent.Payload.Lines, line =>
        {
            Assert.Equal(10m, line.SourceQuantity);
            Assert.Equal(line.Quantity, line.TargetQuantity);
            Assert.Equal("PCS", line.UomCode);
            Assert.Equal(WorkOrder.CreatedStatus, line.SourceStatus);
            Assert.Equal(WorkOrder.CreatedStatus, line.TargetStatus);
            Assert.Equal(1, line.SourceVersion);
            Assert.Equal(1, line.TargetVersion);
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var replay = await handler.Handle(command, CancellationToken.None);
        Assert.Empty(db.WorkOrderTransformations.Local);
        var parent = await db.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-SPLIT-PARENT");
        var children = await db.WorkOrders
            .Where(x => x.WorkOrderIdValue.StartsWith("WO-SPLIT-CHILD-"))
            .OrderBy(x => x.WorkOrderIdValue)
            .ToArrayAsync();
        var readback = await new GetWorkOrderTransformationQueryHandler(db).Handle(
            new("org-001", "env-dev", first.TransformationId),
            CancellationToken.None);

        var operations = await db.OperationTasks.OrderBy(x => x.WorkOrderId).ToArrayAsync();
        Assert.Equal(3, operations.Length);
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled,
            operations.Single(x => x.WorkOrderId == "WO-SPLIT-PARENT").Status);
        var childOperations = operations.Where(x => x.WorkOrderId != "WO-SPLIT-PARENT").ToArray();
        Assert.Equal([8m, 12m], childOperations.Select(x => x.PlannedQuantity));
        Assert.All(childOperations, operation =>
        {
            Assert.Equal(OperationTaskLifecycleStatus.Queued, operation.Status);
            Assert.Equal(10, operation.OperationSequence);
            Assert.Equal("WC-1", operation.WorkCenterId);
            Assert.Equal(["WC-2"], operation.AlternativeWorkCenterIdList);
            Assert.Equal(TimeSpan.FromMinutes(20).Ticks, operation.DurationTicks);
            Assert.Equal("SKU-001", operation.SkuCode);
            Assert.Equal("PCS", operation.UomCode);
            Assert.True(operation.RequiresQualityInspection);
            Assert.Equal("CUT", operation.OperationCode);
            Assert.Equal("SKILL-1", operation.RequiredSkillCode);
            Assert.Null(operation.ExistingStartUtc);
            Assert.Null(operation.ExistingEndUtc);
            Assert.Null(operation.AssignedUserId);
            Assert.Null(operation.SchedulePlanId);
        });
        Assert.False(first.IsIdempotentReplay);
        Assert.True(replay.IsIdempotentReplay);
        Assert.Equal(first.TransformationId, replay.TransformationId);
        Assert.Equal(["WO-SPLIT-CHILD-1", "WO-SPLIT-CHILD-2"], first.TargetWorkOrderIds);
        Assert.Equal(WorkOrder.SplitStatus, parent.Status);
        Assert.Equal(2, parent.Version);
        Assert.Equal([4m, 6m], children.Select(x => x.Quantity));
        Assert.All(children, child => Assert.Equal(["SUG-ASSEMBLY"],
            child.SourcePlanReference?.AssemblyParentSuggestionIds));
        foreach (var child in children)
        {
            child.RecordMaterialRequirementSnapshot(WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus, child.CreatedAtUtc);
            await db.SaveChangesAsync();
            await new ReleaseWorkOrderCommandHandler(db).Handle(
                new("org-001", "env-dev", child.WorkOrderIdValue, child.CreatedAtUtc.AddMinutes(1)), CancellationToken.None);
            Assert.Equal(WorkOrder.ReleasedStatus, child.Status);
        }
        await Assert.ThrowsAsync<MesLifecycleConflictException>(() => new ChangeOperationTaskStateCommandHandler(db)
            .Handle(new("org-001", "env-dev", "SOURCE-OP", "start", occurredAtUtc), CancellationToken.None));
        Assert.Equal(2, readback.Lines.Count);
        Assert.Equal(10m, readback.Lines.Sum(x => x.Quantity));
        Assert.All(readback.Lines, line => Assert.Equal("WO-SPLIT-PARENT", line.SourceWorkOrderId));
    }

    [Fact]
    public async Task Merge_rejects_a_different_payload_reusing_the_same_idempotency_key()
    {
        await using var db = CreateContext();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T03:00:00Z");
        db.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-MERGE-SOURCE-1", "SKU-001", "PV-001", 3m, 10, occurredAtUtc.AddHours(4), "PCS",
                new SourcePlanReference("DemandPlanning", "PlanningSuggestion", "SUG-CHILD", "SO-1",
                    assemblyParentSuggestionIds: ["SUG-ASSEMBLY"])),
            WorkOrder.Create("org-001", "env-dev", "WO-MERGE-SOURCE-2", "SKU-001", "PV-001", 7m, 10, occurredAtUtc.AddHours(4), "PCS"));
        db.OperationTasks.AddRange(
            OperationTask.Queue("org-001", "env-dev", "WO-MERGE-SOURCE-1", "MERGE-OP-1", 10,
                "WC-1", [], occurredAtUtc, TimeSpan.FromMinutes(20), "SKU-001", "PCS", 6m),
            OperationTask.Queue("org-001", "env-dev", "WO-MERGE-SOURCE-2", "MERGE-OP-2", 10,
                "WC-1", [], occurredAtUtc, TimeSpan.FromMinutes(20), "SKU-001", "PCS", 14m));
        await db.SaveChangesAsync();

        var handler = new MergeWorkOrdersCommandHandler(db);
        var first = new MergeWorkOrdersCommand(
            "org-001",
            "env-dev",
            ["WO-MERGE-SOURCE-1", "WO-MERGE-SOURCE-2"],
            "WO-MERGE-TARGET",
            "合并同 SKU 小单",
            "merge-application-001",
            "user:planner-001",
            occurredAtUtc);
        await handler.Handle(first, CancellationToken.None);
        var fact = db.WorkOrderTransformations.Local.Single();
        var mergedEvent = new WorkOrderMergedIntegrationEventConverter(new TransformationEventContextAccessor()).Convert(
            Assert.IsType<WorkOrderMergedDomainEvent>(Assert.Single(fact.GetDomainEvents())));
        Assert.Equal(fact.Id.Id, mergedEvent.Payload.TransformationId);
        Assert.Equal(first.OccurredAtUtc, mergedEvent.OccurredAtUtc);
        Assert.Equal(first.Actor, mergedEvent.Actor);
        Assert.Equal(first.Reason, mergedEvent.Payload.Reason);
        Assert.Equal(first.OrganizationId, mergedEvent.OrganizationId);
        Assert.Equal(first.EnvironmentId, mergedEvent.EnvironmentId);
        Assert.Equal(MesIntegrationEventTypes.WorkOrderMerged, mergedEvent.EventType);
        Assert.Equal(["WO-MERGE-SOURCE-1", "WO-MERGE-SOURCE-2"], mergedEvent.Payload.Lines.Select(x => x.SourceWorkOrderId));
        Assert.Equal(["WO-MERGE-TARGET", "WO-MERGE-TARGET"], mergedEvent.Payload.Lines.Select(x => x.TargetWorkOrderId));
        Assert.Equal([3m, 7m], mergedEvent.Payload.Lines.Select(x => x.Quantity));
        Assert.All(mergedEvent.Payload.Lines, line =>
        {
            Assert.Equal(line.Quantity, line.SourceQuantity);
            Assert.Equal(10m, line.TargetQuantity);
            Assert.Equal("PCS", line.UomCode);
            Assert.Equal(WorkOrder.CreatedStatus, line.SourceStatus);
            Assert.Equal(WorkOrder.CreatedStatus, line.TargetStatus);
            Assert.Equal(1, line.SourceVersion);
            Assert.Equal(1, line.TargetVersion);
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var merged = await db.WorkOrders.SingleAsync(x => x.WorkOrderIdValue == "WO-MERGE-TARGET");
        Assert.Equal(["SUG-ASSEMBLY"], merged.SourcePlanReference?.AssemblyParentSuggestionIds);
        var mergedOperation = await db.OperationTasks.SingleAsync(x => x.WorkOrderId == "WO-MERGE-TARGET");
        Assert.Equal(20m, mergedOperation.PlannedQuantity);
        Assert.Equal(OperationTaskLifecycleStatus.Queued, mergedOperation.Status);
        Assert.All(await db.OperationTasks.Where(x => x.WorkOrderId != "WO-MERGE-TARGET").ToArrayAsync(),
            operation => Assert.Equal(OperationTaskLifecycleStatus.Cancelled, operation.Status));

        merged.RecordMaterialRequirementSnapshot(WorkOrder.MaterialRequirementSnapshotNoRequirementsStatus, merged.CreatedAtUtc);
        await db.SaveChangesAsync();
        await new ReleaseWorkOrderCommandHandler(db).Handle(
            new("org-001", "env-dev", merged.WorkOrderIdValue, merged.CreatedAtUtc.AddMinutes(1)), CancellationToken.None);
        Assert.Equal(WorkOrder.ReleasedStatus, merged.Status);

        var conflicting = first with { TargetWorkOrderId = "WO-MERGE-TARGET-OTHER" };
        await Assert.ThrowsAsync<MesIdempotencyConflictException>(() =>
            handler.Handle(conflicting, CancellationToken.None));

        Assert.Equal("idempotency-conflict", MesIdempotencyConflictException.SafeCode);
        Assert.Equal(1, await db.WorkOrderTransformations.CountAsync());
        Assert.Equal(3, await db.WorkOrders.CountAsync());
    }

    [Fact]
    public async Task Split_maps_non_transformable_source_to_a_lifecycle_conflict()
    {
        await using var db = CreateContext();
        var source = WorkOrder.Create(
            "org-001", "env-dev", "WO-SPLIT-STARTED", "SKU-001", "PV-001", 10m, 10,
            DateTimeOffset.Parse("2026-08-26T04:00:00Z"), "PCS");
        source.MarkReleased();
        source.MarkSplit();
        db.WorkOrders.Add(source);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new SplitWorkOrderCommandHandler(db).Handle(
                new SplitWorkOrderCommand(
                    "org-001", "env-dev", "WO-SPLIT-STARTED",
                    [new("WO-SPLIT-CHILD-1", 4m), new("WO-SPLIT-CHILD-2", 6m)],
                    "重复拆分", "split-application-conflict", "user:planner-001",
                    DateTimeOffset.Parse("2026-08-26T04:00:00Z")),
                CancellationToken.None));

        Assert.Equal("work-order-transformation", exception.Action);
        Assert.Equal("invalid-split", exception.CurrentStatus);
        Assert.Equal(0, await db.WorkOrderTransformations.CountAsync());
    }

    [Fact]
    public async Task Split_requeues_frozen_route_without_execution_or_assignment_and_preserves_completed_history()
    {
        await using var db = CreateContext();
        var at = DateTimeOffset.UnixEpoch;
        db.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-PARENT", "SKU-001", "PV-001", 10m, 1, at, "PCS"));
        var active = OperationTask.Queue("org-001", "env-dev", "WO-PARENT", "OP-ACTIVE", 20,
            "WC-1", [], at, TimeSpan.FromMinutes(20), "SKU-001", "PCS", 10m);
        active.ApplyScheduleAssignment("WC-1", "DEVICE-1", at, at.AddMinutes(20), at,
            schedulePlanId: "PLAN-1", scheduleReleaseRevision: 1);
        active.Assign("worker-1", "DEVICE-1", "shift-1", at, "user:planner-001");
        db.OperationTasks.AddRange(active, OperationTask.Create("org-001", "env-dev", "WO-PARENT", "OP-COMPLETED",
            OperationTaskLifecycleStatus.Completed, 10, "WC-1", [], at, TimeSpan.FromMinutes(20), at,
            at.AddMinutes(20), "SKU-001", "PCS", 10m));
        await db.SaveChangesAsync();
        var longId = new string('T', 100);
        await new SplitWorkOrderCommandHandler(db).Handle(new("org-001", "env-dev", "WO-PARENT",
            [new(longId, 4m), new("WO-OTHER", 6m)], "拆分", "split-long", "user:planner-001", at.AddMinutes(30)), CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var operations = await db.OperationTasks.ToArrayAsync();
        Assert.Equal(OperationTaskLifecycleStatus.Completed, operations.Single(x => x.OperationTaskIdValue == "OP-COMPLETED").Status);
        var cancelled = operations.Single(x => x.OperationTaskIdValue == "OP-ACTIVE");
        Assert.Equal(OperationTaskLifecycleStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.HasActiveManualDispatch);
        Assert.All(operations.Where(x => x.WorkOrderId != "WO-PARENT"), operation =>
        {
            Assert.InRange(operation.OperationTaskIdValue.Length, 1, 100);
            Assert.Equal(OperationTaskLifecycleStatus.Queued, operation.Status);
            Assert.Null(operation.ExistingStartUtc);
            Assert.Null(operation.ExistingEndUtc);
            Assert.Null(operation.AssignedUserId);
            Assert.Null(operation.DeviceAssetId);
            Assert.Null(operation.SchedulePlanId);
        });
        await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new AuthorizeAndStartOperationTaskCommandHandler(db, new UnusedApprovalClient(), TimeProvider.System).Handle(
                new("org-001", "env-dev", "OP-ACTIVE", "跳站", "CHAIN-1", "correlation-1", "auth-1"), CancellationToken.None));
    }

    private sealed class UnusedApprovalClient : IMesOperationTaskStartApprovalClient
    {
        public Task<MesOperationTaskStartApproval?> GetApprovedAsync(string approvalChainId, string organizationId,
            string environmentId, string operationTaskId, string workOrderId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cancelled operation must be rejected before approval lookup.");
    }

    // DomainInvariant / Regression: #4031 rejects unusable transformation inputs before changing sources.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transformation_without_source_operations_is_rejected(bool merge)
    {
        await using var db = CreateContext();
        var at = DateTimeOffset.UnixEpoch;
        db.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-A", "SKU-001", "PV-001", 4m, 1, at, "PCS"),
            WorkOrder.Create("org-001", "env-dev", "WO-B", "SKU-001", "PV-001", 6m, 1, at, "PCS"));
        await db.SaveChangesAsync();
        if (merge)
        {
            await Assert.ThrowsAsync<KnownException>(() => new MergeWorkOrdersCommandHandler(db).Handle(
                new("org-001", "env-dev", ["WO-A", "WO-B"], "WO-TARGET", "合并", "missing-merge", "planner", at), CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<KnownException>(() => new SplitWorkOrderCommandHandler(db).Handle(
                new("org-001", "env-dev", "WO-A", [new("WO-C", 1m), new("WO-D", 3m)], "拆分", "missing-split", "planner", at), CancellationToken.None));
        }
        Assert.All(await db.WorkOrders.ToArrayAsync(), source => Assert.Equal(WorkOrder.CreatedStatus, source.Status));
        Assert.Empty(db.WorkOrderTransformations.Local);
        Assert.Equal(2, await db.WorkOrders.CountAsync());
    }

    [Theory]
    [InlineData("center")]
    [InlineData("duration")]
    [InlineData("sequence")]
    [InlineData("skill")]
    public async Task Merge_with_different_frozen_routes_is_rejected(string difference)
    {
        await using var db = CreateContext();
        var at = DateTimeOffset.UnixEpoch;
        db.WorkOrders.AddRange(
            WorkOrder.Create("org-001", "env-dev", "WO-A", "SKU-001", "PV-001", 4m, 1, at, "PCS"),
            WorkOrder.Create("org-001", "env-dev", "WO-B", "SKU-001", "PV-001", 6m, 1, at, "PCS"));
        db.OperationTasks.AddRange(
            OperationTask.Queue("org-001", "env-dev", "WO-A", "OP-A", 10, "WC-1", [], at,
                TimeSpan.FromMinutes(20), "SKU-001", "PCS", 4m, requiredSkillCode: "SKILL-1"),
            OperationTask.Queue("org-001", "env-dev", "WO-B", "OP-B", difference == "sequence" ? 20 : 10,
                difference == "center" ? "WC-2" : "WC-1", [], at,
                TimeSpan.FromMinutes(difference == "duration" ? 30 : 20), "SKU-001", "PCS", 6m,
                requiredSkillCode: difference == "skill" ? "SKILL-2" : "SKILL-1"));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<KnownException>(() => new MergeWorkOrdersCommandHandler(db).Handle(
            new("org-001", "env-dev", ["WO-A", "WO-B"], "WO-TARGET", "合并", "route-conflict", "planner", at), CancellationToken.None));
        Assert.All(await db.WorkOrders.ToArrayAsync(), source => Assert.Equal(WorkOrder.CreatedStatus, source.Status));
        Assert.All(await db.OperationTasks.ToArrayAsync(), operation => Assert.Equal(OperationTaskLifecycleStatus.Queued, operation.Status));
        Assert.Empty(db.WorkOrderTransformations.Local);
        Assert.Equal(2, await db.WorkOrders.CountAsync());
    }

    private sealed class WorkOrderVersionConflict(EntityEntry entry)
        : DbUpdateConcurrencyException("provider-private-diagnostics")
    {
        public override IReadOnlyList<EntityEntry> Entries => [entry];
    }

    [Fact]
    public async Task Split_rejects_a_share_below_persisted_precision_without_changing_sources()
    {
        await using var db = CreateContext();
        var occurredAtUtc = DateTimeOffset.Parse("2026-08-26T02:00:00Z");
        db.WorkOrders.Add(WorkOrder.Create("org-001", "env-dev", "WO-PRECISION", "SKU-001", "PV-001",
            3m, 10, occurredAtUtc.AddHours(4), "PCS"));
        db.OperationTasks.Add(OperationTask.Queue("org-001", "env-dev", "WO-PRECISION", "PRECISION-OP",
            10, "WC-1", [], occurredAtUtc, TimeSpan.FromMinutes(20), "SKU-001", "PCS", 0.000001m));
        await db.SaveChangesAsync();
        var command = new SplitWorkOrderCommand("org-001", "env-dev", "WO-PRECISION",
            [new("WO-PRECISION-A", 1m), new("WO-PRECISION-B", 2m)], "精度不足", "precision-001",
            "user:planner-001", occurredAtUtc);

        await Assert.ThrowsAsync<MesLifecycleConflictException>(() =>
            new SplitWorkOrderCommandHandler(db).Handle(command, CancellationToken.None));
        Assert.Equal(WorkOrder.CreatedStatus, (await db.WorkOrders.SingleAsync()).Status);
        Assert.Equal(OperationTaskLifecycleStatus.Queued, (await db.OperationTasks.SingleAsync()).Status);
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State == EntityState.Added);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-work-order-transformation-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
    private sealed class TransformationEventContextAccessor : IMesIntegrationEventContextAccessor
    {
        public MesIntegrationEventContext GetContext() => new("corr-transformation", "cause-transformation");
    }

}
