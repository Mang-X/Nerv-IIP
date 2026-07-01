using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.AccountPayableAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.JournalVoucherAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierInvoiceAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.Commands.Finance;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

public sealed record CreatePurchaseRequisitionFromSuggestionCommand(
    string OrganizationId,
    string EnvironmentId,
    string? RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate,
    string? IdempotencyKey = null) : ICommand<CreatePurchaseRequisitionFromSuggestionResult>;

public sealed record CreatePurchaseRequisitionFromSuggestionResult(
    PurchaseRequisitionId PurchaseRequisitionId,
    string RequisitionNo);

public sealed class CreatePurchaseRequisitionFromSuggestionCommandValidator : AbstractValidator<CreatePurchaseRequisitionFromSuggestionCommand>
{
    public CreatePurchaseRequisitionFromSuggestionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RequisitionNo).MaximumLength(100);
        RuleFor(x => x.SuggestionId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CreatePurchaseRequisitionFromSuggestionCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<CreatePurchaseRequisitionFromSuggestionCommand, CreatePurchaseRequisitionFromSuggestionResult>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<CreatePurchaseRequisitionFromSuggestionResult> Handle(CreatePurchaseRequisitionFromSuggestionCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "purchase-requisition",
            request.RequisitionNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.SuggestionId, request.SkuCode, request.UomCode, request.SiteCode, request.Quantity, request.RequiredDate),
            cancellationToken);
        var existing = await dbContext.PurchaseRequisitions.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && (x.SuggestionId == request.SuggestionId || x.RequisitionNo == allocation.Code),
            cancellationToken);
        if (existing is not null)
        {
            return new CreatePurchaseRequisitionFromSuggestionResult(existing.Id, existing.RequisitionNo);
        }

        var requisition = PurchaseRequisition.CreateFromSuggestion(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SuggestionId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.RequiredDate);
        dbContext.PurchaseRequisitions.Add(requisition);
        return new CreatePurchaseRequisitionFromSuggestionResult(requisition.Id, requisition.RequisitionNo);
    }
}

public sealed record RfqCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, string SiteCode, DateOnly RequiredDate);

public sealed record CreateRequestForQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string? RfqNo,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<RfqCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<RequestForQuotationId>;

public sealed class CreateRequestForQuotationCommandValidator : AbstractValidator<CreateRequestForQuotationCommand>
{
    public CreateRequestForQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RfqNo).MaximumLength(100);
        RuleFor(x => x.SupplierCodes).NotEmpty();
        RuleForEach(x => x.SupplierCodes).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.RequiredDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class CreateRequestForQuotationCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<CreateRequestForQuotationCommand, RequestForQuotationId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<RequestForQuotationId> Handle(CreateRequestForQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "request-for-quotation",
            request.RfqNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.SupplierCodes, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.RequiredDate}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.RequestForQuotations.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.RfqNo == allocation.Code,
                cancellationToken)).Id;
        }

        var rfq = RequestForQuotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SupplierCodes,
            request.Lines.Select(x => new RfqLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.SiteCode, x.RequiredDate)));
        dbContext.RequestForQuotations.Add(rfq);
        return rfq.Id;
    }
}

public sealed record SupplierQuotationCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly PromisedDate);

public sealed record ReceiveSupplierQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string? QuotationNo,
    string RfqNo,
    string SupplierCode,
    IReadOnlyCollection<SupplierQuotationCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<SupplierQuotationId>;

public sealed class ReceiveSupplierQuotationCommandValidator : AbstractValidator<ReceiveSupplierQuotationCommand>
{
    public ReceiveSupplierQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.QuotationNo).MaximumLength(100);
        RuleFor(x => x.RfqNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
            line.RuleFor(x => x.PromisedDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class ReceiveSupplierQuotationCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<ReceiveSupplierQuotationCommand, SupplierQuotationId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SupplierQuotationId> Handle(ReceiveSupplierQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "supplier-quotation",
            request.QuotationNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.RfqNo, request.SupplierCode, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.PromisedDate}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SupplierQuotations.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.QuotationNo == allocation.Code,
                cancellationToken)).Id;
        }

        var quotation = SupplierQuotation.Receive(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.RfqNo,
            request.SupplierCode,
            request.Lines.Select(x => new SupplierQuotationLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        dbContext.SupplierQuotations.Add(quotation);
        return quotation.Id;
    }
}

public sealed record PurchaseOrderCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly PromisedDate);

