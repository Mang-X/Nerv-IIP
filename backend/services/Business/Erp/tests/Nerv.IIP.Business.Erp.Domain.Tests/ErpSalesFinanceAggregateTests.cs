using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class ErpSalesFinanceAggregateTests
{
    [Fact]
    public void Opportunity_requires_customer_reference_and_topic()
    {
        Assert.Throws<ArgumentException>(() => Opportunity.Open("org-001", "env-dev", "OPP-001", "", "Line expansion"));
        Assert.Throws<ArgumentException>(() => Opportunity.Open("org-001", "env-dev", "OPP-001", "CUST-001", ""));
    }

    [Fact]
    public void Quotation_must_be_approved_before_sales_order()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-001",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)))]);

        Assert.Throws<InvalidOperationException>(() => SalesOrder.CreateFromQuotation("SO-001", quotation));

        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-001", quotation);

        Assert.Equal(20m, order.TotalAmount);
    }

    [Fact]
    public void Quotation_calculates_total_and_tracks_approval_state()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-002",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [
                new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))),
                new QuotationLineDraft("L2", "SKU-PKG", "ea", 3m, 4m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))),
            ]);

        Assert.Equal(QuotationStatus.Draft, quotation.Status);
        Assert.Equal(32m, quotation.TotalAmount);

        quotation.Approve();

        Assert.Equal(QuotationStatus.Approved, quotation.Status);
        Assert.Throws<InvalidOperationException>(() => quotation.Approve());
    }

    [Fact]
    public void Sales_order_copies_approved_quotation_lines_and_total()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-003",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [
                new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))),
                new QuotationLineDraft("L2", "SKU-PKG", "ea", 3m, 4m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))),
            ]);
        quotation.Approve();

        var order = SalesOrder.CreateFromQuotation("SO-002", quotation);

        Assert.Equal("released", order.Status);
        Assert.Equal(32m, order.TotalAmount);
        Assert.Collection(
            order.Lines.OrderBy(x => x.LineNo),
            line =>
            {
                Assert.Equal("L1", line.LineNo);
                Assert.Equal(2m, line.OrderedQuantity);
                Assert.Equal(20m, line.LineAmount);
            },
            line =>
            {
                Assert.Equal("L2", line.LineNo);
                Assert.Equal(3m, line.OrderedQuantity);
                Assert.Equal(12m, line.LineAmount);
            });
    }

    [Fact]
    public void Delivery_order_cannot_exceed_sales_order_open_quantity()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-001",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)))]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-001", quotation);

        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryOrder.Release(order, "DO-001", [new DeliveryOrderLineDraft("L1", 3m)]));
    }

    [Fact]
    public void Finance_rejects_overpayment_and_unbalanced_vouchers()
    {
        var payable = AccountPayable.Create("org-001", "env-dev", "AP-001", "RCV-001", "SUP-001", 100m, "cny");
        Assert.Throws<ArgumentOutOfRangeException>(() => payable.RegisterPayment(101m));

        var receivable = AccountReceivable.Create("org-001", "env-dev", "AR-001", "DO-001", "CUST-001", 80m, "cny");
        Assert.Throws<ArgumentOutOfRangeException>(() => receivable.RegisterCollection(81m));

        Assert.Throws<InvalidOperationException>(() => JournalVoucher.Post(
            "org-001",
            "env-dev",
            "JV-001",
            DateOnly.FromDateTime(DateTime.UtcNow),
            [
                new JournalVoucherLineDraft("1401", 100m, 0m, "debit"),
                new JournalVoucherLineDraft("2202", 0m, 99m, "credit"),
            ]));
    }

    [Fact]
    public void Payable_and_receivable_track_open_amount_after_partial_settlement()
    {
        var payable = AccountPayable.Create("org-001", "env-dev", "AP-002", "RCV-002", "SUP-001", 100m, "cny");
        var receivable = AccountReceivable.Create("org-001", "env-dev", "AR-002", "DO-002", "CUST-001", 80m, "usd");

        payable.RegisterPayment(40m);
        receivable.RegisterCollection(35m);

        Assert.Equal(60m, payable.OpenAmount);
        Assert.Equal(45m, receivable.OpenAmount);
        Assert.Equal("CNY", payable.CurrencyCode);
        Assert.Equal("USD", receivable.CurrencyCode);
    }

    [Fact]
    public void Balanced_journal_voucher_posts_event_and_is_immutable()
    {
        var voucher = JournalVoucher.Post(
            "org-001",
            "env-dev",
            "JV-002",
            DateOnly.FromDateTime(DateTime.UtcNow),
            [
                new JournalVoucherLineDraft("1401", 100m, 0m, "debit"),
                new JournalVoucherLineDraft("2202", 0m, 100m, "credit"),
            ]);

        Assert.Equal(100m, voucher.Lines.Sum(x => x.DebitAmount));
        Assert.Equal(100m, voucher.Lines.Sum(x => x.CreditAmount));
        Assert.Single(voucher.GetDomainEvents());
        Assert.Throws<InvalidOperationException>(() => voucher.Amend());
    }

    [Fact]
    public void Cost_candidate_requires_public_source_fact()
    {
        Assert.Throws<ArgumentException>(() => CostCandidate.Create("org-001", "env-dev", "COST-001", "", "MOVE-001", 10m, "CNY"));
        var candidate = CostCandidate.Create("org-001", "env-dev", "COST-001", "inventory-movement", "MOVE-001", 10m, "CNY");
        Assert.Equal("inventory-movement", candidate.SourceType);
    }
}
