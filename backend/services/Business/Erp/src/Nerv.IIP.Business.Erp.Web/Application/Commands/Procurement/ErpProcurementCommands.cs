using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

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
    string? IdempotencyKey = null) : ICommand<PurchaseRequisitionId>;

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

public sealed class CreatePurchaseRequisitionFromSuggestionCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null)
    : ICommandHandler<CreatePurchaseRequisitionFromSuggestionCommand, PurchaseRequisitionId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<PurchaseRequisitionId> Handle(CreatePurchaseRequisitionFromSuggestionCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-requisition",
            "PR",
            request.RequisitionNo,
            request.IdempotencyKey,
            ErpNumberingService.Fingerprint(request.SuggestionId, request.SkuCode, request.UomCode, request.SiteCode, request.Quantity, request.RequiredDate));
        var existing = await dbContext.PurchaseRequisitions.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && (x.SuggestionId == request.SuggestionId || x.RequisitionNo == allocation.Number),
            cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var requisition = PurchaseRequisition.CreateFromSuggestion(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.SuggestionId,
            request.SkuCode,
            request.UomCode,
            request.SiteCode,
            request.Quantity,
            request.RequiredDate);
        dbContext.PurchaseRequisitions.Add(requisition);
        return requisition.Id;
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

public sealed class CreateRequestForQuotationCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null)
    : ICommandHandler<CreateRequestForQuotationCommand, RequestForQuotationId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<RequestForQuotationId> Handle(CreateRequestForQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(
            request.OrganizationId,
            request.EnvironmentId,
            "request-for-quotation",
            "RFQ",
            request.RfqNo,
            request.IdempotencyKey,
            ErpNumberingService.Fingerprint(request.SupplierCodes, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.RequiredDate}")));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.RequestForQuotations.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.RfqNo == allocation.Number,
                cancellationToken)).Id;
        }

        var rfq = RequestForQuotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
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

public sealed class ReceiveSupplierQuotationCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null)
    : ICommandHandler<ReceiveSupplierQuotationCommand, SupplierQuotationId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<SupplierQuotationId> Handle(ReceiveSupplierQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(
            request.OrganizationId,
            request.EnvironmentId,
            "supplier-quotation",
            "SQ",
            request.QuotationNo,
            request.IdempotencyKey,
            ErpNumberingService.Fingerprint(request.RfqNo, request.SupplierCode, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.PromisedDate}")));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SupplierQuotations.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.QuotationNo == allocation.Number,
                cancellationToken)).Id;
        }

        var quotation = SupplierQuotation.Receive(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
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
    string? IdempotencyKey = null) : ICommand<PurchaseOrderId>;

public sealed class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseOrderNo).MaximumLength(100);
        RuleFor(x => x.SupplierCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
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

public sealed class CreatePurchaseOrderCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null)
    : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<PurchaseOrderId> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-order",
            "PO",
            request.PurchaseOrderNo,
            request.IdempotencyKey,
            ErpNumberingService.Fingerprint(request.SupplierCode, request.SiteCode, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.PromisedDate}")));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.PurchaseOrders.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseOrderNo == allocation.Number,
                cancellationToken)).Id;
        }

        var order = PurchaseOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
            request.SupplierCode,
            request.SiteCode,
            request.Lines.Select(x => new PurchaseOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        dbContext.PurchaseOrders.Add(order);
        return order.Id;
    }
}

public sealed record PurchaseReceiptCommandLine(string PurchaseOrderLineNo, decimal ReceivedQuantity, string QualityStatus);

public sealed record RecordPurchaseReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string? PurchaseReceiptNo,
    string PurchaseOrderNo,
    IReadOnlyCollection<PurchaseReceiptCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<PurchaseReceiptId>;

public sealed class RecordPurchaseReceiptCommandValidator : AbstractValidator<RecordPurchaseReceiptCommand>
{
    public RecordPurchaseReceiptCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseReceiptNo).MaximumLength(100);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.PurchaseOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.ReceivedQuantity).GreaterThan(0);
            line.RuleFor(x => x.QualityStatus).NotEmpty().MaximumLength(50);
        });
    }
}

public sealed class RecordPurchaseReceiptCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null)
    : ICommandHandler<RecordPurchaseReceiptCommand, PurchaseReceiptId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<PurchaseReceiptId> Handle(RecordPurchaseReceiptCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(
            request.OrganizationId,
            request.EnvironmentId,
            "purchase-receipt",
            "GR",
            request.PurchaseReceiptNo,
            request.IdempotencyKey,
            ErpNumberingService.Fingerprint(request.PurchaseOrderNo, request.Lines.Select(x => $"{x.PurchaseOrderLineNo}:{x.ReceivedQuantity}:{x.QualityStatus}")));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.PurchaseReceipts.SingleAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.PurchaseReceiptNo == allocation.Number,
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

        var receipt = PurchaseReceipt.Record(
            order,
            allocation.Number,
            request.Lines.Select(x => new PurchaseReceiptLineDraft(x.PurchaseOrderLineNo, x.ReceivedQuantity, x.QualityStatus)));
        dbContext.PurchaseReceipts.Add(receipt);
        return receipt.Id;
    }
}
