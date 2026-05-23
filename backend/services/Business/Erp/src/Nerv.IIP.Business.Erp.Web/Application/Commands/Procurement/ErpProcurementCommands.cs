using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseReceiptAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.PurchaseRequisitionAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.RequestForQuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SupplierQuotationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Procurement;

public sealed record CreatePurchaseRequisitionFromSuggestionCommand(
    string OrganizationId,
    string EnvironmentId,
    string RequisitionNo,
    string SuggestionId,
    string SkuCode,
    string UomCode,
    string SiteCode,
    decimal Quantity,
    DateOnly RequiredDate) : ICommand<PurchaseRequisitionId>;

public sealed class CreatePurchaseRequisitionFromSuggestionCommandValidator : AbstractValidator<CreatePurchaseRequisitionFromSuggestionCommand>
{
    public CreatePurchaseRequisitionFromSuggestionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RequisitionNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SuggestionId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CreatePurchaseRequisitionFromSuggestionCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreatePurchaseRequisitionFromSuggestionCommand, PurchaseRequisitionId>
{
    public async Task<PurchaseRequisitionId> Handle(CreatePurchaseRequisitionFromSuggestionCommand request, CancellationToken cancellationToken)
    {
        var existing = await dbContext.PurchaseRequisitions.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.SuggestionId == request.SuggestionId,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var requisition = PurchaseRequisition.CreateFromSuggestion(
            request.OrganizationId,
            request.EnvironmentId,
            request.RequisitionNo,
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
    string RfqNo,
    IReadOnlyCollection<string> SupplierCodes,
    IReadOnlyCollection<RfqCommandLine> Lines) : ICommand<RequestForQuotationId>;

public sealed class CreateRequestForQuotationCommandValidator : AbstractValidator<CreateRequestForQuotationCommand>
{
    public CreateRequestForQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RfqNo).NotEmpty().MaximumLength(100);
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

public sealed class CreateRequestForQuotationCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateRequestForQuotationCommand, RequestForQuotationId>
{
    public Task<RequestForQuotationId> Handle(CreateRequestForQuotationCommand request, CancellationToken cancellationToken)
    {
        var rfq = RequestForQuotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.RfqNo,
            request.SupplierCodes,
            request.Lines.Select(x => new RfqLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.SiteCode, x.RequiredDate)));
        dbContext.RequestForQuotations.Add(rfq);
        return Task.FromResult(rfq.Id);
    }
}

public sealed record SupplierQuotationCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly PromisedDate);

public sealed record ReceiveSupplierQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string QuotationNo,
    string RfqNo,
    string SupplierCode,
    IReadOnlyCollection<SupplierQuotationCommandLine> Lines) : ICommand<SupplierQuotationId>;

public sealed class ReceiveSupplierQuotationCommandValidator : AbstractValidator<ReceiveSupplierQuotationCommand>
{
    public ReceiveSupplierQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.QuotationNo).NotEmpty().MaximumLength(100);
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

public sealed class ReceiveSupplierQuotationCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<ReceiveSupplierQuotationCommand, SupplierQuotationId>
{
    public Task<SupplierQuotationId> Handle(ReceiveSupplierQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = SupplierQuotation.Receive(
            request.OrganizationId,
            request.EnvironmentId,
            request.QuotationNo,
            request.RfqNo,
            request.SupplierCode,
            request.Lines.Select(x => new SupplierQuotationLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        dbContext.SupplierQuotations.Add(quotation);
        return Task.FromResult(quotation.Id);
    }
}

public sealed record PurchaseOrderCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly PromisedDate);

public sealed record CreatePurchaseOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string PurchaseOrderNo,
    string SupplierCode,
    string SiteCode,
    IReadOnlyCollection<PurchaseOrderCommandLine> Lines) : ICommand<PurchaseOrderId>;

public sealed class CreatePurchaseOrderCommandValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseOrderNo).NotEmpty().MaximumLength(100);
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

public sealed class CreatePurchaseOrderCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderId>
{
    public Task<PurchaseOrderId> Handle(CreatePurchaseOrderCommand request, CancellationToken cancellationToken)
    {
        var order = PurchaseOrder.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.PurchaseOrderNo,
            request.SupplierCode,
            request.SiteCode,
            request.Lines.Select(x => new PurchaseOrderLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.PromisedDate)));
        dbContext.PurchaseOrders.Add(order);
        return Task.FromResult(order.Id);
    }
}

public sealed record PurchaseReceiptCommandLine(string PurchaseOrderLineNo, decimal ReceivedQuantity, string QualityStatus);

public sealed record RecordPurchaseReceiptCommand(
    string OrganizationId,
    string EnvironmentId,
    string PurchaseReceiptNo,
    string PurchaseOrderNo,
    IReadOnlyCollection<PurchaseReceiptCommandLine> Lines) : ICommand<PurchaseReceiptId>;

public sealed class RecordPurchaseReceiptCommandValidator : AbstractValidator<RecordPurchaseReceiptCommand>
{
    public RecordPurchaseReceiptCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PurchaseReceiptNo).NotEmpty().MaximumLength(100);
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

public sealed class RecordPurchaseReceiptCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<RecordPurchaseReceiptCommand, PurchaseReceiptId>
{
    public async Task<PurchaseReceiptId> Handle(RecordPurchaseReceiptCommand request, CancellationToken cancellationToken)
    {
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
            request.PurchaseReceiptNo,
            request.Lines.Select(x => new PurchaseReceiptLineDraft(x.PurchaseOrderLineNo, x.ReceivedQuantity, x.QualityStatus)));
        dbContext.PurchaseReceipts.Add(receipt);
        return receipt.Id;
    }
}
