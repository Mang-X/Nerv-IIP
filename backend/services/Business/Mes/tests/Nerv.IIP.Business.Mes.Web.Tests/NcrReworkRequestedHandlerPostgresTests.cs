using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.QualityAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;
using Nerv.IIP.Business.Mes.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Quality;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class NcrReworkRequestedHandlerPostgresTests
{
    [MesRealPostgresFact]
    public async Task Rework_request_creates_one_source_linked_work_order_and_persists_inbox_and_numbering()
    {
        await using var provider = await ResetAndCreateProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");

        await HandleAsync(provider, CreateEvent());
        await HandleAsync(provider, CreateEvent(eventId: "evt-replay-002"));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrder = await db.WorkOrders.SingleAsync(x => x.SourceNcrId == "ncr-001");
        Assert.Equal(WorkOrder.ReworkType, workOrder.WorkOrderType);
        Assert.Equal("WO-SOURCE-001", workOrder.SourceWorkOrderId);
        Assert.Equal("OP-SOURCE-10", workOrder.SourceOperationTaskId);
        Assert.Equal("NCR-2026-0001", workOrder.SourceNcrCode);
        Assert.Equal("SKU-001", workOrder.SkuId);
        Assert.Equal(3m, workOrder.Quantity);
        Assert.Equal("LOT-001", workOrder.SourceLotNo);
        Assert.Equal("SN-001", workOrder.SourceSerialNo);
        Assert.Single(await db.ProcessedIntegrationEvents
            .Where(x => x.ConsumerName == NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName)
            .ToArrayAsync());
        var numbering = await db.CodeIdempotencyKeys.SingleAsync(x => x.IdempotencyKey == CreateEvent().IdempotencyKey);
        Assert.Equal(workOrder.WorkOrderIdValue, numbering.Code);
    }

    [MesRealPostgresFact]
    public async Task Same_ncr_with_different_payload_is_dead_lettered_instead_of_treated_as_replay()
    {
        await using var provider = await ResetAndCreateProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await HandleAsync(provider, CreateEvent());

        await HandleAsync(provider, CreateEvent(eventId: "evt-conflict", quantity: 2m));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.WorkOrders.Where(x => x.SourceNcrId == "ncr-001").ToArrayAsync());
        var deadLetter = Assert.Single(await new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db)
            .ListAsync(
                NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None));
        Assert.Equal("mes.ncrReworkRequested.payloadConflict", deadLetter.FailureCode);
    }

    [MesRealPostgresFact]
    public async Task Missing_or_mismatched_mes_source_facts_fail_closed()
    {
        await using var provider = await ResetAndCreateProviderAsync();
        await HandleAsync(provider, CreateEvent(eventId: "evt-missing-defect"));
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await HandleAsync(provider, CreateEvent(eventId: "evt-sku-mismatch", skuCode: "SKU-WRONG", idempotencyKey: "quality:rework:sku-wrong"));
        await HandleAsync(provider, CreateEvent(eventId: "evt-quantity-mismatch", quantity: 2m, idempotencyKey: "quality:rework:quantity-wrong"));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.WorkOrders.Where(x => x.WorkOrderType == WorkOrder.ReworkType).ToArrayAsync());
        var deadLetters = await new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db)
            .ListAsync(
                NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder.ConsumerName,
                IntegrationEventDeadLetterStatus.Pending,
                CancellationToken.None);
        Assert.Equal(
            [
                "mes.ncrReworkRequested.sourceDefectMissing",
                "mes.ncrReworkRequested.skuMismatch",
                "mes.ncrReworkRequested.quantityMismatch",
            ],
            deadLetters.Select(x => x.FailureCode).ToArray());
    }

    [MesRealPostgresFact]
    public async Task Concurrent_delivery_serializes_on_ncr_scope_and_creates_exactly_one_work_order()
    {
        await using var provider = await ResetAndCreateProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstHandler = CreateHandler(
            firstScope.ServiceProvider,
            new BlockingReworkScopeCoordinator(
                firstScope.ServiceProvider.GetRequiredService<IMesReworkWorkOrderScopeCoordinator>(),
                firstEntered,
                releaseFirst));
        var secondHandler = CreateHandler(secondScope.ServiceProvider);

        var firstTask = firstHandler.HandleAsync(CreateEvent(), CancellationToken.None);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = secondHandler.HandleAsync(CreateEvent(eventId: "evt-concurrent-002"), CancellationToken.None);
        await MesPostgresAdvisoryLockProbe.WaitForWaitersAsync(
            MesPostgresLaneDatabase.ConnectionString,
            expectedWaiters: 1,
            scopeDescription: "MES NCR rework creation");
        releaseFirst.SetResult();
        await Task.WhenAll(firstTask, secondTask);

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.WorkOrders.Where(x => x.SourceNcrId == "ncr-001").ToArrayAsync());
        Assert.Single(await db.CodeIdempotencyKeys.Where(x => x.IdempotencyKey == CreateEvent().IdempotencyKey).ToArrayAsync());
    }

    [MesRealPostgresFact]
    public async Task Same_ncr_identity_is_isolated_by_organization_and_environment()
    {
        await using var provider = await ResetAndCreateProviderAsync();
        await SeedSourceAsync(provider, "org-001", "env-dev");
        await SeedSourceAsync(provider, "org-002", "env-test");

        await HandleAsync(provider, CreateEvent());
        await HandleAsync(provider, CreateEvent(
            eventId: "evt-other-scope",
            organizationId: "org-002",
            environmentId: "env-test",
            idempotencyKey: "quality:rework:org-002:env-test:ncr-001"));

        await using var assertionScope = provider.CreateAsyncScope();
        var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var workOrders = await db.WorkOrders.Where(x => x.SourceNcrId == "ncr-001").ToArrayAsync();
        Assert.Equal(2, workOrders.Length);
        Assert.Contains(workOrders, x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev");
        Assert.Contains(workOrders, x => x.OrganizationId == "org-002" && x.EnvironmentId == "env-test");
    }

    private static async Task<ServiceProvider> ResetAndCreateProviderAsync()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var services = new ServiceCollection();
        services.AddSingleton<IMediator>(new NoopMediator());
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            MesPostgresLaneDatabase.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "mes")));
        services.AddScoped<MesCodingService>();
        services.AddScoped<IMesReworkWorkOrderScopeCoordinator, PostgreSqlMesReworkWorkOrderScopeCoordinator>();
        var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(db);
        await db.Database.MigrateAsync();
        return provider;
    }

    private static async Task SeedSourceAsync(
        IServiceProvider provider,
        string organizationId,
        string environmentId)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.WorkOrders.Add(WorkOrder.Create(
            organizationId,
            environmentId,
            "WO-SOURCE-001",
            "SKU-001",
            "PV-001",
            10m,
            100,
            DateTimeOffset.Parse("2026-08-30T08:00:00Z"),
            "PCS"));
        db.OperationTasks.Add(OperationTask.Queue(
            organizationId,
            environmentId,
            "WO-SOURCE-001",
            "OP-SOURCE-10",
            10,
            "WC-001",
            [],
            DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
            TimeSpan.FromMinutes(30)));
        db.DefectRecords.Add(DefectRecord.Create(
            organizationId,
            environmentId,
            "DEF-001",
            "WO-SOURCE-001",
            "OP-SOURCE-10",
            "surface-defect",
            3m,
            DateTimeOffset.Parse("2026-08-29T07:00:00Z")));
        await db.SaveChangesAsync();
    }

    private static async Task HandleAsync(IServiceProvider provider, NcrReworkRequestedIntegrationEvent integrationEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        await CreateHandler(scope.ServiceProvider).HandleAsync(integrationEvent, CancellationToken.None);
    }

    private static NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder CreateHandler(
        IServiceProvider provider,
        IMesReworkWorkOrderScopeCoordinator? coordinator = null)
    {
        var db = provider.GetRequiredService<ApplicationDbContext>();
        return new NcrReworkRequestedIntegrationEventHandlerForCreateMesWorkOrder(
            db,
            provider.GetRequiredService<MesCodingService>(),
            new PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>(db),
            coordinator ?? provider.GetRequiredService<IMesReworkWorkOrderScopeCoordinator>());
    }

    private static NcrReworkRequestedIntegrationEvent CreateEvent(
        string eventId = "evt-rework-001",
        string organizationId = "org-001",
        string environmentId = "env-dev",
        string skuCode = "SKU-001",
        decimal quantity = 3m,
        string idempotencyKey = "quality:rework:org-001:env-dev:ncr-001") => new(
            eventId,
            QualityIntegrationEventTypes.NcrReworkRequested,
            QualityIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-08-29T08:00:00Z"),
            QualityIntegrationEventSources.BusinessQuality,
            "corr-001",
            "cause-001",
            organizationId,
            environmentId,
            "user:quality-manager",
            idempotencyKey,
            new NcrReworkRequestedPayload(
                "ncr-001",
                "NCR-2026-0001",
                "DEF-001",
                skuCode,
                quantity,
                "LOT-001",
                "SN-001",
                DateTimeOffset.Parse("2026-08-29T08:00:00Z")));

    private sealed class BlockingReworkScopeCoordinator(
        IMesReworkWorkOrderScopeCoordinator inner,
        TaskCompletionSource firstEntered,
        TaskCompletionSource releaseFirst) : IMesReworkWorkOrderScopeCoordinator
    {
        public Task ExecuteAsync(
            string organizationId,
            string environmentId,
            string ncrId,
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken) =>
            inner.ExecuteAsync(
                organizationId,
                environmentId,
                ncrId,
                async token =>
                {
                    await action(token);
                    firstEntered.SetResult();
                    await releaseFirst.Task.WaitAsync(token);
                },
                cancellationToken);
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
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
