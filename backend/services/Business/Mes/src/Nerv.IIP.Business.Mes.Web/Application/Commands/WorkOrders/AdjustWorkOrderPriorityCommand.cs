using FluentValidation;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Behaviors;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using Nerv.IIP.Business.Mes.Web.Application.Errors;

namespace Nerv.IIP.Business.Mes.Web.Application.Commands.WorkOrders;

public sealed record AdjustWorkOrderPriorityCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    bool IsRush,
    int Priority,
    DateTimeOffset ChangedAtUtc) : ICommand<MesAcceptedResponse>, IWorkOrderConcurrencyRetryCommand;

public sealed class AdjustWorkOrderPriorityCommandValidator : AbstractValidator<AdjustWorkOrderPriorityCommand>
{
    public AdjustWorkOrderPriorityCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
    }
}

public sealed class AdjustWorkOrderPriorityCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<AdjustWorkOrderPriorityCommand, MesAcceptedResponse>
{
    public async Task<MesAcceptedResponse> Handle(AdjustWorkOrderPriorityCommand request, CancellationToken cancellationToken)
    {
        var workOrder = await WorkOrderLifecycleCommandGuards.GetWorkOrderAsync(
            dbContext, request.OrganizationId, request.EnvironmentId, request.WorkOrderId, cancellationToken);
        try
        {
            workOrder.AdjustPriority(request.IsRush, request.Priority);
        }
        catch (InvalidOperationException)
        {
            throw new MesLifecycleConflictException("adjust-priority", workOrder.Status);
        }
        return new MesAcceptedResponse("Accepted", workOrder.WorkOrderId, request.ChangedAtUtc);
    }
}
