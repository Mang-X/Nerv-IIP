using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Scheduling.Web.Tests;

public sealed class OperationExecutionProjectionConsumerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lifecycle_consumers_preserve_newer_state_and_fill_actual_start()
    {
        await using var db = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var mutationLock = new NoopOperationExecutionProjectionMutationLock();

        await new MesOperationTaskPausedIntegrationEventHandlerForProjectExecution(db, deadLetters, mutationLock)
            .HandleAsync(Lifecycle<MesOperationTaskPausedIntegrationEvent>("evt-pause", BaseTime.AddMinutes(20)), CancellationToken.None);
        await new MesOperationTaskStartedIntegrationEventHandlerForProjectExecution(db, deadLetters, mutationLock)
            .HandleAsync(Lifecycle<MesOperationTaskStartedIntegrationEvent>("evt-start", BaseTime.AddMinutes(10)), CancellationToken.None);

        var projection = await db.OperationExecutionProjections.SingleAsync();
        Assert.Equal(BaseTime.AddMinutes(10), projection.ActualStartedAtUtc);
        Assert.True(projection.IsPaused);
        Assert.Equal("evt-pause", projection.LifecycleEventId);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Production_report_consumer_is_idempotent_and_accumulates_only_net_good_quantity()
    {
        await using var db = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ProductionReportRecordedIntegrationEventHandlerForProjectExecution(
            db, deadLetters, new NoopOperationExecutionProjectionMutationLock());
        var report = ProductionReport("evt-report", "RPT-001", 10m, 2m, 3m, BaseTime);
        var reversal = ProductionReport("evt-reversal", "RPT-002", -4m, -1m, -1m, BaseTime.AddMinutes(10), "RPT-001");

        await handler.HandleAsync(report, CancellationToken.None);
        await handler.HandleAsync(report, CancellationToken.None);
        await handler.HandleAsync(reversal, CancellationToken.None);

        var projection = await db.OperationExecutionProjections.SingleAsync();
        Assert.Equal(6m, projection.CompletedQuantity);
        Assert.Equal("evt-reversal", projection.LatestSourceEventId);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Downtime_and_quality_consumers_skip_facts_without_operation_identity()
    {
        await using var db = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var mutationLock = new NoopOperationExecutionProjectionMutationLock();
        var downtimeHandler = new MesDowntimeStartedIntegrationEventHandlerForProjectExecution(db, deadLetters, mutationLock);
        var qualityHandler = new QualityInspectionResultIntegrationEventHandlerForProjectExecution(db, deadLetters, mutationLock);

        await downtimeHandler.HandleAsync(DowntimeStarted("evt-downtime-unscoped", operationId: null), CancellationToken.None);
        await qualityHandler.HandleAsync(QualityResult("evt-quality-unscoped", QualityIntegrationEventTypes.InspectionRejected, operationId: null), CancellationToken.None);
        await downtimeHandler.HandleAsync(DowntimeStarted("evt-downtime", "op-010"), CancellationToken.None);
        await qualityHandler.HandleAsync(QualityResult("evt-quality", QualityIntegrationEventTypes.InspectionRejected, "op-010"), CancellationToken.None);

        var projection = await db.OperationExecutionProjections.SingleAsync();
        Assert.True(projection.IsDowntimeBlocked);
        Assert.True(projection.IsQualityBlocked);
        Assert.Equal("op-010", projection.OperationId);
        Assert.Equal(2, await db.ProcessedIntegrationEvents.CountAsync());
    }

    [Fact]
    public async Task Projection_identity_is_scoped_by_organization_environment_work_order_and_operation()
    {
        await using var db = CreateDbContext();
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var mutationLock = new NoopOperationExecutionProjectionMutationLock();
        var handler = new ProductionReportRecordedIntegrationEventHandlerForProjectExecution(db, deadLetters, mutationLock);

        await handler.HandleAsync(ProductionReport("evt-a", "RPT-A", 1m, 0m, 0m, BaseTime), CancellationToken.None);
        await handler.HandleAsync(ProductionReport("evt-b", "RPT-B", 2m, 0m, 0m, BaseTime, organizationId: "org-002"), CancellationToken.None);
        await handler.HandleAsync(ProductionReport("evt-c", "RPT-C", 3m, 0m, 0m, BaseTime, operationId: "op-020"), CancellationToken.None);

        var projections = await db.OperationExecutionProjections.OrderBy(x => x.OrganizationId).ThenBy(x => x.OperationId).ToArrayAsync();
        Assert.Equal(3, projections.Length);
        Assert.Equal(
            [("org-001", "op-010", 1m), ("org-001", "op-020", 3m), ("org-002", "op-010", 2m)],
            projections.Select(x => (x.OrganizationId, x.OperationId, x.CompletedQuantity)));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"operation-execution-projection-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static T Lifecycle<T>(string eventId, DateTimeOffset changedAtUtc)
        where T : class
    {
        var payload = new OperationTaskLifecyclePayload("wo-001", "op-010", 10, "wc-001", changedAtUtc);
        object integrationEvent = typeof(T) == typeof(MesOperationTaskStartedIntegrationEvent)
            ? new MesOperationTaskStartedIntegrationEvent(eventId, MesIntegrationEventTypes.OperationTaskStarted, 1, changedAtUtc,
                MesIntegrationEventSources.BusinessMes, eventId, eventId, "org-001", "env-dev", "operator", eventId, payload)
            : new MesOperationTaskPausedIntegrationEvent(eventId, MesIntegrationEventTypes.OperationTaskPaused, 1, changedAtUtc,
                MesIntegrationEventSources.BusinessMes, eventId, eventId, "org-001", "env-dev", "operator", eventId, payload);
        return (T)integrationEvent;
    }

    private static ProductionReportRecordedIntegrationEvent ProductionReport(
        string eventId,
        string reportNo,
        decimal goodQuantity,
        decimal scrapQuantity,
        decimal reworkQuantity,
        DateTimeOffset reportedAtUtc,
        string? reversedReportNo = null,
        string organizationId = "org-001",
        string operationId = "op-010") =>
        new(
            eventId,
            MesIntegrationEventTypes.ProductionReportRecorded,
            1,
            reportedAtUtc,
            MesIntegrationEventSources.BusinessMes,
            eventId,
            eventId,
            organizationId,
            "env-dev",
            "operator",
            $"production-report:{reportNo}",
            new ProductionReportRecordedPayload(
                reportNo,
                "wo-001",
                operationId,
                "wc-001",
                null,
                goodQuantity,
                scrapQuantity,
                reworkQuantity,
                "PCS",
                null,
                reportedAtUtc,
                reversedReportNo is not null,
                reversedReportNo));

    private static MesDowntimeStartedIntegrationEvent DowntimeStarted(string eventId, string? operationId) =>
        new(
            eventId,
            MesIntegrationEventTypes.DowntimeStarted,
            1,
            BaseTime,
            MesIntegrationEventSources.BusinessMes,
            eventId,
            eventId,
            "org-001",
            "env-dev",
            "operator",
            eventId,
            new DowntimeStartedPayload("DT-001", "wo-001", operationId, "wc-001", null, "failure", BaseTime, null));

    private static InspectionResultIntegrationEvent QualityResult(string eventId, string eventType, string? operationId) =>
        new(
            eventId,
            eventType,
            1,
            BaseTime.AddMinutes(5),
            QualityIntegrationEventSources.BusinessQuality,
            eventId,
            eventId,
            "org-001",
            "env-dev",
            "inspector",
            eventId,
            new InspectionResultPayload(
                "inspection-001",
                null,
                QualityInspectionSourceTypes.Operation,
                QualityInspectionSourceServices.MesOperation,
                "wo-001",
                "SKU-001",
                1m,
                "Rejected",
                null,
                [],
                BaseTime.AddMinutes(5),
                WorkOrderId: "wo-001",
                OperationTaskId: operationId));

    private sealed class NoopOperationExecutionProjectionMutationLock : IOperationExecutionProjectionMutationLock
    {
        public Task AcquireAsync(
            string organizationId,
            string environmentId,
            string workOrderId,
            string operationId,
            CancellationToken cancellationToken) => Task.CompletedTask;
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
}
