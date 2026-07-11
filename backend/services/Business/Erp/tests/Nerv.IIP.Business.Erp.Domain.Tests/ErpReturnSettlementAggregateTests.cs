using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountReceivableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.CreditNoteAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;

namespace Nerv.IIP.Business.Erp.Domain.Tests;

public sealed class ErpReturnSettlementAggregateTests
{
    [Fact]
    public void Debit_note_reduces_only_open_account_payable_balance()
    {
        var payable = AccountPayable.Create(
            "org-001",
            "env-dev",
            "AP-RETURN-001",
            "INV-RETURN-001",
            "SUP-001",
            100m,
            "CNY");

        payable.ApplyDebitNote(40m);

        Assert.Equal(40m, payable.DebitNoteAmount);
        Assert.Equal(60m, payable.OpenAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() => payable.ApplyDebitNote(61m));
    }

    [Fact]
    public void Credit_note_reduces_only_open_account_receivable_balance()
    {
        var receivable = AccountReceivable.Create(
            "org-001",
            "env-dev",
            "AR-RETURN-001",
            "DO-RETURN-001",
            "CUST-001",
            100m,
            "CNY");

        receivable.ApplyCreditNote(40m);

        Assert.Equal(40m, receivable.CreditNoteAmount);
        Assert.Equal(60m, receivable.OpenAmount);
        Assert.Throws<ArgumentOutOfRangeException>(() => receivable.ApplyCreditNote(61m));
    }

    [Fact]
    public void Rma_requires_received_and_credit_eligible_quality_before_issuing_credit_note()
    {
        var rma = SalesReturnAuthorization.Authorize(
            "org-001",
            "env-dev",
            "RMA-001",
            "SO-001",
            "AR-001",
            "CUST-001",
            "SITE-01",
            "CNY",
            1m,
            [new SalesReturnAuthorizationLineDraft("LINE-001", "SKU-001", "EA", 2m, 50m, "LOC-RETURN", null)]);

        Assert.Equal(SalesReturnAuthorizationStatus.Authorized, rma.Status);
        Assert.Throws<InvalidOperationException>(() => rma.MarkCreditIssued("CN-001"));

        rma.MarkWarehouseReceived("IN-RMA-001");
        rma.ApplyQualityDisposition("passed");
        rma.MarkCreditIssued("CN-001");

        var creditNote = CreditNote.Issue(rma, "CN-001");
        Assert.Equal(SalesReturnAuthorizationStatus.CreditIssued, rma.Status);
        Assert.Equal(100m, creditNote.Amount);
        Assert.Equal("AR-001", creditNote.AccountReceivableNo);
    }
}
