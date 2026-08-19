using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Erp.Web.Tests;

[Collection(WebApplicationFactoryCollection.Name)]
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
        }

        await using (var dbContext = CreateDbContext(options))
        {
            var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(
                dbContext,
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
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

    /// <summary>
    /// #1683 回归：**种子形状**的采购审批链（单据引用的来源服务取审批契约唯一事实来源
    /// <see cref="ApprovalSourceServices.BusinessErp"/>）被批准后，ERP 回写必须**真的发生**——
    /// 采购订单从 pending-approval 走到 released，而不是在来源服务分流处静默 return。
    /// </summary>
    [Fact]
    public async Task ApprovalCompletedHandler_ReleasesPurchaseOrderForSeedShapedSourceService()
    {
        await using var dbContext = CreateDbContext();
        dbContext.PurchaseOrders.Add(PendingApprovalPurchaseOrder("PO-001", "chain-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, deadLetters);

        await handler.HandleAsync(
            ApprovalCompletedEvent(
                ApprovalResults.Approved,
                documentSourceService: ApprovalSourceServices.BusinessErp),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.Released, dbContext.PurchaseOrders.Single().Status);
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    /// <summary>
    /// #1683 事故形态钉死：来源服务写成旧字面量 <c>erp</c> 时，回写**静默失效**——
    /// 订单永停 pending、无 processed 记录、连死信都没有（走查实证的「审批通过却没有任何报错」）。
    /// 本用例与上一条成对存在：证明上一条的绿是「真的回写了」而不是断言没鉴别力。
    /// </summary>
    [Fact]
    public async Task ApprovalCompletedHandler_SilentlyIgnoresLegacyErpSourceServiceAndLeavesOrderPending()
    {
        await using var dbContext = CreateDbContext();
        dbContext.PurchaseOrders.Add(PendingApprovalPurchaseOrder("PO-001", "chain-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, deadLetters);

        await handler.HandleAsync(
            ApprovalCompletedEvent(ApprovalResults.Approved, documentSourceService: "erp"),
            CancellationToken.None);

        Assert.Equal(PurchaseOrderStatus.PendingApproval, dbContext.PurchaseOrders.Single().Status);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    /// <summary>
    /// #1702 回写侧成对用例（正路径）：信用解冻审批单据类型取审批契约唯一事实来源
    /// <see cref="ApprovalDocumentTypes.SalesOrderCreditRelease"/> 时，ERP 回写必须**真的发生**——
    /// 销售订单从 credit-held 走到 released，而不是掉进采购分支再静默 return。
    /// </summary>
    [Fact]
    public async Task ApprovalCompletedHandler_ReleasesCreditHoldForTheContractDocumentType()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SalesOrders.Add(CreditHeldSalesOrder("SO-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, deadLetters);

        await handler.HandleAsync(
            SalesCreditReleaseApprovedEvent(ApprovalDocumentTypes.SalesOrderCreditRelease),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("released", dbContext.SalesOrders.Single().Status);
        Assert.Single(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    /// <summary>
    /// #1702 回写侧成对用例（事故形态）：单据类型漂移（发起侧 / 种子侧 / 消费侧三处此前各写各的字面量）时
    /// 回写**静默失效**——事件先躲开信用解冻分支，再在采购分支因单据类型不是 purchase-order 静默 return：
    /// 销售订单永停 credit-held、无 processed 记录、连死信都没有。
    /// 本用例与上一条成对存在：证明上一条的绿是「真的解冻了」而不是断言没鉴别力。
    /// </summary>
    [Theory]
    [InlineData("sales-credit-release")]
    [InlineData("sales-order-credit-hold-release")]
    public async Task ApprovalCompletedHandler_SilentlyIgnoresDriftedCreditReleaseDocumentTypeAndLeavesOrderHeld(
        string driftedDocumentType)
    {
        await using var dbContext = CreateDbContext();
        dbContext.SalesOrders.Add(CreditHeldSalesOrder("SO-001"));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();
        var handler = new ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder(dbContext, deadLetters);

        await handler.HandleAsync(SalesCreditReleaseApprovedEvent(driftedDocumentType), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.Equal("credit-held", dbContext.SalesOrders.Single().Status);
        Assert.Empty(dbContext.ProcessedIntegrationEvents);
        Assert.Empty(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    private static ApprovalCompletedIntegrationEvent SalesCreditReleaseApprovedEvent(string documentType)
    {
        return new ApprovalCompletedIntegrationEvent(
            "evt-sales-credit-001",
            ApprovalIntegrationEventTypes.ApprovalApproved,
            ApprovalIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-06-17T03:00:00Z"),
            ApprovalIntegrationEventSources.BusinessApproval,
            "corr-sales-credit-001",
            "cause-sales-credit-001",
            "org-001",
            "env-dev",
            "user:credit-manager",
            "business-approval:approved:org-001:env-dev:chain-sales-credit-001",
            new ApprovalCompletedPayload(
                "chain-sales-credit-001",
                ApprovalResults.Approved,
                "user",
                "credit-manager",
                null,
                null,
                new ApprovalDocumentReferencePayload(
                    ApprovalSourceServices.BusinessErp,
                    documentType,
                    "SO-001",
                    null),
                "user:sales-001"));
    }

    /// <summary>信用冻结现场：额度 10 元的客户下了 40 元的单，落库即 credit-held。</summary>
    private static SalesOrder CreditHeldSalesOrder(string salesOrderNo)
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-CREDIT-001",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
            [new QuotationLineDraft("LINE-001", "SKU-FG-1000", "EA", 2m, 20m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45))]);
        quotation.Approve();

        var order = SalesOrder.CreateFromQuotation(
            salesOrderNo,
            "SITE-01",
            quotation,
            new CustomerCreditSnapshot("CUST-001", CreditLimit: 10m, OpenReceivableAmount: 0m, ActiveSalesOrderExposure: 0m));
        Assert.Equal("credit-held", order.Status);
        return order;
    }

    private static ApprovalCompletedIntegrationEvent ApprovalCompletedEvent(
        string result,
        int eventVersion = ApprovalIntegrationEventVersions.V1,
        string? documentSourceService = null)
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
                new ApprovalDocumentReferencePayload(
                    documentSourceService ?? ApprovalSourceServices.BusinessErp,
                    "purchase-order",
                    "PO-001",
                    null)));
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
