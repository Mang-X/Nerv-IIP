using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Infrastructure.Repositories;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Schedules;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Business.Mes.Web.Application.Planning;
using Nerv.IIP.Business.Mes.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// #3112 的真库回归：加急建单 → 下达 → 完工，跑生产命令 handler 与生产转换器，
/// 断言同一道工序的 <c>WorkOrderReleased.SkuCode</c> 与完工事件 <c>SkuCode</c> 相等。
///
/// <para>必须走真 PostgreSQL：这条链路把工序在**两次不同的命令、两个不同的聚合**里落库又读回，
/// 而缺陷恰恰是"落库那一刻写进 sku_code 列的值不对"。InMemory provider 看不见列约束，
/// 也证不到 <c>PersistentMesPlanningStore</c> 的投影确实把 SKU 带回来了。</para>
///
/// <para>rush 路径是本票新增 SKU 字段的那条（<c>PlannedOperationTask</c> 以前根本没有 SKU 字段，
/// 于是 <c>PersistentMesPlanningStore</c> 落库时"拿不到"而非"忘了传"）。</para>
/// </summary>
[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class MesOperationTaskSkuSourcePostgresTests
{
    [MesRealPostgresFact(Timeout = 120_000)]
    public async Task Rush_release_and_completion_events_agree_on_the_work_order_sku_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var dbContext = new ApplicationDbContext(MesPostgresLaneDatabase.CreateOptions(), new NoopMediator());
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();

        var now = DateTimeOffset.Parse("2026-09-09T08:00:00Z");
        var store = new PersistentMesPlanningStore(dbContext, new OperationTaskRepository(dbContext));

        var rushResponse = await new CreateRushWorkOrderCommandHandler(store, new RuleScheduler(), new MesCodingService(), dbContext)
            .Handle(
                new CreateRushWorkOrderCommand(
                    "org-001", "env-dev", "WO-3112-RUSH", "SKU-FG-3112-RUSH", "PV-001", 5m,
                    now.AddDays(1), "WC-RUSH", "OP-3112-RUSH-10", 10, TimeSpan.FromMinutes(30), now,
                    "rush-3112"),
                CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var workOrderId = rushResponse.WorkOrderId;
        var persistedTask = await dbContext.OperationTasks.AsNoTracking()
            .SingleAsync(x => x.OperationTaskIdValue == "OP-3112-RUSH-10", CancellationToken.None);
        var persistedWorkOrder = await dbContext.WorkOrders.AsNoTracking()
            .SingleAsync(x => x.WorkOrderIdValue == workOrderId, CancellationToken.None);

        // rush 路径断言：落库的工序带工单真实 SKU，而不是工单号。
        Assert.Equal("SKU-FG-3112-RUSH", persistedWorkOrder.SkuId);
        Assert.Equal(persistedWorkOrder.SkuId, persistedTask.SkuCode);
        Assert.NotEqual(workOrderId, persistedTask.SkuCode);

        await new ReleaseWorkOrderCommandHandler(dbContext, NoRequirementsProvider.Instance)
            .Handle(new ReleaseWorkOrderCommand("org-001", "env-dev", workOrderId, now.AddMinutes(5)), CancellationToken.None);
        var releasedEvent = new WorkOrderReleasedIntegrationEventConverter().Convert(
            dbContext.ChangeTracker.Entries<WorkOrder>()
                .Select(x => x.Entity)
                .Single()
                .GetDomainEvents()
                .OfType<WorkOrderReleasedDomainEvent>()
                .Single());
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var change = new ChangeOperationTaskStateCommandHandler(dbContext);
        await change.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-3112-RUSH-10", "start", now.AddMinutes(10)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        await change.Handle(
            new ChangeOperationTaskStateCommand("org-001", "env-dev", "OP-3112-RUSH-10", "complete", now.AddMinutes(20)),
            CancellationToken.None);
        var completedEvent = new OperationTaskCompletedIntegrationEventConverter().Convert(
            dbContext.ChangeTracker.Entries<OperationTask>()
                .Select(x => x.Entity)
                .Single(x => x.OperationTaskIdValue == "OP-3112-RUSH-10")
                .GetDomainEvents()
                .OfType<OperationTaskCompletedDomainEvent>()
                .Single());
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 同源断言：两个事件谈的是同一道工序，SKU 必须相等。
        // 这正是 Quality 的 PeriodicInspectionOperation.Complete 拿来比对、不等就整封进死信的那一对。
        var releasedOperation = Assert.Single(releasedEvent.Payload.Operations);
        Assert.Equal(completedEvent.Payload.OperationTaskId, releasedOperation.OperationId);
        Assert.Equal(releasedEvent.Payload.SkuCode, completedEvent.Payload.SkuCode);
        Assert.Equal("SKU-FG-3112-RUSH", completedEvent.Payload.SkuCode);
    }

    private sealed class NoRequirementsProvider : IMesMaterialRequirementSnapshotProvider
    {
        public static readonly NoRequirementsProvider Instance = new();

        public Task<MesMaterialRequirementSnapshotResult> GetSnapshotAsync(
            MesMaterialRequirementSnapshotRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(MesMaterialRequirementSnapshotResult.NoRequirements("test:no-requirements"));
    }
}
