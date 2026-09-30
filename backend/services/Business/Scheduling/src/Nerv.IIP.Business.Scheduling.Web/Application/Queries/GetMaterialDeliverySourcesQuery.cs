using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.Services;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record GetMaterialDeliverySourcesQuery(
    string PlanId, string OrganizationId, string EnvironmentId,
    IReadOnlyCollection<MaterialDeliverySourceSelection> Sources) : IQuery<MaterialDeliverySourcesResponse>;

public sealed class GetMaterialDeliverySourcesQueryValidator : AbstractValidator<GetMaterialDeliverySourcesQuery>
{
    public GetMaterialDeliverySourcesQueryValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Sources).NotEmpty();
        RuleForEach(x => x.Sources).ChildRules(source =>
        {
            source.RuleFor(x => x.SuggestionId).NotEmpty();
            source.RuleFor(x => x.DueSources).NotEmpty();
            source.RuleForEach(x => x.DueSources).ChildRules(due => due.RuleFor(x => x.SourceReference).NotEmpty());
        });
    }
}

public sealed class GetMaterialDeliverySourcesQueryHandler(
    ApplicationDbContext dbContext,
    ISender sender,
    IMaterialDeliveryMesSourceProvider mesSource,
    ISchedulingProblemProductEngineeringClient engineering,
    ISchedulingProblemMasterDataClient masterData)
    : IQueryHandler<GetMaterialDeliverySourcesQuery, MaterialDeliverySourcesResponse>
{
    public async Task<MaterialDeliverySourcesResponse> Handle(GetMaterialDeliverySourcesQuery request, CancellationToken cancellationToken)
    {
        var plan = await sender.Send(new GetSchedulePlanDetailQuery(request.PlanId, request.OrganizationId, request.EnvironmentId), cancellationToken);
        var snapshot = plan.ValidationContext is null ? null : await dbContext.ScheduleProblems.AsNoTracking()
            .SingleAsync(x => x.ProblemId == plan.ProblemId && x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId, cancellationToken);
        var problem = snapshot is null ? null : JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
        var items = new List<MaterialDeliveryOrderSourceContract>();
        foreach (var source in request.Sources)
        {
            if (source.WorkOrderId is null)
            {
                items.Add(Missing(source, "work-order-not-linked"));
                continue;
            }
            var execution = await mesSource.GetAsync(request.OrganizationId, request.EnvironmentId, source.WorkOrderId, cancellationToken);
            if (execution is null)
            {
                items.Add(Missing(source, "work-order-not-found"));
                continue;
            }
            if (execution.SuggestionId != source.SuggestionId)
                throw new KnownException("计划建议与 MES 工单来源不匹配，请检查下游单据关联。");
            if (plan.ValidationContext is null)
            {
                items.Add(Missing(source, "problem-snapshot-missing"));
                continue;
            }
            var contexts = plan.ValidationContext.Operations.Where(x => x.OrderId == source.WorkOrderId)
                .ToDictionary(x => x.OperationId, StringComparer.Ordinal);
            if (contexts.Count == 0)
            {
                items.Add(Missing(source, "order-not-in-plan"));
                continue;
            }
            if (execution.ProductionVersionId is null || execution.Operations.Count == 0 ||
                execution.Operations.Any(x => !contexts.ContainsKey(x.OperationId)))
            {
                items.Add(Missing(source, "remaining-route-missing"));
                continue;
            }
            var version = await engineering.GetProductionVersionRoutingAsync(request.OrganizationId, request.EnvironmentId,
                execution.ProductionVersionId, cancellationToken);
            var routing = await engineering.GetRoutingAsync(request.OrganizationId, request.EnvironmentId, version.RoutingVersionId, cancellationToken);
            var routeBySequence = routing.Operations.ToDictionary(x => x.Sequence);
            if (execution.Operations.Any(x => !routeBySequence.ContainsKey(x.Sequence)))
            {
                items.Add(Missing(source, "remaining-route-missing"));
                continue;
            }
            var centers = new Dictionary<string, SchedulingProblemWorkCenterSnapshot>(StringComparer.Ordinal);
            foreach (var code in routing.Operations.Select(x => x.WorkCenterCode).Distinct(StringComparer.Ordinal))
                centers[code] = await masterData.GetWorkCenterAsync(request.OrganizationId, request.EnvironmentId, code, cancellationToken);
            var inputOperations = problem!.Orders.Single(x => x.OrderId == source.WorkOrderId).Operations
                .ToDictionary(x => x.OperationId, StringComparer.Ordinal);
            var operations = execution.Operations.Select(operation =>
            {
                var route = routeBySequence[operation.Sequence];
                var remaining = operation.Status == "completed" ? 0m : Math.Max(0m, execution.Quantity - operation.NetGoodQuantity);
                // 与 SchedulingProblemProducer 同一加工/效率口径；已开工工序不再计准备，完成工序为零。
                var minutes = remaining == 0 ? 0 :
                    (double)Math.Ceiling(route.RunMinutes * remaining / centers[route.WorkCenterCode].EfficiencyRate) +
                    route.TeardownMinutes + (operation.StartedAtUtc.HasValue ? 0 : route.SetupMinutes);
                var assignment = plan.Assignments.Where(x => x.OrderId == source.WorkOrderId && x.OperationId == operation.OperationId)
                    .OrderBy(x => x.StartUtc).FirstOrDefault();
                return new MaterialDeliveryOperationSourceContract(operation.OperationId, operation.Sequence, operation.Status,
                    operation.NetGoodQuantity, remaining, minutes, inputOperations[operation.OperationId].EarliestStartUtc,
                    assignment?.StartUtc, assignment is null ? "unscheduled" : "scheduled",
                    contexts[operation.OperationId].PredecessorOperationIds, version.RoutingVersionId);
            }).ToArray();
            var bounds = MaterialDeliveryTimeBoundCalculator.Calculate(source.DueSources.Select(due =>
                new MaterialDeliveryTimeSource(due.SourceReference, due.DueUtc,
                    operations.Select(x => new RemainingRoutingOperation(x.OperationId, TimeSpan.FromMinutes(x.RemainingMinutes),
                        x.PredecessorOperationIds)).ToArray())).ToArray());
            var remainingOperations = operations.Where(x => x.RemainingMinutes > 0).ToArray();
            // 有剩余工序未排时不能用其它已排工序的开始时间冒充整体可开工时间。
            var start = remainingOperations.Length == 0 || remainingOperations.Any(x => x.AssignmentStartUtc is null)
                ? (DateTimeOffset?)null : remainingOperations.Min(x => x.AssignmentStartUtc);
            items.Add(new MaterialDeliveryOrderSourceContract(source.SuggestionId, source.WorkOrderId,
                remainingOperations.Length == 0 ? "completed" : start.HasValue ? "scheduled" : "unscheduled", start,
                bounds.TightestBound.LatestStartUtc, bounds.TightestBound.SourceReference, operations,
                bounds.SourceBounds.Select(x => new MaterialDeliveryBoundContract(x.SourceReference, x.DueUtc,
                    x.RemainingDuration.TotalMinutes, x.LatestStartUtc, x.CriticalPath.Select(op => op.OperationId).ToArray())).ToArray()));
        }
        return new MaterialDeliverySourcesResponse(request.PlanId, items);
    }

    private static MaterialDeliveryOrderSourceContract Missing(MaterialDeliverySourceSelection source, string status) =>
        new(source.SuggestionId, source.WorkOrderId, status, null, null, null, [], []);
}