public sealed record CreatePurchaseOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    IReadOnlyCollection<PurchaseOrderCommandLine> Lines,
    string? IdempotencyKey = null,
    string CurrencyCode = "CNY") : ICommand<PurchaseOrderId>;

public sealed class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseOrderNo).MaximumLength(100);
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
            line.RuleFor(x => x.PromisedDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class CreatePurchaseOrderCommandHandler(
    ApplicationDbContext dbContext,
    ErpCodingService? codingService = null,
    IPurchaseOrderApprovalClient? approvalClient = null)
    : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();
    private readonly IPurchaseOrderApprovalClient _approvalClient = approvalClient ?? new GeneratedPurchaseOrderApprovalClient();

    public async Task<PurchaseOrderId> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "purchase-order",
            request.PurchaseOrderNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.SupplierCode, request.SiteCode, request.CurrencyCode, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.PromisedDate}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.PurchaseOrders.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == allocation.Code,
                cancellationToken)).Id;
        }

        var order = PurchaseOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.SupplierCode,
            request.SiteCode,
            request.CurrencyCode,
            request.Lines.Select(x => new PurchaseOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        var approvalResult = await _approvalClient.StartApprovalAsync(
            new PurchaseOrderApprovalRequest(
                request.OrganizationId,
                request.EnvironmentId,
                "erp-purchase-order-release",
                "business-erp",
                "purchase-order",
                allocation.Code,
                null,
                "system:erp",
                GeneratedPurchaseOrderApprovalClient.BuildChainId(request.OrganizationId, request.EnvironmentId, allocation.Code)),
            cancellationToken);
        order.MarkApprovalRequested(approvalResult.ChainId);
        dbContext.PurchaseOrders.Add(order);
        return order.Id;
    }

    private sealed class GeneratedPurchaseOrderApprovalClient : IPurchaseOrderApprovalClient
    {
        public Task<PurchaseOrderApprovalResult> StartApprovalAsync(PurchaseOrderApprovalRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new PurchaseOrderApprovalResult(request.ChainId));
        }

        public static string BuildChainId(string organizationId, string environmentId, string purchaseOrderNo)
        {
            var raw = $"{organizationId}:{environmentId}:{purchaseOrderNo}";
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
            return $"erp-po-approval-{hash}";
        }
    }
}

public sealed record PurchaseReceiptCommandLine(
    string PurchaseOrderLineNo,
    decimal ReceivedQuantity,
    string QualityStatus,
    string? LocationCode = null,
    string? LotNo = null,
    bool FinalDelivery = false);

public sealed record RecordPurchaseReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseReceiptNo,
    string PurchaseOrderNo,
    IReadOnlyCollection<PurchaseReceiptCommandLine> Lines,
    string? IdempotencyKey = null,
    decimal ExchangeRate = 1m) : ICommand<PurchaseReceiptId>;

public sealed class RecordPurchaseReceiptCommandValidator : AbstractValidator<RecordPurchaseReceiptCommand>
{
    public RecordPurchaseReceiptCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseReceiptNo).MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.ReceivedQuantity).GreaterThan(0);
            line.RuleFor(x => x.QualityStatus).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class RecordPurchaseReceiptCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RecordPurchaseReceiptCommand, PurchaseReceiptId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<PurchaseReceiptId> Handle(RecordPurchaseReceiptCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId, "purchase-receipt",
            request.PurchaseReceiptNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PurchaseOrderNo, request.ExchangeRate, request.Lines.Select(x => $"{x.PurchaseOrderLineNo}:{x.ReceivedQuantity}:{x.QualityStatus}:{x.FinalDelivery}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.PurchaseReceipts.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == allocation.Code,
                cancellationToken)).Id;
        }

        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == request.PurchaseOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");

        PurchaseReceipt receipt;
        try
        {
            receipt = PurchaseReceipt.Record(
                order,
                allocation.Code,
                request.Lines.Select(x => new PurchaseReceiptLineDraft(x.PurchaseOrderLineNo, x.ReceivedQuantity, x.QualityStatus, x.LocationCode, x.LotNo, x.FinalDelivery)),
                request.ExchangeRate);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            throw new KnownException(exception.Message, exception);
        }

        dbContext.PurchaseReceipts.Add(receipt);
        return receipt.Id;
    }
}

public sealed record SupplierInvoiceCommandLine(
    string PurchaseOrderLineNo,
    string PurchaseReceiptLineNo,
    decimal InvoiceQuantity,
    decimal UnitPrice);

