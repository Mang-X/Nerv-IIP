using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.GLAccountAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReturnAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.WorkOrderCostAggregate;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Erp.Web.Tests;

/// <summary>
/// GitHub #3278 / S2：来源两列**填的是什么**。
///
/// <see cref="JournalVoucherSourceContractTests"/> 管的是「填没填」——那是编译期结构性闭合，
/// 覆盖全部 12 族。但**编译期保证不了填对**：把所有位点的 <c>sourceNo</c> 一律填成同一个常量，
/// 那套装置全绿（复审实测 MUT-B）。S5 要在这两列上建唯一键，填错会在那时以最难诊断的形式炸，
/// 所以本类逐族把生产侧**实际取的值**钉住。
///
/// <b>覆盖清单（12 族全覆盖，逐族点名取值来源）</b>：
/// <list type="bullet">
/// <item><c>AP</c> / <c>SUPPINV</c> / <c>AR</c> / <c>COST</c> / <c>APPAY</c> / <c>ARCOL</c> /
///   <c>GRIR</c> / <c>PRTN</c> / <c>CN</c> —— 本类第一条用例，直接对撞 <c>FinanceVoucherFactory</c>；</item>
/// <item><c>WOCADJ</c> —— 本类第二条，直接调 <c>CostVariancePosting.PostLateAdjustmentAsync</c>（即 :332 位点本身）；</item>
/// <item><c>MANUAL</c> —— 本类第三条，跑 <c>PostJournalVoucherCommandHandler</c>（即 :810 位点）；</item>
/// <item><c>WOC</c> —— 不在本类，在 <c>WorkOrderCostEventClosureTests</c>
///   （资本化凭证在集成事件消费者里内联构造，只有跑完那条链路才拿得到）。</item>
/// </list>
///
/// <b>值域边界</b>：本类钉的是「工厂/位点返回的聚合上的两列取值」，**不是**「这些值在真库里唯一」——
/// 唯一性属 #3278 / S5，本票不建唯一索引。
/// </summary>
public sealed class JournalVoucherSourceValueTests
{
    private const string OrganizationId = "org-src";
    private const string EnvironmentId = "env-src";
    private static readonly DateOnly PostingDate = new(2026, 9, 14);

    /// <summary>
    /// 九个命令侧工厂各自盖上**驱动这张凭证的那张单据**，而不是凭证号、也不是同一个占位串。
    ///
    /// ⭐ 其中 <c>AP</c> 与 <c>SUPPINV</c> 是 owner 在 #3278 §A2 点名的设计地雷：
    /// <c>ForAccountPayable</c> 与 <c>ForSupplierInvoiceGrIrClearing</c> 产出**同一个**凭证号
    /// <c>JV-AP-{应付单号}</c>。这里断言两者的 <c>(SourceType, SourceNo)</c> 各自取自己的驱动单据，
    /// 所以 S5 建唯一键时这两条路径不会互相挡住。
    /// **这条断言必须对着工厂的实际返回值，不能只比两个码常量不等**——那样把地雷装回去也不会红。
    /// </summary>
    [Fact]
    public void Every_command_side_factory_stamps_its_own_driving_document()
    {
        var payable = AccountPayable.Create(OrganizationId, EnvironmentId, "AP-0001", "SRC-DOC-0001", "SUP-001", 100m, "CNY", PostingDate, PostingDate);
        var receivable = AccountReceivable.Create(OrganizationId, EnvironmentId, "AR-0001", "SRC-DOC-0002", "CUST-001", 100m, "CNY", PostingDate, PostingDate);
        var candidate = CostCandidate.Create(OrganizationId, EnvironmentId, "COST-0001", "mes-report", "RPT-0001", 100m, "CNY");
        var purchaseOrder = PurchaseOrder.Create(
            OrganizationId,
            EnvironmentId,
            "PO-0001",
            "SUP-001",
            "SITE-001",
            [new PurchaseOrderLineDraft("L1", "SKU-RM-001", "EA", 10m, 10m, PostingDate)]);
        purchaseOrder.MarkApprovalRequested("chain-0001");
        purchaseOrder.ReleaseAfterApproval("chain-0001");
        var receipt = PurchaseReceipt.Record(purchaseOrder, "RCV-0001", [new PurchaseReceiptLineDraft("L1", 10m, "accepted")]);
        var invoice = SupplierInvoice.Match(
            purchaseOrder,
            receipt,
            "INV-0001",
            PostingDate,
            PostingDate,
            "CNY",
            0m,
            0m,
            [new SupplierInvoiceLineDraft("L1", "L1", 10m, 10m)]);
        var purchaseReturn = PurchaseReturn.Record(
            OrganizationId,
            EnvironmentId,
            "PRTN-0001",
            "RCV-0001",
            "WMS-OUT-0001",
            "SUP-001",
            "CNY",
            1m,
            [new PurchaseReturnLineDraft("L1", "SKU-RM-001", "EA", 2m, 10m, 1m, 1m)]);
        var rma = SalesReturnAuthorization.Authorize(
            OrganizationId,
            EnvironmentId,
            "RMA-0001",
            "SO-0001",
            "AR-0001",
            "CUST-001",
            "SITE-001",
            "CNY",
            1m,
            [new SalesReturnAuthorizationLineDraft("L1", "SKU-FG-001", "EA", 1m, 50m, "receiving", null)]);
        rma.MarkWarehouseReceived("WMS-IN-0001");
        rma.ApplyQualityDisposition("passed");
        rma.MarkCreditIssued("CN-0001");
        var creditNote = CreditNote.Issue(rma, "CN-0001");

        var expectations = new (string Family, JournalVoucher Voucher, string Type, string No)[]
        {
            ("AP", FinanceVoucherFactory.ForAccountPayable(payable), "AP", "AP-0001"),
            ("SUPPINV", FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing(invoice, payable, 1m), "SUPPINV", "INV-0001"),
            ("AR", FinanceVoucherFactory.ForAccountReceivable(receivable), "AR", "AR-0001"),
            ("COST", FinanceVoucherFactory.ForCostCandidate(candidate), "COST", "COST-0001"),
            ("APPAY", FinanceVoucherFactory.ForPayablePayment([new PayablePaymentVoucherAllocation(payable, 100m)], "JV-PAY-0001", "APPAY-0001", 100m, "CNY", 1m, PostingDate, "1002"), "APPAY", "APPAY-0001"),
            ("ARCOL", FinanceVoucherFactory.ForReceivableCollection(receivable, "JV-COL-0001", "ARCOL-0001", 100m, PostingDate, "1002"), "ARCOL", "ARCOL-0001"),
            ("GRIR", FinanceVoucherFactory.ForGoodsReceiptIrAccrual(receipt, 100m, "JV-GRIR-RCV-0001"), "GRIR", "RCV-0001"),
            ("PRTN", FinanceVoucherFactory.ForPurchaseReturn(purchaseReturn, "JV-PRTN-PRTN-0001", PostingDate), "PRTN", "PRTN-0001"),
            ("CN", FinanceVoucherFactory.ForCreditNote(creditNote, PostingDate), "CN", "CN-0001"),
        };

        Assert.Equal(9, expectations.Length);
        foreach (var expectation in expectations)
        {
            Assert.Equal(
                (expectation.Family, expectation.Type, expectation.No),
                (expectation.Family, expectation.Voucher.SourceType, expectation.Voucher.SourceNo));
        }

        // 两条 JV-AP-{应付单号} 同号路径：凭证号相同，来源单据必须不同——否则 S5 的唯一键会把它们互相挡住。
        var directPayable = FinanceVoucherFactory.ForAccountPayable(payable);
        var invoiceClearing = FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing(invoice, payable, 1m);
        Assert.Equal(directPayable.VoucherNo, invoiceClearing.VoucherNo);
        Assert.NotEqual(
            (directPayable.SourceType, directPayable.SourceNo),
            (invoiceClearing.SourceType, invoiceClearing.SourceNo));
    }

