using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.DeliveryOrderAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.OpportunityAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.QuotationAggregate;
using Nerv.IIP.Business.Erp.Domain.AggregatesModel.SalesOrderAggregate;
using Nerv.IIP.Business.Erp.Infrastructure;

namespace Nerv.IIP.Business.Erp.Web.Application.Commands.Sales;

public sealed record OpenOpportunityCommand(string OrganizationId, string EnvironmentId, string OpportunityNo, string CustomerCode, string Topic) : ICommand<OpportunityId>;

public sealed class OpenOpportunityCommandValidator : AbstractValidator<OpenOpportunityCommand>
{
    public OpenOpportunityCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.OpportunityNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.CustomerCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Topic).NotEmpty().MaximumLength(200);
    }
}

public sealed class OpenOpportunityCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<OpenOpportunityCommand, OpportunityId>
{
    public Task<OpportunityId> Handle(OpenOpportunityCommand request, CancellationToken cancellationToken)
    {
        var opportunity = Opportunity.Open(request.OrganizationId, request.EnvironmentId, request.OpportunityNo, request.CustomerCode, request.Topic);
        dbContext.Opportunities.Add(opportunity);
        return Task.FromResult(opportunity.Id);
    }
}

public sealed record QuotationCommandLine(string LineNo, string SkuCode, string UomCode, decimal Quantity, decimal UnitPrice, DateOnly RequiredDate);

public sealed record CreateQuotationCommand(
    string OrganizationId,
    string EnvironmentId,
    string QuotationNo,
    string CustomerCode,
    DateOnly ExpiresOn,
    IReadOnlyCollection<QuotationCommandLine> Lines) : ICommand<QuotationId>;

public sealed class CreateQuotationCommandValidator : AbstractValidator<CreateQuotationCommand>
{
    public CreateQuotationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.QuotationNo).NotEmpty().MaximumLength(100);
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

public sealed class CreateQuotationCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<CreateQuotationCommand, QuotationId>
{
    public Task<QuotationId> Handle(CreateQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = Quotation.Create(
            request.OrganizationId,
            request.EnvironmentId,
            request.QuotationNo,
            request.CustomerCode,
            request.ExpiresOn,
            request.Lines.Select(x => new QuotationLineDraft(x.LineNo, x.SkuCode, x.UomCode, x.Quantity, x.UnitPrice, x.RequiredDate)));
        dbContext.Quotations.Add(quotation);
        return Task.FromResult(quotation.Id);
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

public sealed record CreateSalesOrderCommand(string OrganizationId, string EnvironmentId, string SalesOrderNo, string QuotationNo) : ICommand<SalesOrderId>;

public sealed class CreateSalesOrderCommandValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SalesOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.QuotationNo).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateSalesOrderCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<CreateSalesOrderCommand, SalesOrderId>
{
    public async Task<SalesOrderId> Handle(CreateSalesOrderCommand request, CancellationToken cancellationToken)
    {
        var quotation = await dbContext.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId
                && x.EnvironmentId == request.EnvironmentId
                && x.QuotationNo == request.QuotationNo,
                cancellationToken)
            ?? throw new KnownException($"Quotation '{request.QuotationNo}' was not found.");
        var order = SalesOrder.CreateFromQuotation(request.SalesOrderNo, quotation);
        dbContext.SalesOrders.Add(order);
        return order.Id;
    }
}

public sealed record DeliveryOrderCommandLine(string SalesOrderLineNo, decimal Quantity);

public sealed record ReleaseDeliveryOrderCommand(
    string OrganizationId,
    string EnvironmentId,
    string DeliveryOrderNo,
    string SalesOrderNo,
    IReadOnlyCollection<DeliveryOrderCommandLine> Lines) : ICommand<DeliveryOrderId>;

public sealed class ReleaseDeliveryOrderCommandValidator : AbstractValidator<ReleaseDeliveryOrderCommand>
{
    public ReleaseDeliveryOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DeliveryOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SalesOrderNo).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SalesOrderLineNo).NotEmpty().MaximumLength(100);
            line.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public sealed class ReleaseDeliveryOrderCommandHandler(ApplicationDbContext dbContext) : ICommandHandler<ReleaseDeliveryOrderCommand, DeliveryOrderId>
{
    public async Task<DeliveryOrderId> Handle(ReleaseDeliveryOrderCommand request, CancellationToken cancellationToken)
    {
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
            request.DeliveryOrderNo,
            request.Lines.Select(x => new DeliveryOrderLineDraft(x.SalesOrderLineNo, x.Quantity)));
        dbContext.DeliveryOrders.Add(delivery);
        return delivery.Id;
    }
}
