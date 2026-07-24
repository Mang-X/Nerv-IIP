using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.FinishedGoodsReceiptRequestAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Repository;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class WorkOrderCostCapitalizedPersistenceTests
{
    [Fact]
    public void Cost_capitalization_handler_requires_a_transaction_unit_of_work()
    {
        var runtimeConstructor = Assert.Single(
            typeof(WorkOrderCostCapitalizedIntegrationEventHandler)
                .GetConstructors(),
            constructor => constructor.GetParameters()
                .Any(candidate => candidate.ParameterType == typeof(IMesWorkOrderCapitalizationScopeCoordinator)));
        var parameter = Assert.Single(
            runtimeConstructor.GetParameters(),
            candidate => candidate.ParameterType == typeof(ITransactionUnitOfWork));
        var scopeCoordinator = Assert.Single(
            runtimeConstructor.GetParameters(),
            candidate => candidate.ParameterType == typeof(IMesWorkOrderCapitalizationScopeCoordinator));

        Assert.False(parameter.HasDefaultValue);
        Assert.False(parameter.IsOptional);
        Assert.False(scopeCoordinator.HasDefaultValue);
        Assert.False(scopeCoordinator.IsOptional);
    }

    [Fact]
    public async Task Receipt_first_then_capitalization_persists_unit_cost_and_dispatches_inventory_request()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-cost-cap-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var completedAtUtc = DateTimeOffset.Parse("2026-07-23T07:15:28Z");

        await using (var seed = new ApplicationDbContext(options, new RecordingMediator()))
        {
            seed.WorkOrders.Add(CreateCompletedWorkOrder(completedAtUtc));
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
        Assert.Equal(0, unitOfWork.BeginTransactionCallCount);
        Assert.Equal(0, unitOfWork.CommitCallCount);
        Assert.Equal(0, unitOfWork.RollbackCallCount);
        Assert.Equal(0, unitOfWork.TransactionDisposeAsyncCallCount);
        Assert.Null(unitOfWork.CurrentTransaction);
        Assert.Contains(mediator.Published, notification => notification is FinishedGoodsReceiptRequestedDomainEvent);
        await using var verification = new ApplicationDbContext(options, new RecordingMediator());
        Assert.Equal(25m, (await verification.FinishedGoodsReceiptRequests.SingleAsync()).UnitCost);
        Assert.Equal(25m, (await verification.WorkOrders.SingleAsync()).CapitalizedUnitCost);
        Assert.Contains(await verification.ProcessedIntegrationEvents.ToListAsync(),
            item => item.ConsumerName == WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName);
    }

    [Fact]
    public async Task Capitalization_first_commits_inbox_and_later_receipt_dispatches_without_redelivery()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"mes-cost-cap-first-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var completedAtUtc = DateTimeOffset.Parse("2026-07-23T07:15:28Z");

        await using (var seed = new ApplicationDbContext(options, new RecordingMediator()))
        {
            seed.WorkOrders.Add(CreateCompletedWorkOrder(completedAtUtc));
            seed.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
                "org-001", "env-dev", "WO-001", "OP-10", "PRPT-001", "LOT-001", null, 10m, completedAtUtc));
            await seed.SaveChangesAsync();
        }

        var integrationEvent = CreateCapitalizationEvent(completedAtUtc);
        RecordingUnitOfWork? unitOfWork = null;
        await using (var handling = new ApplicationDbContext(options, new RecordingMediator()))
        {
            unitOfWork = new RecordingUnitOfWork(handling);
            var handler = new WorkOrderCostCapitalizedIntegrationEventHandler(
                handling,
                new InMemoryIntegrationEventDeadLetterStore(),
                unitOfWork);

            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }

        Assert.NotNull(unitOfWork);
        Assert.Equal(1, unitOfWork.SaveEntitiesCallCount);
        Assert.Equal(0, unitOfWork.CommitCallCount);

        await using (var earlyVerification = new ApplicationDbContext(options, new RecordingMediator()))
        {
            Assert.Equal(25m, (await earlyVerification.WorkOrders.SingleAsync()).CapitalizedUnitCost);
            Assert.Single(await earlyVerification.ProcessedIntegrationEvents
                .Where(item => item.ConsumerName == WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName)
                .ToListAsync());
            Assert.Empty(await earlyVerification.FinishedGoodsReceiptRequests.ToListAsync());
        }

        var receiptMediator = new RecordingMediator();
        await using (var creation = new ApplicationDbContext(options, receiptMediator))
        {
            await new CreateFinishedGoodsReceiptRequestCommandHandler(creation).Handle(
                new CreateFinishedGoodsReceiptRequestCommand(
                    "org-001",
                    "env-dev",
                    "WO-001",
                    "FG-001",
                    10m,
                    "ea",
                    completedAtUtc.AddMinutes(1),
                    UnitCost: null,
                    IdempotencyKey: "receipt-after-capitalization",
                    ProducedLotNo: "LOT-001"),
                CancellationToken.None);
            await ((IUnitOfWork)creation).SaveEntitiesAsync(CancellationToken.None);
        }

        Assert.Contains(receiptMediator.Published, notification => notification is FinishedGoodsReceiptRequestedDomainEvent);
        await using var finalVerification = new ApplicationDbContext(options, new RecordingMediator());
        Assert.Equal(25m, (await finalVerification.FinishedGoodsReceiptRequests.SingleAsync()).UnitCost);
    }

    private static WorkOrder CreateCompletedWorkOrder(DateTimeOffset completedAtUtc)
    {
        var workOrder = WorkOrder.Create(
            "org-001",
            "env-dev",
            "WO-001",
            "FG-001",
            "PV-001",
            10m,
            10,
            completedAtUtc.AddHours(1),
            "ea");
        workOrder.MarkReleased();
        workOrder.Start(completedAtUtc.AddMinutes(-10));
        workOrder.RecordProductionProgress(10m, 0m, completedAtUtc);
        return workOrder;
    }

    private static WorkOrderCostCapitalizedIntegrationEvent CreateCapitalizationEvent(DateTimeOffset completedAtUtc)
    {
        return new WorkOrderCostCapitalizedIntegrationEvent(
            "evt-cost",
            ErpIntegrationEventTypes.WorkOrderCostCapitalized,
            ErpIntegrationEventVersions.V1,
            completedAtUtc,
            ErpIntegrationEventSources.BusinessErp,
            "WO-001",
            "WO-001",
            "org-001",
            "env-dev",
            "system:erp",
            "cost-001",
            new WorkOrderCostCapitalizedPayload(
                "WO-001",
                "FG-001",
                10m,
                0m,
                250m,
                250m,
                25m,
                completedAtUtc));
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

    private sealed class RecordingUnitOfWork(ITransactionUnitOfWork inner) : ITransactionUnitOfWork
    {
        private CountingDbContextTransaction? transaction;

        public int SaveEntitiesCallCount { get; private set; }
        public int BeginTransactionCallCount { get; private set; }
        public int CommitCallCount { get; private set; }
        public int RollbackCallCount { get; private set; }
        public int TransactionDisposeAsyncCallCount => transaction?.DisposeAsyncCallCount ?? 0;

        public IDbContextTransaction? CurrentTransaction
        {
            get => inner.CurrentTransaction;
            set => inner.CurrentTransaction = value;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);

        public Task<bool> SaveEntitiesAsync(CancellationToken cancellationToken = default)
        {
            SaveEntitiesCallCount++;
            return ((IUnitOfWork)inner).SaveEntitiesAsync(cancellationToken);
        }

        public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            BeginTransactionCallCount++;
            transaction = new CountingDbContextTransaction(
                await inner.BeginTransactionAsync(cancellationToken));
            return transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCallCount++;
            return inner.CommitAsync(cancellationToken);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCallCount++;
            return inner.RollbackAsync(cancellationToken);
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CountingDbContextTransaction(IDbContextTransaction inner) : IDbContextTransaction
    {
        public int DisposeAsyncCallCount { get; private set; }
        public Guid TransactionId => inner.TransactionId;
        public bool SupportsSavepoints => inner.SupportsSavepoints;
        public void Commit() => inner.Commit();
        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);
        public void Rollback() => inner.Rollback();
        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);
        public System.Data.Common.DbTransaction GetDbTransaction() => inner.GetDbTransaction();
        public void CreateSavepoint(string name) => inner.CreateSavepoint(name);
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            inner.CreateSavepointAsync(name, cancellationToken);
        public void RollbackToSavepoint(string name) => inner.RollbackToSavepoint(name);
        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            inner.RollbackToSavepointAsync(name, cancellationToken);
        public void ReleaseSavepoint(string name) => inner.ReleaseSavepoint(name);
        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            inner.ReleaseSavepointAsync(name, cancellationToken);
        public void Dispose() => inner.Dispose();
        public async ValueTask DisposeAsync()
        {
            DisposeAsyncCallCount++;
            await inner.DisposeAsync();
        }
    }
}
