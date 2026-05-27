using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;
using Nerv.IIP.Business.Erp.Web.Application.Commands;

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

public sealed class OpenOpportunityCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null) : ICommandHandler<OpenOpportunityCommand, OpportunityId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<OpportunityId> Handle(OpenOpportunityCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(request.OrganizationId, request.EnvironmentId, "opportunity", "OPP", request.OpportunityNo, request.IdempotencyKey, ErpNumberingService.Fingerprint(request.CustomerCode, request.Topic));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.Opportunities.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.OpportunityNo == allocation.Number, cancellationToken)).Id;
        }

        var opportunity = Opportunity.Open(request.OrganizationId, request.EnvironmentId, allocation.Number, request.CustomerCode, request.Topic);
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

public sealed class CreateQuotationCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null) : ICommandHandler<CreateQuotationCommand, QuotationId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<QuotationId> Handle(CreateQuotationCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(request.OrganizationId, request.EnvironmentId, "quotation", "QUO", request.QuotationNo, request.IdempotencyKey, ErpNumberingService.Fingerprint(request.CustomerCode, request.ExpiresOn, request.Lines.Select(x => $"{x.LineNo}:{x.SkuCode}:{x.Quantity}:{x.UnitPrice}:{x.RequiredDate}")));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.Quotations.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.QuotationNo == allocation.Number, cancellationToken)).Id;
        }

        var quotation = Quotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            allocation.Number,
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

public sealed record CreateSalesOrderCommand(string OrganizationId, string EnvironmentId, string? SalesOrderNo, string QuotationNo, string? IdempotencyKey = null) : ICommand<SalesOrderId>;

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

public sealed class CreateSalesOrderCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null) : ICommandHandler<CreateSalesOrderCommand, SalesOrderId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<SalesOrderId> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(request.OrganizationId, request.EnvironmentId, "sales-order", "SO", request.SalesOrderNo, request.IdempotencyKey, ErpNumberingService.Fingerprint(request.QuotationNo));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.SalesOrders.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.SalesOrderNo == allocation.Number, cancellationToken)).Id;
        }

        var quotation = await dbContext.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.QuotationNo == request.QuotationNo,
                cancellationToken)
            ?? throw new KnownException($"Quotation '{request.QuotationNo}' was not found.");
        var order = SalesOrder.CreateFromQuotation(allocation.Number, quotation);
        dbContext.SalesOrders.Add(order);
        return order.Id;
    }
}

public sealed record DeliveryOrderCommandLine(string SalesOrderLineNo, decimal Quantity);

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

public sealed class ReleaseDeliveryOrderCommandHandler(ApplicationDbContext dbContext, ErpNumberingService? numberingService = null) : ICommandHandler<ReleaseDeliveryOrderCommand, DeliveryOrderId>
{
    private readonly ErpNumberingService _numberingService = numberingService ?? new ErpNumberingService();

    public async Task<DeliveryOrderId> Handle(ReleaseDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
        var allocation = _numberingService.Allocate(request.OrganizationId, request.EnvironmentId, "delivery-order", "DO", request.DeliveryOrderNo, request.IdempotencyKey, ErpNumberingService.Fingerprint(request.SalesOrderNo, request.Lines.Select(x => $"{x.SalesOrderLineNo}:{x.Quantity}")));
        if (allocation.IsIdempotentReplay)
        {
            return (await dbContext.DeliveryOrders.SingleAsync(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.DeliveryOrderNo == allocation.Number, cancellationToken)).Id;
        }

        var order = await dbContext.SalesOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.SalesOrderNo == request.SalesOrderNo,
                cancellationToken)
            ?? throw new KnownException($"Sales order '{request.SalesOrderNo}' was not found.");
        var delivery = DeliveryOrder.Release(
            order,
            allocation.Number,
            request.Lines.Select(x => new DeliveryOrderLineDraft(x.SalesOrderLineNo, x.Quantity)));
        dbContext.DeliveryOrders.Add(delivery);
        return delivery.Id;
    }
}
