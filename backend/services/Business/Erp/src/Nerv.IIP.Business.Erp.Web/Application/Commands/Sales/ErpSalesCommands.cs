using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesReturnAuthorizationAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Approval;
using Nerv.IIP.Business.Erp.Web.Application.Commands;
using Nerv.IIP.Business.Erp.Web.Application.MasterData;
using Nerv.IIP.Business.Erp.Web.Application.Wms;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;

public sealed record OpenOpportunityCommand(string OrganizationId, string EnvironmentId, string? OpportunityNo, string CustomerCode, string Topic, string? IdempotencyKey = null) : ICommand<OpportunityId>;

public sealed class OpenOpportunityCommandValidator : AbstractValidator<OpenOpportunityCommand>
{
    public OpenOpportunityCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OpportunityNo).MaximumLength(100);
        RuleFor(x => x.CustomerCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200);
    }
}

public sealed class OpenOpportunityCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<OpenOpportunityCommand, OpportunityId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<OpportunityId> Handle(OpenOpportunityCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "opportunity", request.OpportunityNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.CustomerCode, request.Topic), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.Opportunities.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.OpportunityNo == allocation.Code, cancellationToken)).Id;
        }

        var opportunity = Opportunity.Open(request.OrganizationId, request.EnvironmentId, allocation.Code, request.CustomerCode, request.Topic);
        dbContext.Opportunities.Add(opportunity);
        return opportunity.Id;
    }
}

public sealed record QuotationCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly RequiredDate);

public sealed record CreateQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string? QuotationNo,
    string CustomerCode,
    DateOnly ExpiresOn,
    IReadOnlyCollection<QuotationCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<QuotationId>;

public sealed class CreateQuotationCommandValidator : AbstractValidator<CreateQuotationCommand>
{
    public CreateQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.QuotationNo).MaximumLength(100);
        RuleFor(x => x.CustomerCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExpiresOn).NotEqual(default(DateOnly));
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.LineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.SkuCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.UomCode).NotEmpty().MaximumLength(50);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.UnitPrice).GreaterThan(0);
            line.RuleFor(x => x.RequiredDate).NotEqual(default(DateOnly));
        });
    }
}

public sealed class CreateQuotationCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateQuotationCommand, QuotationId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<QuotationId> Handle(CreateQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "quotation", request.QuotationNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.CustomerCode, request.ExpiresOn, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.RequiredDate}")), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.Quotations.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.QuotationNo == allocation.Code, cancellationToken)).Id;
        }

        var quotation = Quotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            request.CustomerCode,
            request.ExpiresOn,
            request.Lines.Select(x => new QuotationLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.RequiredDate)));
        dbContext.Quotations.Add(quotation);
        return quotation.Id;
    }
}

public sealed record ApproveQuotationCommand(string OrganizationId, string EnvironmentId, string QuotationNo) : ICommand;

public sealed class ApproveQuotationCommandValidator : AbstractValidator<ApproveQuotationCommand>
{
    public ApproveQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.QuotationNo).NotEmpty().MaximumLength(100);
    }
}

public sealed class ApproveQuotationCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<ApproveQuotationCommand>
{
    public async Task Handle(ApproveQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = await dbContext.Quotations.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.QuotationNo == request.QuotationNo,
            cancellationToken)
            ?? throw new KnownException($"Quotation '{request.QuotationNo}' was not found.");
        quotation.Approve();
    }
}

public sealed record CreateSalesOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string? SalesOrderNo,
    string QuotationNo,
    string? IdempotencyKey = null) : ICommand<SalesOrderId>;

public sealed class CreateSalesOrderCommandValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SalesOrderNo).MaximumLength(100);
        RuleFor(x => x.QuotationNo).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateSalesOrderCommandHandler(ApplicationDbContext dbContext, ICustomerCreditProfileReader creditProfileReader, ErpCodingService? codingService = null) : ICommandHandler<CreateSalesOrderCommand, SalesOrderId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SalesOrderId> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "sales-order", request.SalesOrderNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.QuotationNo), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SalesOrders.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.SalesOrderNo == allocation.Code, cancellationToken)).Id;
        }

        var quotation = await dbContext.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.QuotationNo == request.QuotationNo,
                cancellationToken)
            ?? throw new KnownException($"Quotation '{request.QuotationNo}' was not found.");
        var creditProfile = await creditProfileReader.GetAsync(request.OrganizationId, request.EnvironmentId, quotation.CustomerCode, cancellationToken)
            ?? throw new KnownException($"Customer '{quotation.CustomerCode}' credit limit master data is required before creating a sales order.");
        var openReceivables = await dbContext.AccountReceivables
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CustomerCode == quotation.CustomerCode)
            .SumAsync(x => x.Amount - x.CollectedAmount, cancellationToken);
        var activeSalesOrders = (await dbContext.SalesOrders
            .Include(x => x.Lines)
            .Where(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.CustomerCode == quotation.CustomerCode
                && (x.Status == "released" || x.Status == "credit-held"))
            .ToListAsync(cancellationToken))
            .SelectMany(x => x.Lines)
            .Sum(x => x.OpenQuantity * x.UnitPrice);
        var creditSnapshot = new CustomerCreditSnapshot(quotation.CustomerCode, creditProfile.CreditLimit, openReceivables, activeSalesOrders);

        var order = SalesOrder.CreateFromQuotation(allocation.Code, quotation, creditSnapshot);

        dbContext.SalesOrders.Add(order);
        return order.Id;
    }
}

public sealed record ReleaseSalesOrderCreditHoldCommand(
    string OrganizationId,
    string EnvironmentId,
    string SalesOrderNo,
    string StartedBy = "system:erp") : ICommand;

public sealed class ReleaseSalesOrderCreditHoldCommandValidator : AbstractValidator<ReleaseSalesOrderCreditHoldCommand>
{
    public ReleaseSalesOrderCreditHoldCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SalesOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.StartedBy).NotEmpty().MaximumLength(150);
    }
}

public sealed class ReleaseSalesOrderCreditHoldCommandHandler(
    ApplicationDbContext dbContext,
    IPurchaseOrderApprovalClient approvalClient)
    : ICommandHandler<ReleaseSalesOrderCreditHoldCommand>
{
    public async Task Handle(ReleaseSalesOrderCreditHoldCommand request, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrders
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SalesOrderNo == request.SalesOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Sales order '{request.SalesOrderNo}' was not found.");

        try
        {
            if (!string.Equals(order.Status, "credit-held", StringComparison.Ordinal))
            {
                order.ReleaseCreditHold();
                return;
            }

            await approvalClient.StartApprovalAsync(new PurchaseOrderApprovalRequest(
                order.OrganizationId,
                order.EnvironmentId,
                "erp-sales-credit-release",
                "business-erp",
                "sales-order-credit-release",
                order.SalesOrderNo,
                null,
                request.StartedBy,
                $"sales-credit:{order.OrganizationId}:{order.EnvironmentId}:{order.SalesOrderNo}",
                order.TotalAmount), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}

public sealed record DeliveryOrderCommandLine(string SalesOrderLineNo, decimal Quantity, string? LocationCode = null, string? LotNo = null);

public sealed record ReleaseDeliveryOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string? DeliveryOrderNo,
    string SalesOrderNo,
    IReadOnlyCollection<DeliveryOrderCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<DeliveryOrderId>;

public sealed class ReleaseDeliveryOrderCommandValidator : AbstractValidator<ReleaseDeliveryOrderCommand>
{
    public ReleaseDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeliveryOrderNo).MaximumLength(100);
        RuleFor(x => x.SalesOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SalesOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class ReleaseDeliveryOrderCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<ReleaseDeliveryOrderCommand, DeliveryOrderId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<DeliveryOrderId> Handle(ReleaseDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(request.OrganizationId, request.EnvironmentId, "delivery-order", request.DeliveryOrderNo, request.IdempotencyKey, ErpCodingService.Fingerprint(request.SalesOrderNo, request.Lines.Select(x => $"{x.SalesOrderLineNo}:{x.Quantity}")), cancellationToken);
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.DeliveryOrders.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.DeliveryOrderNo == allocation.Code, cancellationToken)).Id;
        }

        var order = await dbContext.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SalesOrderNo == request.SalesOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Sales order '{request.SalesOrderNo}' was not found.");
        DeliveryOrder delivery;
        try
        {
            delivery = DeliveryOrder.Release(
                order,
                allocation.Code,
                request.Lines.Select(x => new DeliveryOrderLineDraft(x.SalesOrderLineNo, x.Quantity, x.LocationCode, x.LotNo)));
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }

        dbContext.DeliveryOrders.Add(delivery);
        return delivery.Id;
    }
}

