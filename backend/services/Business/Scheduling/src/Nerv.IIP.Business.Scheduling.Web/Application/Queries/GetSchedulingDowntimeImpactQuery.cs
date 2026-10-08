using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Web.Application.Scheduling;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record GetSchedulingDowntimeImpactQuery(string PlanId, string OrganizationId, string EnvironmentId)
    : IQuery<SchedulingDowntimeImpactResponse>;

public sealed class GetSchedulingDowntimeImpactQueryValidator : AbstractValidator<GetSchedulingDowntimeImpactQuery>
{
    public GetSchedulingDowntimeImpactQueryValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty().MaximumLength(128);
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetSchedulingDowntimeImpactQueryHandler(ApplicationDbContext db, ISender sender,
    ISchedulingDowntimeFactsProvider facts, ISchedulingEquipmentAvailabilityProvider equipment, TimeProvider clock)
    : IQueryHandler<GetSchedulingDowntimeImpactQuery, SchedulingDowntimeImpactResponse>
{
    public async Task<SchedulingDowntimeImpactResponse> Handle(GetSchedulingDowntimeImpactQuery request, CancellationToken ct)
    {
        var baseline = await sender.Send(new GetSchedulePlanDetailQuery(request.PlanId, request.OrganizationId, request.EnvironmentId), ct);
        var snapshot = await db.ScheduleProblems.AsNoTracking().SingleOrDefaultAsync(x => x.ProblemId == baseline.ProblemId
            && x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId, ct)
            ?? throw new KnownException("基线问题快照缺失，无法核对停机影响及工艺资格。");
        var problem = JsonSerializer.Deserialize<SchedulingProblemContract>(snapshot.ProblemJson, SchedulingJson.Options)!;
        var asOf = clock.GetUtcNow();
        var factsTask = facts.QueryAsync(problem, asOf, ct);
        var availabilityTask = equipment.QueryAsync(problem with { HorizonStartUtc = asOf, HorizonEndUtc = asOf.AddMinutes(1) }, ct);
        await Task.WhenAll(factsTask, availabilityTask);
        return SchedulingDowntimeImpactProjector.Project(problem, baseline, factsTask.Result, availabilityTask.Result, asOf);
    }
}
