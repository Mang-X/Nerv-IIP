using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ApprovalCompletedConsumerReliabilityTests
{
    [Fact]
    public async Task ApprovalCompletedHandler_SkipsDuplicateApprovalBeforeRepeatingSideEffect()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"erp-approval-duplicate-{Guid.CreateVersion7():N}", databaseRoot);
        var integrationEvent = ApprovalCompletedEvent(ApprovalResults.Approved);

        await using (var dbContext = CreateDbContext(options))
        {
            dbContext.PurchaseOrders.Add(PendingApprovalPurchaseOrder("PO-001", "chain-001"));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await using var assertionDbContext = CreateDbContext(options);
        Assert.Equal(PurchaseOrderStatus.Released, assertionDbContext.PurchaseOrders.Single().Status);
        var processed = Assert.Single(assertionDbContext.ProcessedIntegrationEvents);
        Assert.Equal(ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName, processed.ConsumerName);
        Assert.Equal(integrationEvent.IdempotencyKey, processed.IdempotencyKey);
    }

    [Fact]
    public async Task ApprovalCompletedHandler_DeadLettersUnsupportedVersionWithoutChangingPurchaseOrder()
    {
        await using var dbContext = CreateDbContext();
        dbContext.PurchaseOrders.Add(PendingApprovalPurchaseOrder("PO-001", "chain-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, deadLetters);

        await handler.HandleAsync(ApprovalCompletedEvent(ApprovalResults.Approved, eventVersion: 2), CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.PendingApproval, dbContext.PurchaseOrders.Single().Status);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unsupported-version", deadLetter.FailureCode);
        Assert.Equal(2, deadLetter.EventVersion);
    }

    [Fact]
    public async Task ApprovalCompletedHandler_DeadLettersUnexpectedSourceServiceWithoutChangingPurchaseOrder()
    {
        await using var dbContext = CreateDbContext();
        dbContext.PurchaseOrders.Add(PendingApprovalPurchaseOrder("PO-001", "chain-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, deadLetters);

        await handler.HandleAsync(
            ApprovalCompletedEvent(ApprovalResults.Approved) with { SourceService = "business-erp" },
            CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.PendingApproval, dbContext.PurchaseOrders.Single().Status);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        var deadLetter = Assert.Single(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
        Assert.Equal("unexpected-source-service", deadLetter.FailureCode);
        Assert.Equal("business-erp", deadLetter.SourceService);
    }

    [Fact]
    public void PostgreSQL_profile_uses_persistent_dead_letter_store()
    {
        using var factory = new ErpPostgreSqlWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IIntegrationEventDeadLetterStore>();

        Assert.IsType<PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>(store);
    }

    private static ApprovalCompletedIntegrationEvent ApprovalCompletedEvent(
        string result,
        int eventVersion = ApprovalIntegrationEventVersions.V1)
    {
        return new ApprovalCompletedIntegrationEvent(
            "evt-approval-001",
            ApprovalIntegrationEventTypes.ApprovalApproved,
            eventVersion,
            DateTimeOffset.Parse("2026-06-17T03:00:00Z"),
            ApprovalIntegrationEventSources.BusinessApproval,
            "corr-001",
            "cause-001",
            "org-001",
            "env-dev",
            "system:approval",
            "business-approval:approved:org-001:env-dev:chain-001",
            new ApprovalCompletedPayload(
                "chain-001",
                result,
                "user",
                "u-manager",
                null,
                null,
                new ApprovalDocumentReferencePayload("business-erp", "purchase-order", "PO-001", null)));
    }

    private static PurchaseOrder PendingApprovalPurchaseOrder(string purchaseOrderNo, string chainId)
    {
        var order = PurchaseOrder.Create(
            "org-001",
            "env-dev",
            purchaseOrderNo,
            "SUP-001",
            "SITE-01",
            [new PurchaseOrderLineDraft("LINE-001", "SKU-RM-1000", "kg", 5m, 12.5m, new DateOnly(2026, 6, 5))]);
        order.MarkApprovalRequested(chainId);
        return order;
    }

    private static ApplicationDbContext CreateDbContext()
    {
        return CreateDbContext(CreateDbContextOptions($"erp-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot()));
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options)
    {
        return new ApplicationDbContext(options, new NoopMediator());
    }

    private static DbContextOptions<ApplicationDbContext> CreateDbContextOptions(
        string databaseName,
        InMemoryDatabaseRoot databaseRoot)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot send requests.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Noop mediator cannot stream requests.");
        }
    }

    private sealed class ErpPostgreSqlWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSQL"] = "Host=localhost;Database=nerv_iip_erp_dead_letter_test;Username=nerv;Password=nerv",
                    ["InternalService:BearerToken"] = "test-internal-token",
                });
            });
        }
    }
}
