using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.EngineeringChangeAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Workbench;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Mes.Web.Application.ProductEngineering;

public sealed record RecordEngineeringChangeDecisionCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string ChangeNumber,
    string Decision,
    string DecidedBy,
    string Reason) : ICommand;

public sealed class RecordEngineeringChangeDecisionCommandValidator : AbstractValidator<RecordEngineeringChangeDecisionCommand>
{
    public RecordEngineeringChangeDecisionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ChangeNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Decision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.DecidedBy).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class RecordEngineeringChangeDecisionCommandHandler(ApplicationDbContext dbContext, TimeProvider? timeProvider = null)
    : ICommandHandler<RecordEngineeringChangeDecisionCommand>
{
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;

    public async Task Handle(RecordEngineeringChangeDecisionCommand request, CancellationToken cancellationToken)
    {
        var impact = await dbContext.EngineeringChangeWorkOrderImpacts
            .SingleOrDefaultAsync(x =>
                x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId &&
                x.WorkOrderId == request.WorkOrderId &&
                x.ChangeNumber == request.ChangeNumber,
                cancellationToken)
            ?? throw new KnownException($"未找到工程变更影响记录，WorkOrderId = {request.WorkOrderId}, ECO = {request.ChangeNumber}");

        try
        {
            var decidedAtUtc = timeProvider.GetUtcNow();
            if (!impact.RecordDecision(request.Decision, request.DecidedBy, request.Reason, decidedAtUtc))
            {
                return;
            }

            var workOrder = await dbContext.WorkOrders.SingleOrDefaultAsync(x =>
                    x.OrganizationId == request.OrganizationId &&
                    x.EnvironmentId == request.EnvironmentId &&
                    x.WorkOrderIdValue == request.WorkOrderId,
                    cancellationToken)
                ?? throw new KnownException($"未找到工程变更影响工单，WorkOrderId = {request.WorkOrderId}");

            if (request.Decision == MesEngineeringChangeDecisions.AbortWorkOrder)
            {
                await WorkOrderCancellationOrchestrator.CancelAsync(
                    dbContext,
                    request.OrganizationId,
                    request.EnvironmentId,
                    request.WorkOrderId,
                    $"Engineering change {request.ChangeNumber}: {request.Reason}",
                    decidedAtUtc,
                    cancellationToken);
            }
            else if (request.Decision == MesEngineeringChangeDecisions.ContinueWithArchivedVersion)
            {
                workOrder.ResolveEngineeringChangeHold(impact.WorkOrderStatusAtDetection);
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new KnownException(exception.Message, exception);
        }
    }
}

public static class MesArchivedProductionVersionGuard
{
    public static async Task ThrowIfArchivedAsync(
        ApplicationDbContext dbContext,
        string organizationId,
        string environmentId,
        string? productionVersionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productionVersionId))
        {
            return;
        }

        var normalizedProductionVersionId = productionVersionId.Trim();
        var archived = await dbContext.EngineeringChangeWorkOrderImpacts.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.EnvironmentId == environmentId &&
            x.ArchivedProductionVersionId == normalizedProductionVersionId &&
            x.Status == MesEngineeringChangeImpactStatuses.ArchivedProductionVersion,
            cancellationToken);
        if (archived)
        {
            throw new KnownException($"MES cannot create a work order with archived production version '{normalizedProductionVersionId}'.");
        }
    }
}
