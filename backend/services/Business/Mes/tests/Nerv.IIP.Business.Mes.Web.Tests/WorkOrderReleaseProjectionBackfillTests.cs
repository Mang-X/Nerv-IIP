using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Mes;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Mes.Web.Tests;

/// <summary>
/// 存量在制工单发布投影回填（#3000）的选取判据与补投载荷。
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class WorkOrderReleaseProjectionBackfillTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    [Fact]
    public async Task Only_in_flight_work_orders_with_unfinished_operations_are_backfilled()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-RELEASED", Released);
        AddWorkOrder(dbContext, "WO-STARTED", Started);
        AddWorkOrder(dbContext, "WO-HOLD", workOrder => { Started(workOrder); workOrder.Hold("待料"); });
        AddWorkOrder(dbContext, "WO-CREATED", static _ => { });
        AddWorkOrder(dbContext, "WO-COMPLETED", Completed);
        AddWorkOrder(dbContext, "WO-CLOSED", workOrder => { Completed(workOrder); workOrder.Close(Now); });
        AddWorkOrder(dbContext, "WO-CANCELLED", static workOrder => workOrder.Cancel("客户取消", Now));
        AddWorkOrder(dbContext, "WO-SPLIT", workOrder => { Released(workOrder); workOrder.MarkSplit(); });
        AddWorkOrder(dbContext, "WO-MERGED", workOrder => { Released(workOrder); workOrder.MarkMerged(); });
        // 在制工单但全部工序已终结：门禁不会再拦它的报工，因此不补投。
        AddWorkOrder(
            dbContext,
            "WO-ALL-FINISHED",
            Started,
            [
                (OperationTaskLifecycleStatus.Completed, 10),
                (OperationTaskLifecycleStatus.Cancelled, 20),
            ]);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var report = await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        Assert.Equal(4, report.WorkOrdersScanned);
        Assert.Equal(3, report.WorkOrdersPublished);
        Assert.Equal(
            new[] { "WO-HOLD", "WO-RELEASED", "WO-STARTED" },
            publisher.Published.Select(x => x.Payload.WorkOrderId).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Backfilled_payload_carries_only_unfinished_operations()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(
            dbContext,
            "WO-MIXED",
            Started,
            [
                (OperationTaskLifecycleStatus.Completed, 10),
                (OperationTaskLifecycleStatus.InProgress, 20),
                (OperationTaskLifecycleStatus.Paused, 30),
                (OperationTaskLifecycleStatus.ScheduleInvalidated, 40),
                (OperationTaskLifecycleStatus.Cancelled, 50),
                (OperationTaskLifecycleStatus.Queued, 60),
            ]);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var report = await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(4, report.OperationsPublished);
        Assert.Equal(
            new[] { 20, 30, 40, 60 },
            published.Payload.Operations.Select(x => x.OperationSequence).ToArray());
        Assert.Equal(MesIntegrationEventTypes.WorkOrderReleaseProjectionBackfilled, published.EventType);
        Assert.Equal(MesIntegrationEventVersions.V1, published.EventVersion);
        Assert.Equal("SKU-FG-1000", published.Payload.SkuCode);
        Assert.Equal("WC-020", published.Payload.Operations.First().WorkCenterId);
    }

    [Fact]
    public async Task Release_time_falls_back_to_the_earliest_backdated_report_of_the_work_order()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-BACKDATED", Started);
        // 报工时刻由调用方填，可以早于工序建单时刻。Quality 的聚合拒绝「报工早于发布」的组合，
        // 所以补投的发布时刻必须压到这条最早报工之前，否则整张工单进死信、回填对它无效。
        var backdated = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-OLD", "WO-BACKDATED", "OP-WO-BACKDATED-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: backdated));
        dbContext.ProductionReports.Add(ProductionReport.Record(
            "org-001", "env-dev", "RPT-NEW", "WO-BACKDATED", "OP-WO-BACKDATED-10",
            goodQuantity: 3m, scrapQuantity: 0m, completesOperation: false, reportedAtUtc: Now));
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(backdated, published.Payload.ReleasedAtUtc);
    }

    [Fact]
    public async Task Release_time_falls_back_to_the_earliest_operation_creation_when_nothing_was_reported()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-NO-REPORT", Released);
        await dbContext.SaveChangesAsync();
        var earliestCreatedAtUtc = await dbContext.OperationTasks
            .AsNoTracking()
            .Where(x => x.WorkOrderId == "WO-NO-REPORT")
            .MinAsync(x => x.CreatedAtUtc);

        var publisher = new RecordingPublisher();
        await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        var published = Assert.Single(publisher.Published);
        Assert.Equal(earliestCreatedAtUtc, published.Payload.ReleasedAtUtc);
    }

    [Fact]
    public async Task Rerunning_the_backfill_republishes_the_same_business_identity()
    {
        await using var dbContext = CreateDbContext();
        AddWorkOrder(dbContext, "WO-RERUN", Started);
        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var handler = CreateHandler(dbContext, publisher);
        await handler.Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);
        await handler.Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        Assert.Equal(2, publisher.Published.Count);
        Assert.Equal(
            "mes:work-order-release-projection-backfill:org-001:env-dev:WO-RERUN",
            publisher.Published[0].IdempotencyKey);
        Assert.Equal(publisher.Published[0].IdempotencyKey, publisher.Published[1].IdempotencyKey);
        Assert.Equal(publisher.Published[0].Payload.ReleasedAtUtc, publisher.Published[1].Payload.ReleasedAtUtc);
        Assert.Equal(
            publisher.Published[0].Payload.Operations,
            publisher.Published[1].Payload.Operations);
        Assert.NotEqual(publisher.Published[0].EventId, publisher.Published[1].EventId);
    }

    [Fact]
    public async Task Backfill_pages_beyond_a_single_database_round_trip()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 0; index < 205; index++)
        {
            AddWorkOrder(dbContext, $"WO-{index:D4}", Released);
        }

        await dbContext.SaveChangesAsync();

        var publisher = new RecordingPublisher();
        var report = await CreateHandler(dbContext, publisher).Handle(new BackfillWorkOrderReleaseProjectionCommand(), CancellationToken.None);

        Assert.Equal(205, report.WorkOrdersPublished);
        Assert.Equal(205, publisher.Published.Select(x => x.Payload.WorkOrderId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Backfill_endpoint_is_reachable_only_with_an_internal_service_token()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("InternalService:BearerToken", "test-internal-service-token");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ISender>();
                    services.AddSingleton<ISender>(new BackfillReportSender());
                });
            });
        using var client = factory.CreateClient();
        await CapTestHost.WaitForCapBootstrapAsync(factory.Services);

        using var anonymous = await client.PostAsync(
            "/internal/business-mes/v1/work-order-release-projection-backfill",
            content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", "test-internal-service-token");
        using var response = await client.PostAsync(
            "/internal/business-mes/v1/work-order-release-projection-backfill",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(7, body.RootElement.GetProperty("workOrdersScanned").GetInt32());
        Assert.Equal(5, body.RootElement.GetProperty("workOrdersPublished").GetInt32());
        Assert.Equal(9, body.RootElement.GetProperty("operationsPublished").GetInt32());
    }

    private static BackfillWorkOrderReleaseProjectionCommandHandler CreateHandler(
        ApplicationDbContext dbContext,
        RecordingPublisher publisher) =>
        new(dbContext, publisher, new FakeTimeProvider(Now));

    private static void Released(WorkOrder workOrder) => workOrder.MarkReleased();

    private static void Started(WorkOrder workOrder)
    {
        workOrder.MarkReleased();
        workOrder.Start(Now);
    }

    private static void Completed(WorkOrder workOrder)
    {
        Started(workOrder);
        workOrder.RecordProductionProgress(goodQuantity: 1000m, scrapQuantity: 0m, reportedAtUtc: Now);
    }

    private static void AddWorkOrder(
        ApplicationDbContext dbContext,
        string workOrderId,
        Action<WorkOrder> advanceToStatus,
        IReadOnlyCollection<(OperationTaskLifecycleStatus Status, int Sequence)>? operations = null)
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            workOrderId,
            "SKU-FG-1000",
            null,
            quantity: 1000m,
            priority: 1,
            dueUtc: Now.AddDays(3));
        advanceToStatus(workOrder);
        dbContext.WorkOrders.Add(workOrder);
        foreach (var (operationStatus, sequence) in operations ?? [(OperationTaskLifecycleStatus.Queued, 10)])
        {
            dbContext.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                workOrderId,
                $"OP-{workOrderId}-{sequence}",
                operationStatus,
                sequence,
                $"WC-{sequence:D3}",
                [],
                Now,
                TimeSpan.FromHours(1),
                null,
                null,
                "SKU-FG-1000",
                "EA",
                1000m));
        }
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-release-projection-backfill-{Guid.CreateVersion7():N}")
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private sealed class BackfillReportSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = Assert.IsType<BackfillWorkOrderReleaseProjectionCommand>(request);
            return Task.FromResult((TResponse)(object)new WorkOrderReleaseProjectionBackfillReport(7, 5, 9));
        }

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

    private sealed class RecordingPublisher : IWorkOrderReleaseProjectionBackfillPublisher
    {
        public List<WorkOrderReleaseProjectionBackfilledIntegrationEvent> Published { get; } = [];

        public Task PublishAsync(WorkOrderReleaseProjectionBackfilledIntegrationEvent integrationEvent)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
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