    /// <summary>
    /// <c>WOCADJ</c>：迟到成本调整凭证的来源取**触发这次调整的来源标识**（报工单号 / 移动号 /
    /// 工序任务修订串），不是工单号。直接调 <c>CostVariancePosting.PostLateAdjustmentAsync</c>——
    /// 那就是 <c>WorkOrderCostIntegrationEventHandlers.cs</c> 里那个建凭证位点本身。
    /// </summary>
    [Fact]
    public async Task Late_cost_adjustment_stamps_the_triggering_source_id()
    {
        await using var db = CreateInMemoryDbContext();
        var cost = WorkOrderCost.Open(OrganizationId, EnvironmentId, "WO-0001", "FG-0001");
        cost.RecordLabor("RPT-0001", "WC-001", 2m, 50m, "CNY", false, DateTimeOffset.UtcNow);
        db.WorkOrderCosts.Add(cost);
        await db.SaveChangesAsync();

        await CostVariancePosting.PostLateAdjustmentAsync(
            db,
            cost,
            -60m,
            "machine-OPT-0001-r7-void",
            DateTimeOffset.Parse("2026-09-14T00:00:00Z"),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var voucher = await db.JournalVouchers.SingleAsync();
        Assert.Equal(JournalVoucherSourceType.WorkOrderCostAdjustment.Code, voucher.SourceType);
        Assert.Equal("machine-OPT-0001-r7-void", voucher.SourceNo);
    }

    /// <summary>
    /// <c>MANUAL</c>：手工凭证没有上游单据，来源单号取分配器给的凭证号自身。
    /// 跑真命令处理器，不是直接调 <c>JournalVoucher.Post</c>——后者证明不了这个位点填了什么。
    /// </summary>
    [Fact]
    public async Task Manual_posting_stamps_itself_as_its_own_source()
    {
        await using var provider = ErpTestProvider.CreateInMemoryProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<Infrastructure.ApplicationDbContext>();

        await new PostJournalVoucherCommandHandler(dbContext).Handle(
            new PostJournalVoucherCommand(
                OrganizationId,
                EnvironmentId,
                "JV-MANUAL-0001",
                PostingDate,
                [
                    new JournalVoucherCommandLine("1401", 100m, 0m, "inventory"),
                    new JournalVoucherCommandLine("2202", 0m, 100m, "payable"),
                ]),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var voucher = await dbContext.JournalVouchers.SingleAsync();
        Assert.Equal(JournalVoucherSourceType.Manual.Code, voucher.SourceType);
        Assert.Equal(voucher.VoucherNo, voucher.SourceNo);
        Assert.Equal("JV-MANUAL-0001", voucher.SourceNo);
    }

    private static Infrastructure.ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<Infrastructure.ApplicationDbContext>()
            .UseInMemoryDatabase($"erp-voucher-source-{Guid.CreateVersion7():N}")
            .Options;
        return new Infrastructure.ApplicationDbContext(options, new SourceValueNoopMediator());
    }

    private sealed class SourceValueNoopMediator : MediatR.IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : MediatR.INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : MediatR.IRequest => throw new NotSupportedException("This test mediator only supports publish.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test mediator only supports publish.");
    }
}
