using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;

namespace Nerv.IIP.Business.Erp.Web.Tests;

public sealed class ErpCommandValidatorTests
{
    [Fact]
    public void Procurement_commands_reject_empty_or_non_positive_input()
    {
        AssertInvalid(new CreateRequestForQuotationCommandValidator().Validate(new CreateRequestForQuotationCommand(
            "", "env-dev", "", [], [])));
        AssertInvalid(new ReceiveSupplierQuotationCommandValidator().Validate(new ReceiveSupplierQuotationCommand(
            "org-001", "env-dev", "", "", "", [new SupplierQuotationCommandLine("", "", "", 0m, 0m, new DateOnly(2026, 6, 1))])));
        AssertInvalid(new CreatePurchaseOrderCommandValidator().Validate(new CreatePurchaseOrderCommand(
            "org-001", "env-dev", "", "", "", [new PurchaseOrderCommandLine("", "", "", 0m, 0m, new DateOnly(2026, 6, 1))])));
        AssertInvalid(new RecordPurchaseReceiptCommandValidator().Validate(new RecordPurchaseReceiptCommand(
            "org-001", "env-dev", "", "", [new PurchaseReceiptCommandLine("", 0m, "")])));
    }

    [Fact]
    public void Sales_commands_reject_empty_or_non_positive_input()
    {
        AssertInvalid(new OpenOpportunityCommandValidator().Validate(new OpenOpportunityCommand("", "env-dev", "", "", "")));
        AssertInvalid(new CreateQuotationCommandValidator().Validate(new CreateQuotationCommand(
            "org-001", "env-dev", "", "", new DateOnly(2026, 6, 1), [new QuotationCommandLine("", "", "", 0m, 0m, new DateOnly(2026, 6, 1))])));
        AssertInvalid(new ApproveQuotationCommandValidator().Validate(new ApproveQuotationCommand("", "", "")));
        AssertInvalid(new CreateSalesOrderCommandValidator().Validate(new CreateSalesOrderCommand("org-001", "env-dev", "", "", "")));
        AssertInvalid(new ReleaseDeliveryOrderCommandValidator().Validate(new ReleaseDeliveryOrderCommand(
            "org-001", "env-dev", "", "", [new DeliveryOrderCommandLine("", 0m)])));
    }

    [Fact]
    public void Sales_order_requires_an_authoritative_site_for_demand_planning()
    {
        var validator = new CreateSalesOrderCommandValidator();

        AssertInvalid(validator.Validate(new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001", "")));
        Assert.True(validator.Validate(new CreateSalesOrderCommand("org-001", "env-dev", "SO-001", "QT-001", "SITE-001")).IsValid);
    }

    [Fact]
    public void Finance_commands_reject_empty_or_non_positive_input()
    {
        AssertInvalid(new CreateAccountPayableCommandValidator().Validate(new CreateAccountPayableCommand("", "env-dev", "", "", "", 0m, "")));
        AssertInvalid(new CreateAccountReceivableCommandValidator().Validate(new CreateAccountReceivableCommand("", "env-dev", "", "", "", 0m, "")));
        AssertInvalid(new CreateCostCandidateCommandValidator().Validate(new CreateCostCandidateCommand("", "env-dev", "", "", "", 0m, "")));
        AssertInvalid(new PostJournalVoucherCommandValidator().Validate(new PostJournalVoucherCommand(
            "org-001",
            "env-dev",
            "",
            new DateOnly(2026, 6, 1),
            [new JournalVoucherCommandLine("", 0m, 0m, "")])));
    }

    private static void AssertInvalid(FluentValidation.Results.ValidationResult result)
    {
        Assert.False(result.IsValid);
    }
}
