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

public sealed class CreateSalesOrderCommandHandler(ApplicationDbContext dbContext, ErpCodingService? codingService = null) : ICommandHandler<CreateSalesOrderCommand, SalesOrderId>
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
        var order = SalesOrder.CreateFromQuotation(allocation.Code, quotation);
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
        var delivery = DeliveryOrder.Release(
            order,
            allocation.Code,
            request.Lines.Select(x => new DeliveryOrderLineDraft(x.SalesOrderLineNo, x.Quantity)));
        dbContext.DeliveryOrders.Add(delivery);
        return delivery.Id;
    }
}
