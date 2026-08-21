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
using NetCorePal.Extensions.Primitives;

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

        // #1344 三方漂移契约（Inventory 发起侧）：默认模板码 / 单据类型必须逐字等于审批契约常量，
        // 且该模板由审批种子补齐落库；此前默认 COUNT-VARIANCE 在种子里根本不存在 → 盘点确认必 400。
        Assert.Equal(ApprovalTemplateCodes.StockCountVariance, approvalClient.Request!.TemplateCode);
        Assert.Equal("APT-WB-CNT-001", approvalClient.Request.TemplateCode);
        // #1702 三方漂移契约（Inventory 发起侧）：来源服务同样必须逐字等于审批契约常量——
        // 它是回写消费侧的分流依据之一，漂了就静默丢事件。
        Assert.Equal(ApprovalSourceServices.Inventory, approvalClient.Request.SourceService);
        Assert.Equal("inventory", approvalClient.Request.SourceService);
        Assert.Equal(ApprovalDocumentTypes.StockCountVariance, approvalClient.Request.DocumentType);
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

    [Fact]
    public async Task Cancelling_a_pending_approval_task_is_rejected_so_the_approval_write_back_never_poisons()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = CreateDbContextOptions($"inventory-count-cancel-pending-{Guid.CreateVersion7():N}", databaseRoot);

        await using var dbContext = CreateDbContext(options);
        var ledger = NewLedgerWithOnHand(10m);
        var task = DomainCountTaskFactory.NewTask(ledger);
        ledger.FreezeForCount(task.CountTaskCode);
        task.SubmitForApproval(ledger, 7m);
        var adjustment = StockCountAdjustment.RecordPendingApproval(task, "idem-count-approval-001", "chain-count-001", 15m);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockCountTasks.Add(task);
        dbContext.StockCountAdjustments.Add(adjustment);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // 关闭出口必须与重盘守卫同口径：待审批先走完审批。放行的话审批链还在跑，
        // 回写命令随后踩到 EnsureStatus(pending-approval) 抛异常，从 CAP 消费者逃逸成毒消息。
        var exception = await Assert.ThrowsAsync<KnownException>(() =>
            new CancelStockCountTaskCommandHandler(dbContext).Handle(
                new CancelStockCountTaskCommand(task.Id, "盘点范围调整"),
                CancellationToken.None));
        Assert.Contains("状态不支持取消", exception.Message, StringComparison.Ordinal);
        Assert.Equal(StockCountTaskStatuses.PendingApproval, dbContext.StockCountTasks.Single().Status);
        Assert.True(dbContext.StockLedgers.Single().IsFrozenForCount);

        // 审批回写照常落地，没有异常逃逸到消费者。
        var handler = new ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment(
            new CommandExecutingSender(dbContext),
            new InMemoryIntegrationEventDeadLetterStore());
        await handler.HandleAsync(ApprovalCompletedEvent(ApprovalResults.Rejected), CancellationToken.None);

        await using var verificationDbContext = CreateDbContext(options);
        Assert.Equal(StockCountTaskStatuses.RecountRequired, verificationDbContext.StockCountTasks.Single().Status);
        Assert.Equal(StockCountAdjustmentStatuses.Voided, verificationDbContext.StockCountAdjustments.Single().Status);
        Assert.False(verificationDbContext.StockLedgers.Single().IsFrozenForCount);

        // 审批走完之后，这张任务既可以重盘，也可以关闭——出口没有被守卫堵死。
        var restarted = await new RestartStockCountTaskCommandHandler(verificationDbContext).Handle(
            new RestartStockCountTaskCommand(task.Id),
            CancellationToken.None);
        Assert.Equal(StockCountTaskStatuses.Open, restarted.Status);
    }

    /// <summary>
    /// #1702 回写侧成对用例（正路径）：单据引用的来源服务取审批契约唯一事实来源
    /// <see cref="ApprovalSourceServices.Inventory"/> 时，盘点调整**必须真的落库**——
    /// 调整单 posted、账面从 10 走到 7、冻结解除、流水生成，而不是在来源服务分流处静默 return。
    /// 与下一条事故形态用例成对存在：单独看这条绿是看不出断言有没有鉴别力的。
    /// </summary>
    [Fact]
    public async Task Approval_completion_writes_back_for_the_contract_source_service()
    {
        var options = CreateDbContextOptions($"inventory-count-source-ok-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
        await using var dbContext = CreateDbContext(options);
        SeedPendingApprovalCount(dbContext);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await new ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment(
            new CommandExecutingSender(dbContext),
            deadLetters).HandleAsync(
            ApprovalCompletedEvent(ApprovalResults.Approved, ApprovalSourceServices.Inventory),
            CancellationToken.None);

        await using var verificationDbContext = CreateDbContext(options);
        var adjustment = verificationDbContext.StockCountAdjustments.Single();
        Assert.Equal(StockCountAdjustmentStatuses.Posted, adjustment.Status);
        Assert.NotNull(adjustment.MovementId);
        Assert.Equal(7m, verificationDbContext.StockLedgers.Single().OnHandQuantity);
        Assert.False(verificationDbContext.StockLedgers.Single().IsFrozenForCount);
        Assert.Equal(StockCountTaskStatuses.Confirmed, verificationDbContext.StockCountTasks.Single().Status);
        Assert.Empty(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    /// <summary>
    /// #1702 回写侧成对用例（事故形态）：来源服务写成集成事件信封的发布方标识
    /// <c>business-inventory</c>（同域最容易误用的同义词）时，回写**静默失效**——
    /// 调整单永停 pending-approval、账面不动、库存仍冻结、任务仍 pending-approval，
    /// 而且连死信都没有（无日志、无异常、无死信，走查时只能靠肉眼发现）。
    /// 本用例证明上一条的绿是「真的回写了」，而不是断言没有鉴别力。
    /// </summary>
    [Theory]
    [InlineData("business-inventory")]
    [InlineData("inventory-service")]
    public async Task Approval_completion_silently_drops_a_drifted_source_service_and_leaves_the_adjustment_pending(
        string driftedSourceService)
    {
        var options = CreateDbContextOptions($"inventory-count-source-drift-{Guid.CreateVersion7():N}", new InMemoryDatabaseRoot());
        await using var dbContext = CreateDbContext(options);
        SeedPendingApprovalCount(dbContext);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var deadLetters = new InMemoryIntegrationEventDeadLetterStore();

        await new ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment(
            new CommandExecutingSender(dbContext),
            deadLetters).HandleAsync(
            ApprovalCompletedEvent(ApprovalResults.Approved, driftedSourceService),
            CancellationToken.None);

        await using var verificationDbContext = CreateDbContext(options);
        var adjustment = verificationDbContext.StockCountAdjustments.Single();
        Assert.Equal(StockCountAdjustmentStatuses.PendingApproval, adjustment.Status);
        Assert.Null(adjustment.MovementId);
        Assert.Equal(10m, verificationDbContext.StockLedgers.Single().OnHandQuantity);
        Assert.True(verificationDbContext.StockLedgers.Single().IsFrozenForCount);
        Assert.Equal(StockCountTaskStatuses.PendingApproval, verificationDbContext.StockCountTasks.Single().Status);
        Assert.Empty(await deadLetters.ListAsync(
            ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment.ConsumerName,
            IntegrationEventDeadLetterStatus.Pending,
            CancellationToken.None));
    }

    /// <summary>盘点差异待审批的落库现场：账面 10 / 盘点 7 / 已冻结 / 调整单 pending-approval。</summary>
    private static void SeedPendingApprovalCount(ApplicationDbContext dbContext)
    {
        var ledger = NewLedgerWithOnHand(10m);
        var task = DomainCountTaskFactory.NewTask(ledger);
        ledger.FreezeForCount(task.CountTaskCode);
        task.SubmitForApproval(ledger, 7m);
        dbContext.StockLedgers.Add(ledger);
        dbContext.StockCountTasks.Add(task);
        dbContext.StockCountAdjustments.Add(
            StockCountAdjustment.RecordPendingApproval(task, "idem-count-approval-001", "chain-count-001", 15m));
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

    private static ApprovalCompletedIntegrationEvent ApprovalCompletedEvent(
        string result,
        string? documentSourceService = null)
    {
        return new ApprovalCompletedIntegrationEvent(
            "evt-approval-001", ApprovalIntegrationEventTypes.ApprovalApproved, ApprovalIntegrationEventVersions.V1,
            DateTimeOffset.Parse("2026-07-10T01:00:00Z"), ApprovalIntegrationEventSources.BusinessApproval,
            "corr-001", "cause-001", "org-001", "env-dev", "system:approval", "approval-completed:chain-count-001",
            new ApprovalCompletedPayload(
                "chain-count-001", result, "user", "u-finance", null, null,
                new ApprovalDocumentReferencePayload(
                    documentSourceService ?? ApprovalSourceServices.Inventory,
                    ApprovalDocumentTypes.StockCountVariance,
                    "COUNT-001",
                    null)));
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
