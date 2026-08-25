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
                if (!context.QuantityGenerationAnchorAtUtc.HasValue)
                {
                    return;
                }

                var before = context.LastGeneratedQuantityWindowSequence;
                PeriodicInspectionQuantityTaskGeneration.AddDueTasks(
                    dbContext,
                    [context],
                    new DateTimeOffset(context.QuantityGenerationAnchorAtUtc.Value),
                    request.MaxWindows);
                generated = checked((int)(context.LastGeneratedQuantityWindowSequence - before));
            },
            cancellationToken);
        return generated;
    }
}
