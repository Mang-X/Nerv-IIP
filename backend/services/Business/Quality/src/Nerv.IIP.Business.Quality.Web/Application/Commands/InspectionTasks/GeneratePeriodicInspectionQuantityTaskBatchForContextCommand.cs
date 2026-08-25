using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;
using Nerv.IIP.Business.Quality.Web.Application.IntegrationEventHandlers;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

public sealed record GeneratePeriodicInspectionQuantityTaskBatchForContextCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationId,
    PeriodicInspectionRuntimeContextId RuntimeContextId,
    DateTime ObservedNextAttemptAtUtc,
    DateTime NextAttemptAtUtc,
    int MaxWindows) : ICommand<int>;

public sealed class GeneratePeriodicInspectionQuantityTaskBatchForContextCommandValidator
    : AbstractValidator<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand>
{
    public GeneratePeriodicInspectionQuantityTaskBatchForContextCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RuntimeContextId).NotEmpty();
        RuleFor(x => x.ObservedNextAttemptAtUtc.Kind).Equal(DateTimeKind.Utc);
        RuleFor(x => x.NextAttemptAtUtc.Kind).Equal(DateTimeKind.Utc);
        RuleFor(x => x.MaxWindows).InclusiveBetween(1, 1000);
    }
}

public sealed class GeneratePeriodicInspectionQuantityTaskBatchForContextCommandHandler(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator)
    : ICommandHandler<GeneratePeriodicInspectionQuantityTaskBatchForContextCommand, int>
{
    public async Task<int> Handle(
        GeneratePeriodicInspectionQuantityTaskBatchForContextCommand request,
        CancellationToken cancellationToken)
    {
        var generated = 0;
        await scopeCoordinator.ExecuteAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            [request.OperationId],
            async token =>
            {
                var operation = await dbContext.PeriodicInspectionOperations
                    .Include(x => x.RuntimeContexts)
                    .SingleAsync(
                        x => x.OrganizationId == request.OrganizationId
                            && x.EnvironmentId == request.EnvironmentId
                            && x.WorkOrderId == request.WorkOrderId
                            && x.OperationId == request.OperationId,
                        token);
                var context = operation.RuntimeContexts.Single(x => x.Id == request.RuntimeContextId);
                if (!context.QuantityGenerationAnchorAtUtc.HasValue
                    || context.QuantityContinuationNextAttemptAtUtc != request.ObservedNextAttemptAtUtc)
                {
                    return;
                }

                var before = context.LastGeneratedQuantityWindowSequence;
                PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                    dbContext,
                    [context],
                    new DateTimeOffset(context.QuantityGenerationAnchorAtUtc.Value),
                    request.MaxWindows,
                    request.NextAttemptAtUtc);
                generated = checked((int)(context.LastGeneratedQuantityWindowSequence - before));
            },
            cancellationToken);
        return generated;
    }
}

public sealed record DeferPeriodicInspectionQuantityContinuationCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationId,
    PeriodicInspectionRuntimeContextId RuntimeContextId,
    DateTime ObservedNextAttemptAtUtc,
    DateTime NextAttemptAtUtc) : ICommand;

public sealed class DeferPeriodicInspectionQuantityContinuationCommandValidator
    : AbstractValidator<DeferPeriodicInspectionQuantityContinuationCommand>
{
    public DeferPeriodicInspectionQuantityContinuationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RuntimeContextId).NotEmpty();
        RuleFor(x => x.ObservedNextAttemptAtUtc.Kind).Equal(DateTimeKind.Utc);
        RuleFor(x => x.NextAttemptAtUtc.Kind).Equal(DateTimeKind.Utc);
    }
}

public sealed class DeferPeriodicInspectionQuantityContinuationCommandHandler(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator)
    : ICommandHandler<DeferPeriodicInspectionQuantityContinuationCommand>
{
    public async Task Handle(
        DeferPeriodicInspectionQuantityContinuationCommand request,
        CancellationToken cancellationToken)
    {
        await scopeCoordinator.ExecuteAsync(
            request.OrganizationId,
            request.EnvironmentId,
            request.WorkOrderId,
            [request.OperationId],
            async token =>
            {
                var operation = await dbContext.PeriodicInspectionOperations
                    .Include(x => x.RuntimeContexts)
                    .SingleAsync(
                        x => x.OrganizationId == request.OrganizationId
                            && x.EnvironmentId == request.EnvironmentId
                            && x.WorkOrderId == request.WorkOrderId
                            && x.OperationId == request.OperationId,
                        token);
                var context = operation.RuntimeContexts.Single(x => x.Id == request.RuntimeContextId);
                if (context.QuantityGenerationAnchorAtUtc.HasValue
                    && context.QuantityContinuationNextAttemptAtUtc == request.ObservedNextAttemptAtUtc)
                {
                    context.DeferQuantityContinuation(request.NextAttemptAtUtc);
                }
            },
            cancellationToken);
    }
}
