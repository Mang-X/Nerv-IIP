using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

public sealed record GeneratePeriodicInspectionTimeTasksCommand(
    string OrganizationId,
    string EnvironmentId,
    int MaxWindowsPerContext,
    int ContextBatchSize) : ICommand<int>;

public sealed class GeneratePeriodicInspectionTimeTasksCommandValidator
    : AbstractValidator<GeneratePeriodicInspectionTimeTasksCommand>
{
    public GeneratePeriodicInspectionTimeTasksCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaxWindowsPerContext).InclusiveBetween(1, 1000);
        RuleFor(x => x.ContextBatchSize).InclusiveBetween(1, 1000);
    }
}

public sealed class GeneratePeriodicInspectionTimeTasksCommandHandler(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator,
    TimeProvider timeProvider)
    : ICommandHandler<GeneratePeriodicInspectionTimeTasksCommand, int>
{
    public async Task<int> Handle(
        GeneratePeriodicInspectionTimeTasksCommand request,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await dbContext.PeriodicInspectionRuntimeContexts
            .AsNoTracking()
            .Where(context =>
                context.OrganizationId == request.OrganizationId
                && context.EnvironmentId == request.EnvironmentId
                && context.Status == "active"
                && context.TimeIntervalHours != null
                && context.NextTimeWindowAtUtc != null
                && context.NextTimeWindowAtUtc <= nowUtc
                && context.UomCode != null
                && context.QuantityHighWater > 0m)
            .OrderBy(context => context.NextTimeWindowAtUtc)
            .ThenBy(context => context.WorkOrderId)
            .ThenBy(context => context.OperationId)
            .ThenBy(context => context.Id)
            .Select(context => new Candidate(
                context.WorkOrderId,
                context.OperationId,
                context.Id))
            .Take(request.ContextBatchSize)
            .ToArrayAsync(cancellationToken);

        var generated = 0;
        foreach (var candidate in candidates)
        {
            await scopeCoordinator.ExecuteAsync(
                request.OrganizationId,
                request.EnvironmentId,
                candidate.WorkOrderId,
                [candidate.OperationId],
                async token =>
                {
                    var operation = await dbContext.PeriodicInspectionOperations
                        .Include(x => x.RuntimeContexts)
                        .SingleAsync(
                            x => x.OrganizationId == request.OrganizationId
                                && x.EnvironmentId == request.EnvironmentId
                                && x.WorkOrderId == candidate.WorkOrderId
                                && x.OperationId == candidate.OperationId,
                            token);
                    var context = operation.RuntimeContexts.Single(x => x.Id == candidate.RuntimeContextId);
                    var windows = context.TakeDueTimeWindows(nowUtc, request.MaxWindowsPerContext);
                    foreach (var window in windows)
                    {
                        AddTask(context, window);
                    }

                    generated += windows.Count;
                },
                cancellationToken);
        }

        return generated;
    }

    private void AddTask(PeriodicInspectionRuntimeContext context, PeriodicInspectionTimeWindow window)
    {
        var createdAtUtc = new DateTimeOffset(window.DueAtUtc);
        var task = InspectionTask.CreatePending(
            context.OrganizationId,
            context.EnvironmentId,
            context.InspectionPlanId,
            sourceType: "operation",
            sourceService: "mes",
            sourceDocumentId: context.WorkOrderId,
            sourceDocumentLineId: $"{context.OperationId}:periodic-time:{window.Sequence}",
            skuCode: context.SkuCode,
            quantity: context.QuantityHighWater,
            uomCode: context.UomCode!,
            batchNo: null,
            serialNo: null,
            createdAtUtc,
            dueAtUtc: createdAtUtc.AddHours(24),
            triggerIdempotencyKey: $"quality:periodic-time:{context.Id}:{window.Sequence}");
        if (context.AssignedInspectorUserId is not null || context.AssignedTeamId is not null)
        {
            task.Assign(
                context.AssignedInspectorUserId,
                context.AssignedTeamId,
                task.Version,
                createdAtUtc);
        }

        dbContext.InspectionTasks.Add(task);
    }

    private sealed record Candidate(
        string WorkOrderId,
        string OperationId,
        PeriodicInspectionRuntimeContextId RuntimeContextId);
}
