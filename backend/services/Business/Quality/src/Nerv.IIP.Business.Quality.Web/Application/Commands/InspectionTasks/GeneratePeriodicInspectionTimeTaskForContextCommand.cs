using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.InspectionTaskAggregate;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;

public sealed record GeneratePeriodicInspectionTimeTaskForContextCommand(
    string OrganizationId,
    string EnvironmentId,
    string WorkOrderId,
    string OperationId,
    PeriodicInspectionRuntimeContextId RuntimeContextId,
    DateTime NowUtc,
    int MaxWindows) : ICommand<int>;

public sealed class GeneratePeriodicInspectionTimeTaskForContextCommandValidator
    : AbstractValidator<GeneratePeriodicInspectionTimeTaskForContextCommand>
{
    public GeneratePeriodicInspectionTimeTaskForContextCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.WorkOrderId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.OperationId).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RuntimeContextId).NotEmpty();
        RuleFor(x => x.NowUtc.Kind).Equal(DateTimeKind.Utc);
        RuleFor(x => x.MaxWindows).InclusiveBetween(1, 1000);
    }
}

public sealed class GeneratePeriodicInspectionTimeTaskForContextCommandHandler(
    ApplicationDbContext dbContext,
    IPeriodicInspectionOperationScopeCoordinator scopeCoordinator)
    : ICommandHandler<GeneratePeriodicInspectionTimeTaskForContextCommand, int>
{
    public async Task<int> Handle(
        GeneratePeriodicInspectionTimeTaskForContextCommand request,
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
                // The candidate list is only a hint. Reloading in this command's fresh scope after
                // acquiring the advisory lock makes a competing scanner observe the committed watermark.
                var operation = await dbContext.PeriodicInspectionOperations
                    .Include(x => x.RuntimeContexts)
                    .SingleAsync(
                        x => x.OrganizationId == request.OrganizationId
                            && x.EnvironmentId == request.EnvironmentId
                            && x.WorkOrderId == request.WorkOrderId
                            && x.OperationId == request.OperationId,
                        token);
                var context = operation.RuntimeContexts.Single(x => x.Id == request.RuntimeContextId);
                var windows = context.TakeDueTimeWindows(request.NowUtc, request.MaxWindows);
                foreach (var window in windows)
                {
                    AddTask(context, window);
                }

                generated = windows.Count;
            },
            cancellationToken);

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
            sourceDocumentLineId: $"{context.OperationId}:periodic-time:{context.Id.Id:D}:{window.Sequence}",
            skuCode: context.SkuCode,
            quantity: context.QuantityHighWater,
            uomCode: context.UomCode!,
            batchNo: null,
            serialNo: null,
            createdAtUtc,
            dueAtUtc: createdAtUtc.AddHours(24),
            triggerIdempotencyKey: $"quality:periodic-time:{context.Id.Id:D}:{window.Sequence}");
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
}
