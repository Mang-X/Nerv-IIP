using MediatR;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Erp;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Repository.EntityFrameworkCore;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

public sealed class WorkOrderCapitalizationConcurrencyPostgresTests
{
    [MesRealPostgresFact]
    public async Task Receipt_creation_and_capitalization_serialize_without_cap_redelivery()
    {
        await using var database = await TemporaryDatabase.CreateAsync(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(database.ConnectionString)
            .Options;
        await using (var setup = CreateContext(options, new NoopMediator()))
        {
            await setup.Database.MigrateAsync(CancellationToken.None);
            var completedAtUtc = DateTimeOffset.Parse("2026-07-23T07:15:28Z");
            setup.WorkOrders.Add(CreateCompletedWorkOrder(completedAtUtc));
            setup.OperationTasks.Add(OperationTask.Create(
                "org-001",
                "env-dev",
                "WO-001",
                "OP-10",
                OperationTaskLifecycleStatus.Completed,
                10,
                "WC-10",
                [],
                completedAtUtc.AddMinutes(-10),
                TimeSpan.FromMinutes(10),
                completedAtUtc.AddMinutes(-10),
                completedAtUtc));
            setup.ProductionReports.Add(ProductionReport.Record(
                "org-001",
                "env-dev",
                "PRPT-001",
                "WO-001",
                "OP-10",
                10m,
                0m,
                true,
                completedAtUtc,
                producedLotNo: "LOT-001"));
            setup.OutputLotGenealogies.Add(OutputLotGenealogy.Create(
                "org-001", "env-dev", "WO-001", "OP-10", "PRPT-001", "LOT-001", null, 10m, completedAtUtc));
            await setup.SaveChangesAsync(CancellationToken.None);
        }

        var receiptTracked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReceiptCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capitalizationMediator = new RecordingMediator();
        await using var receiptContext = CreateContext(options, new NoopMediator());
        await using var capitalizationContext = CreateContext(options, capitalizationMediator);
        var blockingCoordinator = new BlockingWorkOrderCapitalizationScopeCoordinator(
            new PostgreSqlMesWorkOrderCapitalizationScopeCoordinator(receiptContext, receiptContext),
            receiptTracked,
            allowReceiptCommit);
        var receiptHandler = new CreateFinishedGoodsReceiptRequestCommandHandler(
            receiptContext,
            blockingCoordinator);
        var receiptTask = receiptHandler.Handle(
            new CreateFinishedGoodsReceiptRequestCommand(
                "org-001",
                "env-dev",
                "WO-001",
                "FG-001",
                10m,
                "ea",
                DateTimeOffset.Parse("2026-07-23T07:16:28Z"),
                UnitCost: null,
                IdempotencyKey: "receipt-racing-capitalization",
                ProducedLotNo: "LOT-001"),
            CancellationToken.None);
        await receiptTracked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var capitalizationHandler = new WorkOrderCostCapitalizedIntegrationEventHandler(
            capitalizationContext,
            new InMemoryIntegrationEventDeadLetterStore(),
            capitalizationContext,
            new PostgreSqlMesWorkOrderCapitalizationScopeCoordinator(
                capitalizationContext,
                capitalizationContext));
        var capitalizationTask = capitalizationHandler.HandleAsync(
            CreateCapitalizationEvent(),
            CancellationToken.None);

        // Wait for the real edge instead of a settle window: the capitalization transaction is observably
        // parked on the work-order-scope advisory lock held by the in-flight receipt creation.
        await MesPostgresAdvisoryLockProbe.WaitForWaitersAsync(
            database.ConnectionString,
            expectedWaiters: 1,
            scopeDescription: "the MES work-order capitalization scope held by the in-flight receipt creation");
        Assert.False(
            capitalizationTask.IsCompleted,
            "Capitalization must wait for in-flight receipt creation in the same work-order scope.");
        allowReceiptCommit.SetResult();
        await receiptTask;
        await capitalizationTask;

        await using var verification = CreateContext(options, new NoopMediator());
        Assert.Equal(25m, (await verification.WorkOrders.SingleAsync()).CapitalizedUnitCost);
        Assert.Equal(25m, (await verification.FinishedGoodsReceiptRequests.SingleAsync()).UnitCost);
        Assert.Single(await verification.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == WorkOrderCostCapitalizedIntegrationEventHandler.ConsumerName)
            .ToListAsync());
        Assert.Contains(
            capitalizationMediator.Published,
            notification => notification is FinishedGoodsReceiptRequestedDomainEvent);
    }

    private static WorkOrder CreateCompletedWorkOrder(DateTimeOffset completedAtUtc)
    {
        var workOrder = WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "FG-001", "PV-001",
            10m, 10, completedAtUtc.AddHours(1), "ea");
        workOrder.MarkReleased();
        workOrder.Start(completedAtUtc.AddMinutes(-10));
        workOrder.RecordProductionProgress(10m, 0m, completedAtUtc);
        return workOrder;
    }

    private static WorkOrderCostCapitalizedIntegrationEvent CreateCapitalizationEvent()
    {
        var completedAtUtc = DateTimeOffset.Parse("2026-07-23T07:15:28Z");
        return new WorkOrderCostCapitalizedIntegrationEvent(
            "evt-cost-race",
            ErpIntegrationEventTypes.WorkOrderCostCapitalized,
            ErpIntegrationEventVersions.V1,
            completedAtUtc,
            ErpIntegrationEventSources.BusinessErp,
            "WO-001",
            "WO-001",
            "org-001",
            "env-dev",
            "system:erp",
            "work-order-cost-capitalized:org-001:env-dev:WO-001",
            new WorkOrderCostCapitalizedPayload(
                "WO-001", "FG-001", 10m, 0m, 250m, 250m, 25m, completedAtUtc));
    }

    private static ApplicationDbContext CreateContext(
        DbContextOptions<ApplicationDbContext> options,
        IMediator mediator) =>
        new(options, mediator);

    private sealed class BlockingWorkOrderCapitalizationScopeCoordinator(
        IMesWorkOrderCapitalizationScopeCoordinator inner,
        TaskCompletionSource receiptTracked,
        TaskCompletionSource allowReceiptCommit) : IMesWorkOrderCapitalizationScopeCoordinator
    {
        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            string workOrderId,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                workOrderId,
                async token =>
                {
                    await action(token);
                    receiptTracked.SetResult();
                    await allowReceiptCommit.Task.WaitAsync(token);
                },
                cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string organizationId,
            string environmentId,
            string workOrderId,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                workOrderId,
                async token =>
                {
                    var result = await action(token);
                    receiptTracked.SetResult();
                    await allowReceiptCommit.Task.WaitAsync(token);
                    return result;
                },
                cancellationToken);
    }

    private sealed class RecordingMediator : IMediator
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TemporaryDatabase(
        string adminConnectionString,
        string databaseName,
        string connectionString) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync(string baseConnectionString)
        {
            var databaseName = $"nerv_mes_cost_race_{Guid.CreateVersion7():N}";
            var adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                Database = "postgres"
            }.ConnectionString;
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
            return new TemporaryDatabase(
                adminConnectionString,
                databaseName,
                new NpgsqlConnectionStringBuilder(baseConnectionString)
                {
                    Database = databaseName
                }.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)",
                connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
