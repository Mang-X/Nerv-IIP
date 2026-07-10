using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountAdjustmentAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockCountTaskAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockLedgerAggregate;
using Nerv.IIP.Business.Inventory.Domain.AggregatesModel.StockMovementAggregate;
using Nerv.IIP.Business.Inventory.Infrastructure;
using Nerv.IIP.Business.Inventory.Web.Application.Approval;
using Nerv.IIP.Business.Inventory.Web.Application.Commands.StockCounts;
using Nerv.IIP.Business.Inventory.Web.Application.IntegrationEventHandlers;
using Nerv.IIP.Contracts.Approval;
using Nerv.IIP.Messaging.CAP;

namespace Nerv.IIP.Business.Inventory.Web.Tests;

public sealed class StockCountApprovalTests
{
    [Fact]
    public async Task Above_threshold_adjustment_remains_frozen_and_unposted_until_approval_completes()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"inventory-count-approval-{Guid.CreateVersion7():N}", databaseRoot);
        var approvalClient = new CapturingStockCountApprovalClient("chain-count-001");

        await using (var dbContext = CreateDbContext(options))
        {
            var ledger = NewLedgerWithOnHand(10m);
            var task = DomainCountTaskFactory.NewTask(ledger);
            ledger.FreezeForCount(task.CountTaskCode);
            dbContext.StockLedgers.Add(ledger);
            dbContext.StockCountTasks.Add(task);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var result = await new ConfirmStockCountAdjustmentCommandHandler(
                dbContext,
                approvalClient,
                Options.Create(new StockCountAdjustmentApprovalOptions
                {
                    QuantityThreshold = 1m,
                    AmountThreshold = decimal.MaxValue,
                })).Handle(
                    new ConfirmStockCountAdjustmentCommand(task.Id, 7m, "idem-count-approval-001"),
                    CancellationToken.None);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            Assert.Equal(StockCountAdjustmentStatuses.PendingApproval, result.Status);
            Assert.Null(result.MovementId);
            Assert.Equal(10m, dbContext.StockLedgers.Single().OnHandQuantity);
            Assert.True(dbContext.StockLedgers.Single().IsFrozenForCount);
            Assert.Equal(StockCountTaskStatuses.PendingApproval, dbContext.StockCountTasks.Single().Status);
            var adjustment = Assert.Single(dbContext.StockCountAdjustments);
            Assert.Equal(StockCountAdjustmentStatuses.PendingApproval, adjustment.Status);
            Assert.Equal("chain-count-001", adjustment.ApprovalChainId);
            Assert.Null(adjustment.MovementId);
        }

        Assert.Equal("COUNT-VARIANCE", approvalClient.Request!.TemplateCode);
        Assert.Equal("inventory", approvalClient.Request.SourceService);
        Assert.Equal("inventory-count-variance", approvalClient.Request.DocumentType);
        Assert.Equal("COUNT-001", approvalClient.Request.DocumentId);
    }

    [Fact]
    public void Approval_client_is_required_instead_of_fabricating_a_chain_id()
    {
        var options = CreateDbContextOptions($"inventory-count-approval-client-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
        using var dbContext = CreateDbContext(options);

        Assert.Throws<ArgumentNullException>(() => new ConfirmStockCountAdjustmentCommandHandler(
            dbContext,
            approvalClient: null!,
            approvalOptions: Options.Create(new StockCountAdjustmentApprovalOptions())));
    }

    [Theory]
    [InlineData(ApprovalResults.Approved, StockCountAdjustmentStatuses.Posted, StockCountTaskStatuses.Confirmed, 7, false)]
    [InlineData(ApprovalResults.Rejected, StockCountAdjustmentStatuses.Voided, StockCountTaskStatuses.RecountRequired, 10, false)]
    public async Task Approval_completion_posts_or_voids_the_pending_adjustment(
        string approvalResult,
        string expectedAdjustmentStatus,
        string expectedTaskStatus,
        decimal expectedOnHandQuantity,
        bool expectedFreeze)
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"inventory-count-completion-{Guid.CreateVersion7():N}", databaseRoot);

        await using (var dbContext = CreateDbContext(options))
        {
            var ledger = NewLedgerWithOnHand(10m);
            var task = DomainCountTaskFactory.NewTask(ledger);
            ledger.FreezeForCount(task.CountTaskCode);
            task.SubmitForApproval(ledger, 7m);
            var adjustment = StockCountAdjustment.RecordPendingApproval(task, "idem-count-approval-001", "chain-count-001", 15m);
            dbContext.StockLedgers.Add(ledger);
            dbContext.StockCountTasks.Add(task);
            dbContext.StockCountAdjustments.Add(adjustment);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var handler = new ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment(
                new CommandExecutingSender(dbContext),
                new InMemoryIntegrationEventDeadLetterStore());
            await handler.HandleAsync(ApprovalCompletedEvent(approvalResult), CancellationToken.None);

            await using var verificationDbContext = CreateDbContext(options);
            Assert.Equal(expectedOnHandQuantity, verificationDbContext.StockLedgers.Single().OnHandQuantity);
            Assert.Equal(expectedFreeze, verificationDbContext.StockLedgers.Single().IsFrozenForCount);
            Assert.Equal(expectedTaskStatus, verificationDbContext.StockCountTasks.Single().Status);
            var persistedAdjustment = verificationDbContext.StockCountAdjustments.Single();
            Assert.Equal(expectedAdjustmentStatus, persistedAdjustment.Status);
            Assert.Equal(approvalResult == ApprovalResults.Approved, persistedAdjustment.MovementId is not null);
        }
    }

    private static StockLedger NewLedgerWithOnHand(decimal quantity)
    {
        var ledger = DomainLedgerFactory.NewLedger();
        ledger.ApplyMovement(StockMovement.Post(
            "org-001", "env-dev", "inbound", "wms", "DOC-001", "LINE-001", "idem-in-001",
            "SKU-FG-1000", "kg", "SITE-01", "LOC-A-01", "LOT-001", null, "qualified", "company", "owner-001",
            quantity, 5m));
        return ledger;
    }

    private static ApprovalCompletedIntegrationEvent ApprovalCompletedEvent(string result)
    {
        return new ApprovalCompletedIntegrationEvent(
            "evt-approval-001", ApprovalIntegrationEventTypes.ApprovalApproved, ApprovalIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-10T01:00:00Z"), ApprovalIntegrationEventSources.BusinessApproval,
            "corr-001", "cause-001", "org-001", "env-dev", "system:approval", "approval-completed:chain-count-001",
            new ApprovalCompletedPayload(
                "chain-count-001", result, "user", "u-finance", null, null,
                new ApprovalDocumentReferencePayload("inventory", "inventory-count-variance", "COUNT-001", null)));
    }

    private static ApplicationDbContext CreateDbContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static DbContextOptions<ApplicationDbContext> CreateDbContextOptions(string databaseName, InMemoryDatabaseRoot databaseRoot) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName, databaseRoot).Options;

    private sealed class CapturingStockCountApprovalClient(string chainId) : IStockCountApprovalClient
    {
        public StockCountApprovalRequest? Request { get; private set; }

        public Task<StockCountApprovalResult> StartApprovalAsync(StockCountApprovalRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new StockCountApprovalResult(chainId));
        }
    }

    private sealed class CommandExecutingSender(ApplicationDbContext dbContext) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CompleteStockCountAdjustmentApprovalCommand command)
            {
                var result = await new CompleteStockCountAdjustmentApprovalCommandHandler(dbContext).Handle(command, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                return (TResponse)(object)result;
            }

            throw new NotSupportedException($"Request type is not supported by this test sender: {request.GetType().FullName}");
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task Send<TRequest>(IRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
