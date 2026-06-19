using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockMovements;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.MaterialSupplyAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Contracts.Inventory;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using InventoryDbContext = Nerv.IIP.Business.Inventory.Infrastructure.ApplicationDbContext;
using MesDbContext = Nerv.IIP.Business.Mes.Infrastructure.ApplicationDbContext;

namespace Nerv.IIP.Business.Acceptance.Tests;

public sealed class MesInventoryLineSideTransferAcceptanceTests
{
    [Fact]
    public async Task Mes_issue_receipt_and_consumption_posts_continuous_inventory_line_side_account()
    {
        await using var mesDb = CreateMesContext();
        await using var inventoryDb = CreateInventoryContext();
        SeedMesWorkOrder(mesDb);
        await mesDb.SaveChangesAsync();
        var inventoryPublisher = new RecordingIntegrationEventPublisher();
        var inventoryHandler = CreateInventoryHandler(inventoryDb, inventoryPublisher);
        var issuedAtUtc = DateTimeOffset.Parse("2026-06-18T08:00:00Z");
        var receivedAtUtc = issuedAtUtc.AddMinutes(20);
        var reportedAtUtc = issuedAtUtc.AddHours(1);
        await inventoryHandler.HandleAsync(CreateWarehouseSeedEvent(issuedAtUtc.AddMinutes(-10)), CancellationToken.None);

        var issueResult = await new CreateMaterialIssueRequestCommandHandler(mesDb).Handle(
            new CreateMaterialIssueRequestCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                "MAT-OIL",
                "L",
                5m,
                issuedAtUtc,
                "issue-446"),
            CancellationToken.None);
        var issueRequest = mesDb.MaterialIssueRequests.Local.Single(x => x.RequestNo == issueResult.ReferenceId);
        await mesDb.SaveChangesAsync();