public sealed record ChangeSalesOrderLineCommand(
    string OrganizationId,
    string EnvironmentId,
    string SalesOrderNo,
    string LineNo,
    decimal OrderedQuantity,
    decimal UnitPrice,
    DateOnly RequiredDate,
    string Reason) : ICommand;

public sealed class ChangeSalesOrderLineCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<ChangeSalesOrderLineCommand>
{
    public async Task Handle(ChangeSalesOrderLineCommand request, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrders.Include(x => x.Lines).Include(x => x.ChangeHistory).SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.SalesOrderNo == request.SalesOrderNo,
            cancellationToken) ?? throw new KnownException($"Sales order '{request.SalesOrderNo}' was not found.");
        try { order.ChangeLine(request.LineNo, request.OrderedQuantity, request.UnitPrice, request.RequiredDate, request.Reason); }
        catch (InvalidOperationException exception) { throw new KnownException(exception.Message, exception); }
    }
}

public sealed record CancelSalesOrderCommand(string OrganizationId, string EnvironmentId, string SalesOrderNo, string Reason) : ICommand;

public sealed class CancelSalesOrderCommandHandler(
    ApplicationDbContext dbContext,
    IWmsOutboundCancellationClient wmsOutboundCancellationClient) : ICommandHandler<CancelSalesOrderCommand>
{
    public async Task Handle(CancelSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await dbContext.SalesOrders.Include(x => x.Lines).Include(x => x.ChangeHistory).SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.SalesOrderNo == request.SalesOrderNo,
            cancellationToken) ?? throw new KnownException($"Sales order '{request.SalesOrderNo}' was not found.");
        var deliveries = await dbContext.DeliveryOrders.Include(x => x.Lines).Where(x => x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId && x.SalesOrderNo == request.SalesOrderNo && x.Status == "released").ToArrayAsync(cancellationToken);
        var cancellationResults = await wmsOutboundCancellationClient.CancelForDeliveryOrdersAsync(
            request.OrganizationId, request.EnvironmentId, deliveries.Select(x => x.DeliveryOrderNo).ToArray(), request.Reason, cancellationToken);
        var missingDeliveryOrderNos = cancellationResults
            .Where(x => x.Status == WmsOutboundCancellationStatus.NotFound)
            .Select(x => x.DeliveryOrderNo)
            .ToArray();
        if (missingDeliveryOrderNos.Length > 0)
        {
            throw new KnownException($"Sales order has WMS outbound orders that could not be found yet: {string.Join(", ", missingDeliveryOrderNos)}.");
        }

        var notCancellableDeliveryOrderNos = cancellationResults
            .Where(x => x.Status == WmsOutboundCancellationStatus.NotCancellable)
            .Select(x => x.DeliveryOrderNo)
            .ToArray();
        if (notCancellableDeliveryOrderNos.Length > 0)
        {
            throw new KnownException($"Sales order has WMS outbound orders that are already in a non-cancellable state: {string.Join(", ", notCancellableDeliveryOrderNos)}.");
        }
        try
        {
            foreach (var delivery in deliveries)
            {
                if (delivery.Cancel(request.Reason, DateTime.UtcNow))
                {
                    foreach (var line in delivery.Lines)
                    {
                        order.ReleaseDelivery(line.SalesOrderLineNo, line.Quantity);
                    }
                }
            }

            order.Cancel(request.Reason);
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}

public sealed record SalesReturnAuthorizationCommandLine(
    string SalesOrderLineNo,
    decimal Quantity,
    string LocationCode,
    string? LotNo);

public sealed record CreateSalesReturnAuthorizationCommand(
    string OrganizationId,
    string EnvironmentId,
    string? RmaNo,
    string SalesOrderNo,
    string AccountReceivableNo,
    string SiteCode,
    IReadOnlyCollection<SalesReturnAuthorizationCommandLine> Lines,
    string? IdempotencyKey = null) : ICommand<SalesReturnAuthorizationId>;

public sealed class CreateSalesReturnAuthorizationCommandValidator : AbstractValidator<CreateSalesReturnAuthorizationCommand>
{
    public CreateSalesReturnAuthorizationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.RmaNo).MaximumLength(100);
        RuleFor(x => x.SalesOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AccountReceivableNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SiteCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SalesOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.LocationCode).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.LotNo).MaximumLength(100);
        });
    }
}

public sealed class CreateSalesReturnAuthorizationCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null)
    : ICommandHandler<CreateSalesReturnAuthorizationCommand, SalesReturnAuthorizationId>
{
    private readonly ErpCodingService _codingService = codingService ?? new ErpCodingService();

    public async Task<SalesReturnAuthorizationId> Handle(CreateSalesReturnAuthorizationCommand request, CancellationToken cancellationToken)
    {
        var allocation = await _codingService.AllocateAsync(
            request.OrganizationId,
            request.EnvironmentId,
            "sales-return-authorization",
            request.RmaNo,
            request.IdempotencyKey,
            ErpCodingService.Fingerprint(request.SalesOrderNo, request.AccountReceivableNo, request.SiteCode, request.Lines),
            cancellationToken);
        var existing = await dbContext.SalesReturnAuthorizations.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.RmaNo == allocation.Code,
            cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var salesOrder = await dbContext.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SalesOrderNo == request.SalesOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Sales order '{request.SalesOrderNo}' was not found for RMA authorization.");
        var receivable = await dbContext.AccountReceivables.SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId
            && x.EnvironmentId == request.EnvironmentId
            && x.ReceivableNo == request.AccountReceivableNo,
            cancellationToken)
            ?? throw new KnownException($"Account receivable '{request.AccountReceivableNo}' was not found for RMA authorization.");
        if (!string.Equals(salesOrder.CustomerCode, receivable.CustomerCode, StringComparison.Ordinal))
        {
            throw new KnownException("RMA sales order customer must match the account receivable customer.");
        }

        var priorRmas = await dbContext.SalesReturnAuthorizations
            .Include(x => x.Lines)
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SalesOrderNo == salesOrder.SalesOrderNo)
            .ToListAsync(cancellationToken);
        var drafts = new List<SalesReturnAuthorizationLineDraft>();
        foreach (var requestedLine in request.Lines)
        {
            var salesLine = salesOrder.Lines.SingleOrDefault(x => x.LineNo == requestedLine.SalesOrderLineNo)
                ?? throw new KnownException($"Sales order line '{requestedLine.SalesOrderLineNo}' was not found for RMA authorization.");
            var alreadyAuthorized = priorRmas.SelectMany(x => x.Lines)
                .Where(x => x.SalesOrderLineNo == salesLine.LineNo)
                .Sum(x => x.Quantity);
            if (requestedLine.Quantity > salesLine.DeliveredQuantity - alreadyAuthorized)
            {
                throw new KnownException($"RMA quantity for sales order line '{salesLine.LineNo}' exceeds delivered quantity available for return.");
            }

            drafts.Add(new SalesReturnAuthorizationLineDraft(
                salesLine.LineNo,
                salesLine.SkuCode,
                salesLine.UomCode,
                requestedLine.Quantity,
                salesLine.UnitPrice,
                requestedLine.LocationCode,
                requestedLine.LotNo));
        }

        var totalAmount = drafts.Sum(x => x.Quantity * x.UnitPrice);
        if (totalAmount > receivable.OpenAmount)
        {
            throw new KnownException("RMA credit amount cannot exceed the account receivable open balance.");
        }

        var rma = SalesReturnAuthorization.Authorize(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Code,
            salesOrder.SalesOrderNo,
            receivable.ReceivableNo,
            salesOrder.CustomerCode,
            request.SiteCode,
            receivable.CurrencyCode,
            receivable.ExchangeRate,
            drafts);
        dbContext.SalesReturnAuthorizations.Add(rma);
        return rma.Id;
    }
}
