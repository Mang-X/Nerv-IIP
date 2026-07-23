using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountingPeriodAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CashReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CostCandidateAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PaymentExecutionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.DomainEvents;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class ErpSalesFinanceAggregateTests
{
    [Fact]
    public void Sales_order_lifecycle_raises_versioned_release_change_and_cancel_facts()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-DEMAND-001",
            "CUST-001",
            new DateOnly(2026, 8, 1),
            [new QuotationLineDraft("10", "SKU-FG", "EA", 2m, 10m, new DateOnly(2026, 8, 15))]);
        quotation.Approve();

        var order = SalesOrder.CreateFromQuotation("SO-DEMO-001", "SITE-001", quotation);

        var released = Assert.IsType<SalesOrderReleasedDomainEvent>(Assert.Single(order.GetDomainEvents()));
        Assert.Same(order, released.SalesOrder);
        Assert.Equal(1, order.Version);
        Assert.Equal("SITE-001", order.SiteCode);

        order.ClearDomainEvents();
        order.ChangeLine("10", 3m, 10m, new DateOnly(2026, 8, 16), "customer changed quantity");
        Assert.IsType<SalesOrderChangedDomainEvent>(Assert.Single(order.GetDomainEvents()));
        Assert.Equal(2, order.Version);

        order.ClearDomainEvents();
        order.Cancel("customer cancelled order");
        Assert.IsType<SalesOrderCancelledDomainEvent>(Assert.Single(order.GetDomainEvents()));
        Assert.Equal(3, order.Version);
    }

    [Fact]
    public void Credit_held_sales_order_only_raises_release_fact_after_approval()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-DEMAND-HOLD",
            "CUST-001",
            new DateOnly(2026, 8, 1),
            [new QuotationLineDraft("10", "SKU-FG", "EA", 2m, 10m, new DateOnly(2026, 8, 15))]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation(
            "SO-DEMO-HOLD",
            "SITE-001",
            quotation,
            new CustomerCreditSnapshot("CUST-001", 1m, 0m, 0m));

        Assert.Equal("credit-held", order.Status);
        Assert.Empty(order.GetDomainEvents());

        order.ReleaseCreditHold();

        Assert.Equal(2, order.Version);
        Assert.IsType<SalesOrderReleasedDomainEvent>(Assert.Single(order.GetDomainEvents()));
    }

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

        Assert.Throws<InvalidOperationException>(() => SalesOrder.CreateFromQuotation("SO-001", "SITE-001", quotation));

        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-001", "SITE-001", quotation);

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

        var order = SalesOrder.CreateFromQuotation("SO-002", "SITE-001", quotation);

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
    public void Sales_order_credit_check_places_limit_overrun_on_hold_until_release()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-004",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)))]);
        quotation.Approve();

        var heldOrder = SalesOrder.CreateFromQuotation(
            "SO-CREDIT-001",
            "SITE-001",
            quotation,
            new CustomerCreditSnapshot("CUST-001", 25m, OpenReceivableAmount: 10m, ActiveSalesOrderExposure: 1m));
        Assert.Equal("credit-held", heldOrder.Status);
        Assert.Throws<InvalidOperationException>(() => heldOrder.RegisterDelivery("L1", 1m));
        heldOrder.ReleaseCreditHold();
        Assert.Equal("released", heldOrder.Status);

        var order = SalesOrder.CreateFromQuotation(
            "SO-CREDIT-002",
            "SITE-001",
            quotation,
            new CustomerCreditSnapshot("CUST-001", 40m, OpenReceivableAmount: 10m, ActiveSalesOrderExposure: 1m));

        Assert.Equal("released", order.Status);
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
        var order = SalesOrder.CreateFromQuotation("SO-001", "SITE-001", quotation);

        Assert.Throws<ArgumentOutOfRangeException>(() => DeliveryOrder.Release(order, "DO-001", [new DeliveryOrderLineDraft("L1", 3m)]));
    }

    [Fact]
    public void Sales_order_changes_and_cancellation_only_apply_to_unfulfilled_lines_and_release_open_exposure()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-CHANGE-001",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [
                new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))),
                new QuotationLineDraft("L2", "SKU-PKG", "ea", 3m, 4m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20))),
            ]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-CHANGE-001", "SITE-001", quotation);

        order.ChangeLine("L1", 4m, 11m, new DateOnly(2026, 7, 1), "customer change");
        order.CancelLine("L2", "customer removed line");

        Assert.Equal(44m, order.OpenExposureAmount);
        Assert.Equal(44m, order.TotalAmount);
        Assert.Equal(3, order.Version);
        Assert.Equal(2, order.ChangeHistory.Count);
        Assert.True(order.Lines.Single(x => x.LineNo == "L2").Cancelled);

        order.RegisterDelivery("L1", 1m);
        Assert.Throws<InvalidOperationException>(() => order.ChangeLine("L1", 3m, 11m, new DateOnly(2026, 7, 2), "late change"));
        Assert.Throws<InvalidOperationException>(() => order.Cancel("cannot cancel shipped order"));
    }

    [Fact]
    public void Delivery_order_lines_keep_wms_outbound_dimensions_from_sales_order()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-005",
            "CUST-001",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            [new QuotationLineDraft("L1", "SKU-FG", "ea", 2m, 10m, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)))]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-005", "SITE-001", quotation);

        var delivery = DeliveryOrder.Release(order, "DO-005", [new DeliveryOrderLineDraft("L1", 1m, "FG-SHIP", "LOT-FG-001")]);

        var siteCode = typeof(DeliveryOrder).GetProperty("SiteCode");
        var line = Assert.Single(delivery.Lines);
        Assert.NotNull(siteCode);
        Assert.Equal("SITE-001", siteCode.GetValue(delivery));
        Assert.Equal("SKU-FG", line.SkuCode);
        Assert.Equal("ea", line.UomCode);
        Assert.Equal("FG-SHIP", line.LocationCode);
        Assert.Equal("LOT-FG-001", line.LotNo);
    }

    [Fact]
    public void Delivery_order_accumulates_partial_shipments_before_completing_all_lines()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-SHIP-001",
            "CUST-001",
            new DateOnly(2026, 8, 1),
            [
                new QuotationLineDraft("10", "SKU-A", "EA", 3m, 10m, new DateOnly(2026, 8, 15)),
                new QuotationLineDraft("20", "SKU-B", "EA", 2m, 20m, new DateOnly(2026, 8, 15)),
            ]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-SHIP-001", "SITE-001", quotation);
        var delivery = DeliveryOrder.Release(
            order,
            "DO-SHIP-001",
            [new DeliveryOrderLineDraft("10", 3m), new DeliveryOrderLineDraft("20", 2m)]);
        var firstShipmentAtUtc = new DateTime(2026, 7, 20, 1, 2, 3, DateTimeKind.Utc);

        var firstCompleted = delivery.ApplyShipment(
            [new DeliveryOrderShipmentLine("10", 1m), new DeliveryOrderShipmentLine("20", 2m)],
            firstShipmentAtUtc);

        Assert.False(firstCompleted);
        Assert.Equal("partially-shipped", delivery.Status);
        Assert.Equal(firstShipmentAtUtc, delivery.ShippedAtUtc);
        Assert.Null(delivery.CompletedAtUtc);
        Assert.Equal(1m, delivery.Lines.Single(x => x.SalesOrderLineNo == "10").ShippedQuantity);
        Assert.Equal(2m, delivery.Lines.Single(x => x.SalesOrderLineNo == "20").ShippedQuantity);

        var completedAtUtc = firstShipmentAtUtc.AddMinutes(5);
        var completed = delivery.ApplyShipment([new DeliveryOrderShipmentLine("10", 2m)], completedAtUtc);

        Assert.True(completed);
        Assert.Equal("completed", delivery.Status);
        Assert.Equal(firstShipmentAtUtc, delivery.ShippedAtUtc);
        Assert.Equal(completedAtUtc, delivery.CompletedAtUtc);
        Assert.All(delivery.Lines, line => Assert.Equal(line.Quantity, line.ShippedQuantity));
    }

    [Fact]
    public void Delivery_order_rejects_duplicate_unknown_or_excess_shipment_lines_without_mutation()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-SHIP-INVALID",
            "CUST-001",
            new DateOnly(2026, 8, 1),
            [new QuotationLineDraft("10", "SKU-A", "EA", 2m, 10m, new DateOnly(2026, 8, 15))]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-SHIP-INVALID", "SITE-001", quotation);
        var delivery = DeliveryOrder.Release(order, "DO-SHIP-INVALID", [new DeliveryOrderLineDraft("10", 2m)]);
        var shippedAtUtc = new DateTime(2026, 7, 20, 1, 2, 3, DateTimeKind.Utc);

        Assert.Throws<InvalidOperationException>(() => delivery.ApplyShipment(
            [new DeliveryOrderShipmentLine("10", 1m), new DeliveryOrderShipmentLine("10", 1m)],
            shippedAtUtc));
        Assert.Throws<InvalidOperationException>(() => delivery.ApplyShipment(
            [new DeliveryOrderShipmentLine("missing", 1m)],
            shippedAtUtc));
        Assert.Throws<InvalidOperationException>(() => delivery.ApplyShipment(
            [new DeliveryOrderShipmentLine("10", 3m)],
            shippedAtUtc));

        Assert.Equal("released", delivery.Status);
        Assert.Null(delivery.ShippedAtUtc);
        Assert.Null(delivery.CompletedAtUtc);
        Assert.Equal(0m, Assert.Single(delivery.Lines).ShippedQuantity);
    }

    [Fact]
    public void Delivery_order_cannot_be_cancelled_after_any_shipment()
    {
        var quotation = Quotation.Create(
            "org-001",
            "env-dev",
            "QT-SHIP-CANCEL",
            "CUST-001",
            new DateOnly(2026, 8, 1),
            [new QuotationLineDraft("10", "SKU-A", "EA", 2m, 10m, new DateOnly(2026, 8, 15))]);
        quotation.Approve();
        var order = SalesOrder.CreateFromQuotation("SO-SHIP-CANCEL", "SITE-001", quotation);
        var delivery = DeliveryOrder.Release(order, "DO-SHIP-CANCEL", [new DeliveryOrderLineDraft("10", 2m)]);
        delivery.ApplyShipment(
            [new DeliveryOrderShipmentLine("10", 1m)],
            new DateTime(2026, 7, 20, 1, 2, 3, DateTimeKind.Utc));

        Assert.Throws<InvalidOperationException>(() => delivery.Cancel(
            "too late",
            new DateTime(2026, 7, 20, 1, 3, 3, DateTimeKind.Utc)));
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
    public void Journal_voucher_lines_capture_currency_rate_local_amounts_and_balance_in_local_currency()
    {
        var voucher = JournalVoucher.Post(
            "org-001",
            "env-dev",
            "JV-FX-001",
            new DateOnly(2026, 6, 20),
            [
                new JournalVoucherLineDraft("2202", 100m, 0m, "clear USD AP", "USD", 7.1m),
                new JournalVoucherLineDraft("BANK-USD", 0m, 100m, "pay USD cash", "USD", 7.2m),
                new JournalVoucherLineDraft("6603", 10m, 0m, "realized FX loss", "CNY", 1m),
            ]);

        Assert.Equal(720m, voucher.Lines.Sum(x => x.LocalDebitAmount));
        Assert.Equal(720m, voucher.Lines.Sum(x => x.LocalCreditAmount));
        Assert.Contains(voucher.Lines, x => x.AccountCode == "2202" && x.CurrencyCode == "USD" && x.ExchangeRate == 7.1m && x.LocalDebitAmount == 710m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "BANK-USD" && x.CurrencyCode == "USD" && x.ExchangeRate == 7.2m && x.LocalCreditAmount == 720m);
        Assert.Contains(voucher.Lines, x => x.AccountCode == "6603" && x.CurrencyCode == "CNY" && x.LocalDebitAmount == 10m);
    }

    [Fact]
    public void Payable_and_receivable_track_open_amount_after_partial_settlement()
    {
        var payable = AccountPayable.Create(
            "org-001",
            "env-dev",
            "AP-002",
            "RCV-002",
            "SUP-001",
            100m,
            "cny",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 7, 1),
            "NET30");
        var receivable = AccountReceivable.Create(
            "org-001",
            "env-dev",
            "AR-002",
            "DO-002",
            "CUST-001",
            80m,
            "usd",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 15),
            "NET14");

        payable.RegisterPayment(40m);
        receivable.RegisterCollection(35m);

        Assert.Equal(60m, payable.OpenAmount);
        Assert.Equal(45m, receivable.OpenAmount);
        Assert.Equal("CNY", payable.CurrencyCode);
        Assert.Equal("USD", receivable.CurrencyCode);
        Assert.Equal(new DateOnly(2026, 7, 1), payable.DueDate);
        Assert.Equal(new DateOnly(2026, 6, 15), receivable.DueDate);
        Assert.Equal("current", payable.GetAgingBucket(new DateOnly(2026, 6, 30)));
        Assert.Equal("1-30", receivable.GetAgingBucket(new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void Payable_tracks_local_amount_at_document_exchange_rate()
    {
        var payable = AccountPayable.Create(
            "org-001",
            "env-dev",
            "AP-USD-001",
            "INV-USD-001",
            "SUP-001",
            100m,
            "usd",
            exchangeRate: 7.1m);

        Assert.Equal("USD", payable.CurrencyCode);
        Assert.Equal(7.1m, payable.ExchangeRate);
        Assert.Equal(710m, payable.LocalAmount);
        Assert.Equal(710m, payable.LocalOpenAmount);
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

    [Fact]
    public void Accounting_period_closes_reopens_and_tests_posting_date()
    {
        var period = AccountingPeriod.Open(
            "org-001",
            "env-dev",
            "2026-06",
            new DateOnly(2026, 6, 1),
            new DateOnly(2026, 6, 30));

        Assert.True(period.Contains(new DateOnly(2026, 6, 15)));

        period.Close("u-finance", "month close reviewed");

        Assert.Equal(AccountingPeriodStatus.Closed, period.Status);
        Assert.False(period.CanPost);
        Assert.Throws<InvalidOperationException>(() => period.Close("u-finance", "again"));

        period.Reopen("u-controller", "late WMS receipt exception");

        Assert.Equal(AccountingPeriodStatus.Open, period.Status);
        Assert.Equal("u-controller", period.ReopenedBy);
        Assert.Equal("late WMS receipt exception", period.ReopenReason);
    }

    [Fact]
    public void Payment_execution_requires_approval_before_execution_and_records_allocations()
    {
        var payable = AccountPayable.Create(
            "org-001",
            "env-dev",
            "AP-1001",
            "INV-1001",
            "SUP-001",
            100m,
            "CNY");
        var execution = PaymentExecution.Approve(
            "org-001",
            "env-dev",
            "PAY-1001",
            "SUP-001",
            40m,
            "CNY",
            new DateOnly(2026, 6, 20),
            "BANK-001",
            "u-ap");

        execution.Execute([new PaymentExecutionAllocationDraft(payable.PayableNo, 40m)], "u-cashier");
        payable.RegisterPayment(40m);

        Assert.Equal(PaymentExecutionStatus.Executed, execution.Status);
        Assert.Equal(40m, Assert.Single(execution.Allocations).Amount);
        Assert.Equal(60m, payable.OpenAmount);
        Assert.Throws<InvalidOperationException>(() => execution.Execute([new PaymentExecutionAllocationDraft(payable.PayableNo, 1m)], "u-cashier"));
    }

    [Fact]
    public void Cash_receipt_records_partial_receivable_matching()
    {
        var receivable = AccountReceivable.Create(
            "org-001",
            "env-dev",
            "AR-1001",
            "DO-1001",
            "CUST-001",
            80m,
            "CNY",
            dueDate: new DateOnly(2026, 6, 15));
        var receipt = CashReceipt.Register(
            "org-001",
            "env-dev",
            "CR-1001",
            "CUST-001",
            35m,
            "CNY",
            new DateOnly(2026, 6, 21),
            "BANK-001");

        receipt.Match([new CashReceiptAllocationDraft(receivable.ReceivableNo, 35m)]);
        receivable.RegisterCollection(35m);

        Assert.Equal(CashReceiptStatus.Matched, receipt.Status);
        Assert.Equal(35m, Assert.Single(receipt.Allocations).Amount);
        Assert.Equal(45m, receivable.OpenAmount);
        Assert.Equal("1-30", receivable.GetAgingBucket(new DateOnly(2026, 7, 1)));
    }
}
