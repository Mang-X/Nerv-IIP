using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Infrastructure;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record GetSchedulePlanOverridesQuery(string PlanId, string OrganizationId, string EnvironmentId)
    : IQuery<IReadOnlyCollection<ScheduleOperationOverrideResponse>>;

public sealed class GetSchedulePlanOverridesQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetSchedulePlanOverridesQuery, IReadOnlyCollection<ScheduleOperationOverrideResponse>>
{
    public async Task<IReadOnlyCollection<ScheduleOperationOverrideResponse>> Handle(
        GetSchedulePlanOverridesQuery request, CancellationToken cancellationToken)
    {
        var plan = await dbContext.SchedulePlans.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId && x.PlanId == request.PlanId, cancellationToken)
            ?? throw new KnownException($"未找到排程方案，请刷新后重试，方案 ID = {request.PlanId}");
        var snapshot = await dbContext.ScheduleProblems.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId && x.ProblemId == plan.ProblemId, cancellationToken)
            ?? throw new KnownException($"未找到排程问题快照，请重新生成方案，问题 ID = {plan.ProblemId}");
        var problem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
        var keys = problem.Orders.SelectMany(order => order.Operations
            .Select(operation => (order.OrderId, operation.OperationId))).ToHashSet();
        var operationIds = keys.Select(key => key.OperationId).ToArray();
        var facts = await dbContext.ScheduleOperationOverrides.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId &&
                x.IsActive && operationIds.Contains(x.OperationId))
            .ToArrayAsync(cancellationToken);
        return facts.Where(x => keys.Contains((x.WorkOrderId, x.OperationId)))
            .OrderBy(x => x.WorkOrderId, StringComparer.Ordinal)
            .ThenBy(x => x.OperationSequence)
            .ThenBy(x => x.OperationId, StringComparer.Ordinal)
            .Select(x => new ScheduleOperationOverrideResponse(x.OperationId, x.WorkOrderId,
                x.ResourceId, x.WorkCenterId, x.StartUtc, x.EndUtc, x.LockReasonCode, x.SourcePlanId))
            .ToArray();
    }
}
