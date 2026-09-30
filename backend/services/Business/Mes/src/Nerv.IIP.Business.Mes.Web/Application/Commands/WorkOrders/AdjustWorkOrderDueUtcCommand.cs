using FluentValidation;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record AdjustWorkOrderDueUtcCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    DateTimeOffset DueUtc,
    DateTimeOffset ChangedAtUtc) : ICommand<MesAcceptedResponse>, IWorkOrderConcurrencyRetryCommand;

public sealed class AdjustWorkOrderDueUtcCommandValidator : AbstractValidator<AdjustWorkOrderDueUtcCommand>
{
    public AdjustWorkOrderDueUtcCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
    }
}

public sealed class AdjustWorkOrderDueUtcCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AdjustWorkOrderDueUtcCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(AdjustWorkOrderDueUtcCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLifecycleCommandGuards.GetWorkOrderAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.WorkOrderId, cancellationToken);
        try
        {
            workOrder.AdjustDueUtc(request.DueUtc);
        }
        catch (InvalidOperationException)
        {
            throw new MesLifecycleConflictException("adjust-due-utc", workOrder.Status);
        }
        return new MesAcceptedResponse("Accepted", workOrder.WorkOrderId, request.ChangedAtUtc);
    }
}
