using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ScheduleAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.IndustrialTelemetry;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class TelemetryProductionReportAutomationTests
{
    [Fact]
    public async Task Production_count_delta_for_running_mapped_operation_records_telemetry_report_and_advances_progress()
    {
        await using var dbContext = CreateDbContext(nameof(Production_count_delta_for_running_mapped_operation_records_telemetry_report_and_advances_progress));
        SeedRunningOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var sender = new ProductionReportSender(dbContext);
        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            sender);

        await handler.HandleAsync(CreateEvent(reportingMode: "posted", hasActiveAlarm: false), CancellationToken.None);

        var report = await dbContext.ProductionReports.SingleAsync();
        var workOrder = await dbContext.WorkOrders.SingleAsync();
        Assert.Equal("telemetry", report.Source);
        Assert.Equal(3m, report.GoodQuantity);
        Assert.Equal(3m, workOrder.CompletedQuantity);

        await handler.HandleAsync(CreateEvent(reportingMode: "posted", hasActiveAlarm: false), CancellationToken.None);
        Assert.Equal(1, await dbContext.ProductionReports.CountAsync());

        await new ReverseProductionReportCommandHandler(dbContext, sender.CodingService).Handle(
            new ReverseProductionReportCommand(
                "org-001",
                "env-dev",
                report.ReportNo,
                "telemetry counter correction",
                DateTimeOffset.Parse("2026-07-11T08:02:00Z"),
                "telemetry-reversal-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, await dbContext.ProductionReports.CountAsync());
        Assert.Equal(0m, (await dbContext.WorkOrders.SingleAsync()).CompletedQuantity);
    }

    [Fact]
    public async Task Production_count_delta_during_active_alarm_creates_pending_confirmation_without_report()
    {
        var databaseName = nameof(Production_count_delta_during_active_alarm_creates_pending_confirmation_without_report);
        await using var dbContext = CreateDbContext(databaseName);
        SeedRunningOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            new ProductionReportSender(dbContext));

        await handler.HandleAsync(CreateEvent(reportingMode: "posted", hasActiveAlarm: true), CancellationToken.None);
        await using var verificationDbContext = CreateDbContext(databaseName);
        var pending = await verificationDbContext.TelemetryProductionReportCandidates.SingleAsync();
        Assert.Equal("pending-confirmation", pending.Status);
        Assert.Equal("active-alarm", pending.SuspensionReason);
        Assert.Equal(0, await verificationDbContext.ProductionReports.CountAsync());
    }

    [Fact]
    public async Task Production_count_delta_without_current_work_order_creates_pending_confirmation()
    {
        var databaseName = nameof(Production_count_delta_without_current_work_order_creates_pending_confirmation);
        await using var dbContext = CreateDbContext(databaseName);
        dbContext.DeviceAssetWorkCenterMappings.Add(
            DeviceAssetWorkCenterMapping.Create("org-001", "env-dev", "DEV-PACK-01", "WC-PACK-01"));
        await dbContext.SaveChangesAsync();
        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            new ProductionReportSender(dbContext));

        await handler.HandleAsync(CreateEvent(reportingMode: "posted", hasActiveAlarm: false), CancellationToken.None);

        await using var verificationDbContext = CreateDbContext(databaseName);
        var pending = await verificationDbContext.TelemetryProductionReportCandidates.SingleAsync();
        Assert.Equal("pending-confirmation", pending.Status);
        Assert.Equal("no-current-work-order", pending.SuspensionReason);
        Assert.Equal("WC-PACK-01", pending.WorkCenterId);
        Assert.Null(pending.WorkOrderId);
        Assert.Empty(await verificationDbContext.ProductionReports.ToArrayAsync());
    }

    [Fact]
    public async Task Production_count_delta_in_draft_mode_creates_draft_without_advancing_progress()
    {
        var databaseName = nameof(Production_count_delta_in_draft_mode_creates_draft_without_advancing_progress);
        await using var dbContext = CreateDbContext(databaseName);
        SeedRunningOperation(dbContext);
        await dbContext.SaveChangesAsync();
        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            new ProductionReportSender(dbContext));

        await handler.HandleAsync(CreateEvent(reportingMode: "draft", hasActiveAlarm: false), CancellationToken.None);
        await using var verificationDbContext = CreateDbContext(databaseName);
        var draft = await verificationDbContext.TelemetryProductionReportCandidates.SingleAsync();
        Assert.Equal("draft", draft.Status);
        Assert.Equal("WO-001", draft.WorkOrderId);
        Assert.Equal("OP-10", draft.OperationTaskId);
        Assert.Equal(0, await verificationDbContext.ProductionReports.CountAsync());
        Assert.Equal(0m, (await verificationDbContext.WorkOrders.SingleAsync()).CompletedQuantity);
    }

    [Fact]
    public async Task Invalid_production_count_payload_is_dead_lettered_without_recording_inbox_or_candidate()
    {
        var databaseName = nameof(Invalid_production_count_payload_is_dead_lettered_without_recording_inbox_or_candidate);
        await using var dbContext = CreateDbContext(databaseName);
        var deadLetterStore = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new TelemetryProductionCountDeltaIntegrationEventHandlerForAutomateProductionReport(
            dbContext,
            deadLetterStore,
            new ProductionReportSender(dbContext));

        await handler.HandleAsync(CreateEvent(reportingMode: "posted", hasActiveAlarm: false, deltaQuantity: 0m), CancellationToken.None);

        await using var verificationDbContext = CreateDbContext(databaseName);
        Assert.Empty(await verificationDbContext.ProcessedIntegrationEvents.ToArrayAsync());
        Assert.Empty(await verificationDbContext.TelemetryProductionReportCandidates.ToArrayAsync());
        var deadLetters = await deadLetterStore.ListAsync(null, null, CancellationToken.None);
        var deadLetter = Assert.Single(deadLetters);
        Assert.Equal("non-positive-count-delta", deadLetter.FailureCode);
    }

    private static TelemetryProductionCountDeltaIntegrationEvent CreateEvent(
        string reportingMode,
        bool hasActiveAlarm,
        decimal deltaQuantity = 3m) => new(
        "evt-production-count-001",
        IndustrialTelemetryIntegrationEventTypes.ProductionCountDeltaRecorded,
        IndustrialTelemetryIntegrationEventVersions.V1,
        DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
        IndustrialTelemetryIntegrationEventSources.IndustrialTelemetry,
        "industrialTelemetry:production-count:org-001:env-dev:DEV-PACK-01:parts_count:seq-002",
        "summary-001",
        "org-001",
        "env-dev",
        "system:industrial-telemetry",
        "industrialTelemetry:production-count:org-001:env-dev:DEV-PACK-01:parts_count:opcua:opcua-cell-01:seq-002",
        new TelemetryProductionCountDeltaPayload(
            "DEV-PACK-01",
            "parts_count",
            reportingMode,
            deltaQuantity,
            DateTimeOffset.Parse("2026-07-11T08:00:00Z"),
            DateTimeOffset.Parse("2026-07-11T08:01:00Z"),
            "seq-002",
            hasActiveAlarm));

    private static void SeedRunningOperation(ApplicationDbContext dbContext)
    {
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-001", "SKU-FG", "PV-001", 10m, 1, DateTimeOffset.Parse("2026-07-11T07:00:00Z"), "PCS");
        workOrder.MarkReleased();
        workOrder.Start(DateTimeOffset.Parse("2026-07-11T07:00:00Z"));
        var operation = OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "OP-10",
            OperationTaskLifecycleStatus.InProgress,
            10,
            "WC-PACK-01",
            [],
            DateTimeOffset.Parse("2026-07-11T07:00:00Z"),
            TimeSpan.FromHours(1),
            DateTimeOffset.Parse("2026-07-11T07:00:00Z"),
            null);
        operation.Assign(null, "DEV-PACK-01", null, DateTimeOffset.Parse("2026-07-11T07:00:00Z"));
        dbContext.WorkOrders.Add(workOrder);
        dbContext.OperationTasks.Add(operation);
        dbContext.DeviceAssetWorkCenterMappings.Add(DeviceAssetWorkCenterMapping.Create("org-001", "env-dev", "DEV-PACK-01", "WC-PACK-01"));
    }

    private static ApplicationDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class ProductionReportSender(ApplicationDbContext dbContext) : ISender
    {
        public MesCodingService CodingService { get; } = new();

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is RecordProductionReportCommand command)
            {
                var response = await new RecordProductionReportCommandHandler(dbContext, CodingService).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)response;
            }

            throw new NotSupportedException($"Unsupported request: {request.GetType().Name}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