public sealed record RecordSupplierInvoiceCommand(
    string OrganizationId,
    string EnvironmentId,
    string? InvoiceNo,
    string PurchaseOrderNo,
    string PurchaseReceiptNo,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    string CurrencyCode,
    decimal QuantityTolerance,
    decimal AmountTolerance,
    IReadOnlyCollection<SupplierInvoiceCommandLine> Lines,
    string? PayableNo = null,
    string? IdempotencyKey = null,
    decimal? PriceTolerancePercent = null,
    decimal ExchangeRate = 1m) : ICommand<SupplierInvoiceId>;

public sealed class RecordSupplierInvoiceCommandValidator : AbstractValidator<RecordSupplierInvoiceCommand>
{
    public RecordSupplierInvoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvoiceNo).MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PurchaseReceiptNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.InvoiceDate).NotEqual(default(DateOnly));
        RuleFor(x => x.DueDate).NotEqual(default(DateOnly));
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(10);
        RuleFor(x => x.ExchangeRate).GreaterThan(0);
        RuleFor(x => x.QuantityTolerance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AmountTolerance).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PriceTolerancePercent).GreaterThanOrEqualTo(0).When(x => x.PriceTolerancePercent.HasValue);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.PurchaseReceiptLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.InvoiceQuantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
        });
    }
}

public sealed class RecordSupplierInvoiceCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<RecordSupplierInvoiceCommand, SupplierInvoiceId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SupplierInvoiceId> Handle(RecordSupplierInvoiceCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "supplier-invoice",
            request.InvoiceNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.PurchaseOrderNo, request.PurchaseReceiptNo, request.InvoiceDate, request.DueDate, request.CurrencyCode, request.ExchangeRate, request.QuantityTolerance, request.AmountTolerance, request.PriceTolerancePercent, request.PayableNo, request.Lines.Select(x => $"{x.PurchaseOrderLineNo}:{x.PurchaseReceiptLineNo}:{x.InvoiceQuantity}:{x.UnitPrice}")),
            cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SupplierInvoices.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InvoiceNo == allocation.Code,
                cancellationToken)).Id;
        }

        var order = await dbContext.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == request.PurchaseOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Purchase order '{request.PurchaseOrderNo}' was not found.");
        var receipt = await dbContext.PurchaseReceipts
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == request.PurchaseReceiptNo,
                cancellationToken)
            ?? throw new KnownException($"Purchase receipt '{request.PurchaseReceiptNo}' was not found.");
        var alreadyInvoicedQuantitiesByReceiptLineNo = (await dbContext.SupplierInvoices
            .Include(x => x.Lines)
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == request.PurchaseReceiptNo
                && x.MatchStatus != SupplierInvoiceMatchStatus.Voided)
            .SelectMany(x => x.Lines)
            .ToListAsync(cancellationToken))
            .GroupBy(x => x.PurchaseReceiptLineNo, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.InvoiceQuantity), StringComparer.Ordinal);

        var invoiceLines = request.Lines.Select(x => new SupplierInvoiceLineDraft(x.PurchaseOrderLineNo, x.PurchaseReceiptLineNo, x.InvoiceQuantity, x.UnitPrice));
        var invoice = request.PriceTolerancePercent is { } priceTolerancePercent
            ? SupplierInvoice.Match(
                order,
                receipt,
                allocation.Code,
                request.InvoiceDate,
                request.DueDate,
                request.CurrencyCode,
                request.QuantityTolerance,
                request.AmountTolerance,
                priceTolerancePercent,
                invoiceLines,
                alreadyInvoicedQuantitiesByReceiptLineNo,
                request.ExchangeRate)
            : SupplierInvoice.Match(
                order,
                receipt,
                allocation.Code,
                request.InvoiceDate,
                request.DueDate,
                request.CurrencyCode,
                request.QuantityTolerance,
                request.AmountTolerance,
                invoiceLines,
                alreadyInvoicedQuantitiesByReceiptLineNo,
                request.ExchangeRate);
        dbContext.SupplierInvoices.Add(invoice);

        if (invoice.MatchStatus == SupplierInvoiceMatchStatus.PaymentHeld)
        {
            return invoice.Id;
        }

        var payableAllocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable",
            request.PayableNo,
            request.IdempotencyKey is null ? null : $"{request.IdempotencyKey}:account-payable",
            ErpCodingService.Fingerprint(invoice.InvoiceNo, invoice.SupplierCode, invoice.TotalAmount, invoice.CurrencyCode, invoice.InvoiceDate, invoice.DueDate, "MATCHED"),
            cancellationToken);
        var payable = AccountPayable.Create(
            request.OrganizationId,
            request.EnvironmentId,
            payableAllocation.Code,
            invoice.InvoiceNo,
            invoice.SupplierCode,
            invoice.TotalAmount,
            invoice.CurrencyCode,
            invoice.InvoiceDate,
            invoice.DueDate,
            "MATCHED",
            request.ExchangeRate);
        dbContext.AccountPayables.Add(payable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing(invoice, payable, receipt.ExchangeRate));
        return invoice.Id;
    }
}

