using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Repository;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class WorkOrderCostCapitalizedPersistenceTests
{
    [Fact]
    public async Task Cap_handler_persists_unit_cost_and_dispatches_inventory_request()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-cost-cap-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var completedAtUtc = DateTimeOffset.Parse("2026-07-23T07:15:28Z");

        await using (var seed = new ApplicationDbContext(options, new RecordingMediator()))
        {
            seed.FinishedGoodsReceiptRequests.Add(FinishedGoodsReceiptRequest.Create(
                "org-001", "env-dev", "FGR-001", "WO-001", "FG-001", 10m, "ea",
                completedAtUtc, "LOT-001"));
            await seed.SaveChangesAsync();
        }

        var mediator = new RecordingMediator();
        RecordingUnitOfWork? unitOfWork = null;
        await using (var handling = new ApplicationDbContext(options, mediator))
        {
            unitOfWork = new RecordingUnitOfWork(handling);
            var integrationEvent = new WorkOrderCostCapitalizedIntegrationEvent(
                "evt-cost", ErpIntegrationEventTypes.WorkOrderCostCapitalized,
                ErpIntegrationEventVersions.V1, completedAtUtc,
                ErpIntegrationEventSources.BusinessErp, "WO-001", "WO-001",
                "org-001", "env-dev", "system:erp", "cost-001",
                new WorkOrderCostCapitalizedPayload(
                    "WO-001", "FG-001", 10m, 0m, 250m, 250m, 25m, completedAtUtc));

            await new WorkOrderCostCapitalizedIntegrationEventHandler(
                    handling, new InMemoryIntegrationEventDeadLetterStore(), unitOfWork)
                .HandleAsync(integrationEvent, CancellationToken.None);
        }

        Assert.NotNull(unitOfWork);
        Assert.Equal(1, unitOfWork.SaveEntitiesCallCount);
        Assert.Contains(mediator.Published, notification => notification is FinishedGoodsReceiptRequestedDomainEvent);
        await using var verification = new ApplicationDbContext(options, new RecordingMediator());
        Assert.Equal(25m, (await verification.FinishedGoodsReceiptRequests.SingleAsync()).UnitCost);
        Assert.Contains(await verification.ProcessedIntegrationEvents.ToListAsync(),
            item => item.ConsumerName == WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName);
    }

    private sealed class RecordingMediator : IMediator
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingUnitOfWork(IUnitOfWork inner) : IUnitOfWork
    {
        public int SaveEntitiesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);

        public Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            SaveEntitiesCallCount++;
            return inner.SaveEntitiesAsync(cancellationToken);
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
