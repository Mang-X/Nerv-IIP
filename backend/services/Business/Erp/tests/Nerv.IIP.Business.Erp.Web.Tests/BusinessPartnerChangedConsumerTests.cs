using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Infrastructure.MasterData;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Contracts.MasterData;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class BusinessPartnerChangedConsumerTests
{
    [Fact]
    public async Task Disabled_event_persists_latest_partner_projection_and_is_idempotent()
    {
        var options = CreateOptions();
        var integrationEvent = PartnerChanged("evt-disabled", "partner-disabled", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z"));

        await ConsumeAsync(options, integrationEvent);
        await ConsumeAsync(options, integrationEvent);

        await using var assertionDbContext = CreateDbContext(options);
        var availability = Assert.Single(assertionDbContext.BusinessPartnerAvailabilities);
        Assert.Equal("BP-001", availability.PartnerCode);
        Assert.True(availability.IsDisabled);
        Assert.Equal(integrationEvent.Payload.ChangedAtUtc, availability.ChangedAtUtc);
        Assert.Single(assertionDbContext.ProcessedIntegrationEvents);
    }

    [Fact]
    public async Task Older_event_does_not_overwrite_newer_partner_projection()
    {
        var options = CreateOptions();
        await ConsumeAsync(options, PartnerChanged("evt-disabled", "partner-disabled", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z")));

        await ConsumeAsync(options, PartnerChanged("evt-stale-active", "partner-stale-active", "active", DateTimeOffset.Parse("2026-07-13T03:00:00Z")));

        await using var assertionDbContext = CreateDbContext(options);
        Assert.True(Assert.Single(assertionDbContext.BusinessPartnerAvailabilities).IsDisabled);
        Assert.Equal(2, assertionDbContext.ProcessedIntegrationEvents.Count());
    }

    [Fact]
    public async Task Active_event_reenables_projected_partner()
    {
        var options = CreateOptions();
        await ConsumeAsync(options, PartnerChanged("evt-disabled", "partner-disabled", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z")));

        await ConsumeAsync(options, PartnerChanged("evt-active", "partner-active", "active", DateTimeOffset.Parse("2026-07-13T05:00:00Z")));

        await using var assertionDbContext = CreateDbContext(options);
        Assert.False(Assert.Single(assertionDbContext.BusinessPartnerAvailabilities).IsDisabled);
    }

    [Fact]
    public async Task Unsupported_partner_status_is_dead_lettered_without_projection_or_business_exception()
    {
        await using var dbContext = CreateDbContext(CreateOptions());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(dbContext, deadLetters);

        await handler.HandleAsync(
            PartnerChanged("evt-invalid", "partner-invalid", "retired", DateTimeOffset.Parse("2026-07-13T04:00:00Z")),
            CancellationToken.None);

        Assert.Empty(dbContext.BusinessPartnerAvailabilities);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-partner-status", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Missing_partner_status_is_dead_lettered_without_poisoning_the_consumer()
    {
        await using var dbContext = CreateDbContext(CreateOptions());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(dbContext, deadLetters);
        var integrationEvent = PartnerChanged("evt-missing-status", "partner-missing-status", "active", DateTimeOffset.Parse("2026-07-13T04:00:00Z"))
            with { Payload = new MasterDataChangedPayload("business-partner", "BP-001", null!, DateTimeOffset.Parse("2026-07-13T04:00:00Z")) };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Empty(dbContext.BusinessPartnerAvailabilities);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-partner-status", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Missing_payload_is_dead_lettered_without_poisoning_the_consumer()
    {
        await using var dbContext = CreateDbContext(CreateOptions());
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(dbContext, deadLetters);
        var integrationEvent = PartnerChanged("evt-missing-payload", "partner-missing-payload", "active", DateTimeOffset.Parse("2026-07-13T04:00:00Z"))
            with { Payload = null! };

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        Assert.Empty(dbContext.BusinessPartnerAvailabilities);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("missing-payload", deadLetter.FailureCode);
    }

    [Fact]
    public async Task Rejected_auto_numbered_purchase_order_does_not_reserve_a_code()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.BusinessPartnerAvailabilities.Add(BusinessPartnerAvailability.Create(
                "org-001", "env-dev", "BP-001", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z"), "evt-disabled"));
            await dbContext.SaveChangesAsync(CancellationToken.None);

            await Assert.ThrowsAsync<KnownException>(() => new CreatePurchaseOrderCommandHandler(
                dbContext,
                scope.ServiceProvider.GetRequiredService<ErpCodingService>()).Handle(
                    PurchaseOrderCommand() with { PurchaseOrderNo = null, IdempotencyKey = "po-disabled-001" },
                    CancellationToken.None));
        }

        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(assertionDbContext.CodeCounters.Where(x => x.RuleKey == "purchase-order"));
        Assert.Empty(assertionDbContext.CodeIdempotencyKeys.Where(x => x.RuleKey == "purchase-order"));
    }

    [Fact]
    public async Task Rejected_auto_numbered_sales_order_does_not_reserve_a_code()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await CreateApprovedQuotationAsync(dbContext);
            dbContext.BusinessPartnerAvailabilities.Add(BusinessPartnerAvailability.Create(
                "org-001", "env-dev", "BP-001", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z"), "evt-disabled"));
            await dbContext.SaveChangesAsync(CancellationToken.None);

            await Assert.ThrowsAsync<KnownException>(() => new CreateSalesOrderCommandHandler(
                dbContext,
                new StaticCreditProfileReader(),
                scope.ServiceProvider.GetRequiredService<ErpCodingService>()).Handle(
                    new CreateSalesOrderCommand("org-001", "env-dev", null, "QT-001", "so-disabled-001"),
                    CancellationToken.None));
        }

        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(assertionDbContext.CodeCounters.Where(x => x.RuleKey == "sales-order"));
        Assert.Empty(assertionDbContext.CodeIdempotencyKeys.Where(x => x.RuleKey == "sales-order"));
    }

    [Fact]
    public async Task Existing_idempotent_orders_can_be_replayed_after_partner_is_disabled()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var codingService = scope.ServiceProvider.GetRequiredService<ErpCodingService>();
        await CreateApprovedQuotationAsync(dbContext);
        var purchaseCommand = PurchaseOrderCommand() with { PurchaseOrderNo = null, IdempotencyKey = "po-replay-001" };
        var salesCommand = new CreateSalesOrderCommand("org-001", "env-dev", null, "QT-001", "so-replay-001");
        var purchaseOrderId = await new CreatePurchaseOrderCommandHandler(dbContext, codingService).Handle(purchaseCommand, CancellationToken.None);
        var salesOrderId = await new CreateSalesOrderCommandHandler(dbContext, new StaticCreditProfileReader(), codingService).Handle(salesCommand, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        dbContext.BusinessPartnerAvailabilities.Add(BusinessPartnerAvailability.Create(
            "org-001", "env-dev", "BP-001", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z"), "evt-disabled"));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var replayedPurchaseOrderId = await new CreatePurchaseOrderCommandHandler(dbContext, codingService).Handle(purchaseCommand, CancellationToken.None);
        var replayedSalesOrderId = await new CreateSalesOrderCommandHandler(dbContext, new StaticCreditProfileReader(), codingService).Handle(salesCommand, CancellationToken.None);

        Assert.Equal(purchaseOrderId, replayedPurchaseOrderId);
        Assert.Equal(salesOrderId, replayedSalesOrderId);
        Assert.Single(dbContext.PurchaseOrders);
        Assert.Single(dbContext.SalesOrders);
    }

    [Fact]
    public async Task Concurrent_older_event_cannot_overwrite_a_newer_projection()
    {
        var options = CreateOptions();
        await ConsumeAsync(options, PartnerChanged("evt-seed", "partner-seed", "active", DateTimeOffset.Parse("2026-07-13T02:00:00Z")));
        await using var olderContext = CreateDbContext(options);
        await using var newerContext = CreateDbContext(options);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var olderHandler = new BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(olderContext, deadLetters);
        var newerHandler = new BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(newerContext, deadLetters);

        await olderHandler.HandleAsync(PartnerChanged("evt-older", "partner-older", "active", DateTimeOffset.Parse("2026-07-13T03:00:00Z")), CancellationToken.None);
        await newerHandler.HandleAsync(PartnerChanged("evt-newer", "partner-newer", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z")), CancellationToken.None);
        await newerContext.SaveChangesAsync(CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => olderContext.SaveChangesAsync(CancellationToken.None));

        await using var assertionDbContext = CreateDbContext(options);
        Assert.True(Assert.Single(assertionDbContext.BusinessPartnerAvailabilities).IsDisabled);
    }

    [Fact]
    public async Task Consumed_disabled_event_blocks_new_purchase_and_sales_order_submission()
    {
        var options = CreateOptions();
        await CreateApprovedQuotationAsync(options);
        await ConsumeAsync(options, PartnerChanged("evt-disabled", "partner-disabled", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z")));

        await using var dbContext = CreateDbContext(options);
        var purchaseException = await Assert.ThrowsAsync<KnownException>(() =>
            new CreatePurchaseOrderCommandHandler(dbContext).Handle(PurchaseOrderCommand(), CancellationToken.None));
        var salesException = await Assert.ThrowsAsync<KnownException>(() =>
            new CreateSalesOrderCommandHandler(dbContext, new StaticCreditProfileReader()).Handle(
                new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001"),
                CancellationToken.None));

        Assert.Contains("disabled", purchaseException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", salesException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(dbContext.PurchaseOrders);
        Assert.Empty(dbContext.SalesOrders);
    }

    [Fact]
    public async Task Consumed_active_event_allows_new_purchase_and_sales_order_submission()
    {
        var options = CreateOptions();
        await CreateApprovedQuotationAsync(options);
        await ConsumeAsync(options, PartnerChanged("evt-disabled", "partner-disabled", "disabled", DateTimeOffset.Parse("2026-07-13T04:00:00Z")));
        await ConsumeAsync(options, PartnerChanged("evt-active", "partner-active", "active", DateTimeOffset.Parse("2026-07-13T05:00:00Z")));

        await using var dbContext = CreateDbContext(options);
        await new CreatePurchaseOrderCommandHandler(dbContext).Handle(PurchaseOrderCommand(), CancellationToken.None);
        await new CreateSalesOrderCommandHandler(dbContext, new StaticCreditProfileReader()).Handle(
            new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Single(dbContext.PurchaseOrders);
        Assert.Single(dbContext.SalesOrders);
    }

    private static CreatePurchaseOrderCommand PurchaseOrderCommand()
    {
        return new CreatePurchaseOrderCommand(
            "org-001",
            "env-dev",
            "PO-001",
            "BP-001",
            "SITE-001",
            [new PurchaseOrderCommandLine("LINE-001", "SKU-RM", "EA", 1m, 10m, new DateOnly(2099, 1, 31))]);
    }

    private static async Task CreateApprovedQuotationAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var dbContext = CreateDbContext(options);
        await CreateApprovedQuotationAsync(dbContext);
    }

    private static async Task CreateApprovedQuotationAsync(ApplicationDbContext dbContext)
    {
        await new CreateQuotationCommandHandler(dbContext).Handle(
            new CreateQuotationCommand(
                "org-001",
                "env-dev",
                "QT-001",
                "BP-001",
                new DateOnly(2099, 1, 1),
                [new QuotationCommandLine("LINE-001", "SKU-FG", "EA", 1m, 20m, new DateOnly(2099, 1, 31))]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        await new ApproveQuotationCommandHandler(dbContext).Handle(
            new ApproveQuotationCommand("org-001", "env-dev", "QT-001"),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task ConsumeAsync(
        DbContextOptions<ApplicationDbContext> options,
        BusinessPartnerChangedIntegrationEvent integrationEvent)
    {
        await using var dbContext = CreateDbContext(options);
        var handler = new BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability(
            dbContext,
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static BusinessPartnerChangedIntegrationEvent PartnerChanged(
        string eventId,
        string idempotencyKey,
        string status,
        DateTimeOffset changedAtUtc)
    {
        return new BusinessPartnerChangedIntegrationEvent(
            eventId,
            MasterDataIntegrationEventTypes.BusinessPartnerChanged,
            MasterDataIntegrationEventVersions.V1,
            changedAtUtc,
            MasterDataIntegrationEventSources.BusinessMasterData,
            $"corr-{eventId}",
            $"cause-{eventId}",
            "org-001",
            "env-dev",
            "user:masterdata-admin",
            idempotencyKey,
            new MasterDataChangedPayload("business-partner", "BP-001", status, changedAtUtc));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-partner-consumer-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot())
            .Options;
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
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

    private sealed class StaticCreditProfileReader : ICustomerCreditProfileReader
    {
        public Task<CustomerCreditProfile?> GetAsync(
            string organizationId,
            string environmentId,
            string customerCode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<CustomerCreditProfile?>(new CustomerCreditProfile(customerCode, 1_000m, "CNY"));
        }
    }
}
