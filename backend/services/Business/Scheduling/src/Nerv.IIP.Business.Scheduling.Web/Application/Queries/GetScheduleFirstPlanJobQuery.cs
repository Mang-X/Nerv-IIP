using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleFirstPlanJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record GetScheduleFirstPlanJobQuery(ScheduleFirstPlanJobId JobId, string OrganizationId, string EnvironmentId)
    : IQuery<SchedulingFirstPlanJobContract>;
public sealed class GetScheduleFirstPlanJobQueryValidator : AbstractValidator<GetScheduleFirstPlanJobQuery>
{
    public GetScheduleFirstPlanJobQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}
public sealed class GetScheduleFirstPlanJobQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetScheduleFirstPlanJobQuery, SchedulingFirstPlanJobContract>
{
    public async Task<SchedulingFirstPlanJobContract> Handle(GetScheduleFirstPlanJobQuery request, CancellationToken ct) =>
        ScheduleFirstPlanJobMapper.ToContract(await db.ScheduleFirstPlanJobs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.JobId && x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId, ct)
            ?? throw new KnownException("未找到首版排程作业，请刷新后重试。"));
}
