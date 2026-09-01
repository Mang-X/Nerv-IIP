using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionPlanAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Quality.Web.Tests;

/// <summary>
/// 存量在制工单的发布投影回填（#3000）。这批工序读首件确认恒回 <c>not-synchronized</c>、被 #2780 门禁持续拒，
/// 且不靠报工自愈；回填补上发布事实后该状态才消失。回填可重复执行是本票的核心不变量。
/// </summary>
public sealed class WorkOrderReleaseProjectionBackfillConsumerTests
{
    private static readonly DateTimeOffset ReleasedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    [Fact]
    public async Task Backfill_lifts_the_operation_out_of_not_synchronized()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(FirstArticlePlan());
        await dbContext.SaveChangesAsync();
        Assert.Equal(
            QualityFirstArticleConfirmationStatuses.NotSynchronized,
            (await ConfirmAsync(dbContext)).Status);

        await HandleBackfillAsync(dbContext, Backfill());

        var confirmation = await ConfirmAsync(dbContext);
        Assert.Equal(QualityFirstArticleConfirmationStatuses.NotOpened, confirmation.Status);
        var operation = await dbContext.PeriodicInspectionOperations.SingleAsync();
        Assert.Equal("SKU-FG-1000", operation.SkuCode);
        Assert.Equal("WC-MIX", operation.WorkCenterId);
        Assert.Equal(10, operation.OperationSequence);
        Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);
    }

    [Fact]
    public async Task Rerunning_the_backfill_changes_no_projection_row_and_opens_no_first_article_task()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(FirstArticlePlan());
        dbContext.InspectionPlans.Add(PeriodicPlan());
        await dbContext.SaveChangesAsync();

        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await HandleBackfillAsync(dbContext, Backfill(), deadLetters);
        var firstRun = await SnapshotAsync(dbContext);

        // 第二次补投带的是新 EventId、且发布时刻被重建得不一样——inbox 挡不住它，
        // 挡住它的是「已有发布事实的工序不覆盖」。
        await HandleBackfillAsync(
            dbContext,
            Backfill(eventId: "evt-backfill-second", releasedAtUtc: ReleasedAtUtc.AddHours(3)),
            deadLetters);
        var secondRun = await SnapshotAsync(dbContext);

        Assert.Equal(firstRun, secondRun);
        // 不跳过已有发布事实时，第二次补投会被判为冲突事实进死信：投影确实没变，但工单被当成坏数据。
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
        Assert.Empty(await dbContext.InspectionTasks
            .Where(x => x.SourceType == FirstArticleInspection.SourceType)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Backfill_never_overwrites_release_facts_that_already_arrived_from_the_live_event()
    {
        await using var dbContext = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        await new WorkOrderReleasedIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters).HandleAsync(LiveRelease(), CancellationToken.None);

        await HandleBackfillAsync(
            dbContext,
            Backfill(releasedAtUtc: ReleasedAtUtc.AddDays(-30)),
            deadLetters);

        var operation = await dbContext.PeriodicInspectionOperations.SingleAsync();
        Assert.Equal(ReleasedAtUtc.UtcDateTime, operation.ReleasedAtUtc);
        // 冲突的发布事实本该进死信；回填因为根本不碰已有行，所以既不覆盖也不产生死信。
        Assert.Empty(await deadLetters.ListAsync(null, null, CancellationToken.None));
    }

    [Fact]
    public async Task Backfill_reconciles_the_reports_that_arrived_before_the_release_facts()
    {
        await using var dbContext = CreateDbContext();
        dbContext.InspectionPlans.Add(PeriodicPlan());
        await dbContext.SaveChangesAsync();
        await new ProductionReportRecordedIntegrationEventHandlerForTrackPeriodicInspection(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            new InMemoryIntegrationEventDeadLetterStore()).HandleAsync(
                ProductionReport(),
                CancellationToken.None);

        await HandleBackfillAsync(dbContext, Backfill());

        var operation = await dbContext.PeriodicInspectionOperations
            .Include(x => x.RuntimeContexts)
            .SingleAsync();
        var runtimeContext = Assert.Single(operation.RuntimeContexts);
        Assert.Equal(250m, runtimeContext.QuantityHighWater);
        Assert.Equal("EA", runtimeContext.UomCode);
        Assert.Equal(2, await dbContext.InspectionTasks.CountAsync());
    }

    private static async Task HandleBackfillAsync(
        ApplicationDbContext dbContext,
        WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent,
        InMemoryIntegrationEventDeadLetterStore? deadLetters = null) =>
        await new WorkOrderReleaseProjectionBackfilledIntegrationEventHandlerForCreatePeriodicInspectionContexts(
            dbContext,
            new PeriodicInspectionOperationScopeCoordinator(dbContext),
            deadLetters ?? new InMemoryIntegrationEventDeadLetterStore())
            .HandleAsync(integrationEvent, CancellationToken.None);

    private static Task<FirstArticleConfirmationResponse> ConfirmAsync(ApplicationDbContext dbContext) =>
        new GetFirstArticleConfirmationQueryHandler(dbContext).Handle(
            new GetFirstArticleConfirmationQuery("org-001", "env-dev", "WO-001", "OP-10"),
            CancellationToken.None);

    private static async Task<string[]> SnapshotAsync(ApplicationDbContext dbContext) =>
        await dbContext.PeriodicInspectionOperations
            .AsNoTracking()
            .OrderBy(x => x.WorkOrderId)
            .ThenBy(x => x.OperationId)
            .Select(x => x.WorkOrderId + "|" + x.OperationId + "|" + x.SkuCode + "|" + x.OperationSequence
                + "|" + x.WorkCenterId + "|" + x.ReleasedAtUtc)
            .ToArrayAsync();

    private static WorkOrderReleaseProjectionBackfilledIntegrationEvent Backfill(
        string eventId = "evt-backfill-first",
        DateTimeOffset? releasedAtUtc = null) => new(
        eventId,
        MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-release-projection-backfill:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            releasedAtUtc ?? ReleasedAtUtc,
            [new ReleasedOperationPayload("OP-10", 10, "WC-MIX")]));

    private static WorkOrderReleasedIntegrationEvent LiveRelease() => new(
        "evt-release-WO-001",
        MesIntegrationEventTypes.WorkOrderReleased,
        MesIntegrationEventVersions.V1,
        ReleasedAtUtc,
        MesIntegrationEventSources.BusinessMes,
        "mes:work-order-released:org-001:env-dev:WO-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:work-order-released:org-001:env-dev:WO-001",
        new WorkOrderReleasedPayload(
            "WO-001",
            "SKU-FG-1000",
            1000m,
            ReleasedAtUtc,
            [new ReleasedOperationPayload("OP-10", 10, "WC-MIX")]));

    private static ProductionReportRecordedIntegrationEvent ProductionReport() => new(
        "evt-report-RPT-001",
        MesIntegrationEventTypes.ProductionReportRecorded,
        MesIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
        MesIntegrationEventSources.BusinessMes,
        "corr-report-RPT-001",
        "WO-001",
        "org-001",
        "env-dev",
        "system:mes",
        "mes:production-report-recorded:org-001:env-dev:RPT-001",
        new ProductionReportRecordedPayload(
            "RPT-001", "WO-001", "OP-10", "WC-MIX", null, 250m, 0m, 0m, "EA", null,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), false));

    private static InspectionPlan FirstArticlePlan()
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "PLAN-FA-1000", "first-article", "SKU-FG-1000", null, "WC-MIX", null, null);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static InspectionPlan PeriodicPlan()
    {
        var plan = InspectionPlan.Create(
            "org-001", "env-dev", "PLAN-PERIODIC-1000", "operation", "SKU-FG-1000", null, "WC-MIX", null, "mes-operation",
            quantityInterval: 100m);
        plan.AddCharacteristic("appearance", "Appearance", "visual", "major", required: true, "100%");
        plan.Activate();
        return plan;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"quality-release-projection-backfill-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
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

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