public sealed record ReleaseSupplierInvoicePaymentHoldCommand(
    string OrganizationId,
    string EnvironmentId,
    string InvoiceNo,
    string? PayableNo,
    string IdempotencyKey) : ICommand<SupplierInvoiceId>;

public sealed class ReleaseSupplierInvoicePaymentHoldCommandValidator : AbstractValidator<ReleaseSupplierInvoicePaymentHoldCommand>
{
    public ReleaseSupplierInvoicePaymentHoldCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvoiceNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PayableNo).MaximumLength(100);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(150);
    }
}

public sealed class ReleaseSupplierInvoicePaymentHoldCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<ReleaseSupplierInvoicePaymentHoldCommand, SupplierInvoiceId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SupplierInvoiceId> Handle(ReleaseSupplierInvoicePaymentHoldCommand request, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.SupplierInvoices
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.InvoiceNo == request.InvoiceNo,
                cancellationToken)
            ?? throw new KnownException($"Supplier invoice '{request.InvoiceNo}' was not found.");

        var existingPayableForInvoice = await dbContext.AccountPayables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SourceDocumentNo == invoice.InvoiceNo,
            cancellationToken);
        if (existingPayableForInvoice is not null)
        {
            if (invoice.MatchStatus == SupplierInvoiceMatchStatus.PaymentHeld)
            {
                invoice.ReleasePaymentHold();
            }

            return invoice.Id;
        }

        var payableAllocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "account-payable",
            request.PayableNo,
            $"{request.IdempotencyKey}:account-payable",
            ErpCodingService.Fingerprint(invoice.InvoiceNo, invoice.SupplierCode, invoice.TotalAmount, invoice.CurrencyCode, invoice.ExchangeRate, invoice.InvoiceDate, invoice.DueDate, "HELD-RELEASE"),
            cancellationToken);
        var existingPayable = await dbContext.AccountPayables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PayableNo == payableAllocation.Code,
            cancellationToken);
        if (existingPayable is not null)
        {
            invoice.ReleasePaymentHold();
            return invoice.Id;
        }

        try
        {
            invoice.ReleasePaymentHold();
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        var receipt = await dbContext.PurchaseReceipts.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.PurchaseReceiptNo == invoice.PurchaseReceiptNo,
            cancellationToken)
            ?? throw new KnownException($"Purchase receipt '{invoice.PurchaseReceiptNo}' was not found.");
        var payable = AccountPayable.Create(
            request.OrganizationId,
            request.EnvironmentId,
            payableAllocation.Code,
            invoice.InvoiceNo,
            invoice.SupplierCode,
            invoice.TotalAmount,
            invoice.CurrencyCode,
            invoice.InvoiceDate,
            invoice.DueDate,
            "MATCHED",
            invoice.ExchangeRate);
        dbContext.AccountPayables.Add(payable);
        dbContext.JournalVouchers.Add(FinanceVoucherFactory.ForSupplierInvoiceGrIrClearing(invoice, payable, receipt.ExchangeRate));
        return invoice.Id;
    }
}

public sealed record VoidSupplierInvoicePaymentHoldCommand(
    string OrganizationId,
    string EnvironmentId,
    string InvoiceNo) : ICommand<SupplierInvoiceId>;

public sealed class VoidSupplierInvoicePaymentHoldCommandValidator : AbstractValidator<VoidSupplierInvoicePaymentHoldCommand>
{
    public VoidSupplierInvoicePaymentHoldCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.InvoiceNo).NotEmpty().MaximumLength(100);
    }
}

public sealed class VoidSupplierInvoicePaymentHoldCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<VoidSupplierInvoicePaymentHoldCommand, SupplierInvoiceId>
{
    public async Task<SupplierInvoiceId> Handle(VoidSupplierInvoicePaymentHoldCommand request, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.SupplierInvoices.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.InvoiceNo == request.InvoiceNo,
            cancellationToken)
            ?? throw new KnownException($"Supplier invoice '{request.InvoiceNo}' was not found.");

        try
        {
            invoice.VoidPaymentHold();
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        return invoice.Id;
    }
}