        await new ConfirmLineSideMaterialReceiptCommandHandler(mesDb).Handle(
            new ConfirmLineSideMaterialReceiptCommand(
                "org-001",
                "env-dev",
                issueResult.ReferenceId,
                receivedAtUtc,
                5m,
                "LOT-OIL-A"),
            CancellationToken.None);
        var transferEvents = issueRequest.GetDomainEvents().ToArray();
        var issueEvent = new MaterialIssueRequestedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialIssueRequestedDomainEvent>(transferEvents[0]));
        var receiptEvent = new MaterialLineSideReceiptConfirmedIntegrationEventConverter().Convert(
            Assert.IsType<MaterialLineSideReceiptConfirmedDomainEvent>(transferEvents[1]));
        issueRequest.ClearDomainEvents();
        await mesDb.SaveChangesAsync();

        var reportResult = await new RecordProductionReportCommandHandler(mesDb).Handle(
            new RecordProductionReportCommand(
                "org-001",
                "env-dev",
                "WO-446",
                "OP-10",
                1m,
                0m,
                false,
                reportedAtUtc,
                "report-446",
                [
                    new ConsumedMaterialLotInput("MAT-OIL", "LOT-OIL-A", 2m, issueResult.ReferenceId),
                ]),
            CancellationToken.None);
        var consumption = mesDb.ProductionReportMaterialConsumptions.Local.Single(x => x.ReportNo == reportResult.ReportNo);
        var consumptionEvent = new ProductionMaterialConsumedIntegrationEventConverter().Convert(
            Assert.IsType<ProductionMaterialConsumedDomainEvent>(consumption.GetDomainEvents().Single()));

        foreach (var movementEvent in new[] { issueEvent, receiptEvent, consumptionEvent })
        {
            await inventoryHandler.HandleAsync(movementEvent, CancellationToken.None);
        }

        Assert.Empty(inventoryPublisher.Published);
        Assert.Equal(0m, inventoryDb.StockLedgers.Single(x =>
            x.SiteCode == "warehouse" &&
            x.LocationCode == "line-side" &&
            x.SkuCode == "MAT-OIL" &&
            x.LotNo == "LOT-OIL-A").OnHandQuantity);
        Assert.Equal(3m, inventoryDb.StockLedgers.Single(x =>
            x.SiteCode == "production" &&
            x.LocationCode == "line-side" &&
            x.SkuCode == "MAT-OIL" &&
            x.LotNo == "LOT-OIL-A").OnHandQuantity);
        Assert.Contains(inventoryDb.StockMovements, x =>
            x.SiteCode == "warehouse" &&
            x.LocationCode == "line-side" &&
            x.Quantity == -5m);
        Assert.Equal(4, inventoryDb.StockMovements.Count());
    }

    private static InventoryMovementRequestedIntegrationEvent CreateWarehouseSeedEvent(DateTimeOffset occurredAtUtc)
    {
        return new InventoryMovementRequestedIntegrationEvent(
            "evt-mes-446-warehouse-seed",
            InventoryIntegrationEventTypes.InventoryMovementRequested,
            InventoryIntegrationEventVersions.V1,
            occurredAtUtc,
            InventoryIntegrationEventSources.BusinessWms,
            "seed-446",
            "seed-446",
            "org-001",
            "env-dev",
            "system:test",
            "seed:mes-446:warehouse-line-side",
            new InventoryMovementRequestedPayload(
                "inbound",
                InventoryIntegrationEventSources.BusinessWms,
                "SEED-446",
                "SEED-446-LINE",
                "seed:mes-446:warehouse-line-side",
                "MAT-OIL",
                "L",
                "warehouse",
                "line-side",
                "LOT-OIL-A",
                null,
                "Unrestricted",
                "production",
                null,
                5m,
                occurredAtUtc));
    }

    private static InventoryMovementRequestedIntegrationEventHandlerForPostingMovement CreateInventoryHandler(
        InventoryDbContext inventoryDb,
        RecordingIntegrationEventPublisher publisher)
    {
        return new InventoryMovementRequestedIntegrationEventHandlerForPostingMovement(
            NullLogger<InventoryMovementRequestedIntegrationEventHandlerForPostingMovement>.Instance,
            new InventoryCommandExecutingSender(inventoryDb),
            new InMemoryIntegrationEventDeadLetterStore(),
            publisher);
    }

    private static void SeedMesWorkOrder(MesDbContext mesDb)
    {
        var now = DateTimeOffset.Parse("2026-06-18T07:00:00Z");
        var workOrder = WorkOrder.Create("org-001", "env-dev", "WO-446", "SKU-FG", "PV-001", 10m, 10, now.AddHours(8));
        workOrder.MarkReleased();
        workOrder.Start(now);
        mesDb.WorkOrders.Add(workOrder);
        mesDb.OperationTasks.Add(OperationTask.Create(
            "org-001",
            "env-dev",
            "WO-446",
            "OP-10",
            OperationTaskLifecycleStatus.Queued,
            10,
            "WC-10",
            [],
            now,
            TimeSpan.FromMinutes(30),
            null,
            null));
    }

    private static MesDbContext CreateMesContext()
    {
        var options = new DbContextOptionsBuilder<MesDbContext>()
            .UseInMemoryDatabase($"mes-inventory-line-side-{Guid.NewGuid():N}")
            .Options;
        return new MesDbContext(options, new NoopMediator());
    }

    private static InventoryDbContext CreateInventoryContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"mes-inventory-line-side-{Guid.NewGuid():N}")
            .Options;
        return new InventoryDbContext(options, new NoopMediator());
    }

    private sealed class InventoryCommandExecutingSender(InventoryDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PostStockMovementCommand command)
            {
                var result = await new PostStockMovementCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("This test sender only supports command requests with responses.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender only supports typed command requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This test sender does not support streams.");
        }
    }

    private sealed class RecordingIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = [];

        Task IIntegrationEventPublisher.PublishAsync<TIntegrationEvent>(TIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            Published.Add(integrationEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Test mediator cannot stream requests.");
        }
    }
}
