using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Scheduling.Domain.AggregatesModel.ScheduleInsertionPreviewJobAggregate;
using Nerv.IIP.Business.Scheduling.Web.Application.Commands;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record GetScheduleInsertionPreviewJobQuery(ScheduleInsertionPreviewJobId JobId, string OrganizationId, string EnvironmentId)
    : IQuery<SchedulingInsertionPreviewJobContract>;
public sealed class GetScheduleInsertionPreviewJobQueryValidator : AbstractValidator<GetScheduleInsertionPreviewJobQuery>
{
    public GetScheduleInsertionPreviewJobQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
    }
}
public sealed class GetScheduleInsertionPreviewJobQueryHandler(ApplicationDbContext db)
    : IQueryHandler<GetScheduleInsertionPreviewJobQuery, SchedulingInsertionPreviewJobContract>
{
    public async Task<SchedulingInsertionPreviewJobContract> Handle(GetScheduleInsertionPreviewJobQuery request, CancellationToken ct) =>
        ScheduleInsertionPreviewJobMapper.ToContract(await db.ScheduleInsertionPreviewJobs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.JobId && x.OrganizationId == request.OrganizationId &&
                x.EnvironmentId == request.EnvironmentId, ct)
            ?? throw new KnownException("未找到插单预览任务，请刷新后重试。"));
}
