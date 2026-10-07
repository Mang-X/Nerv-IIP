using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.Scheduling;

namespace Nerv.IIP.Business.Scheduling.Web.Application.Queries;

public sealed record ListScheduleWorkingDraftsQuery(string OrganizationId, string EnvironmentId, string UserId, string? PlanId = null)
    : IRequest<IReadOnlyList<SchedulingWorkingDraftContract>>;

public sealed class ListScheduleWorkingDraftsQueryValidator : AbstractValidator<ListScheduleWorkingDraftsQuery>
{
    public ListScheduleWorkingDraftsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PlanId).MaximumLength(96);
    }
}

public sealed class ListScheduleWorkingDraftsQueryHandler(ApplicationDbContext dbContext)
    : IRequestHandler<ListScheduleWorkingDraftsQuery, IReadOnlyList<SchedulingWorkingDraftContract>>
{
    public async Task<IReadOnlyList<SchedulingWorkingDraftContract>> Handle(ListScheduleWorkingDraftsQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.ScheduleWorkingDrafts.AsNoTracking().Where(x => x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId && x.UserId == request.UserId);
        if (request.PlanId is not null) query = query.Where(x => x.PlanId == request.PlanId);
        var drafts = await query.OrderByDescending(x => x.SavedAtUtc).ThenBy(x => x.PlanId).ToListAsync(cancellationToken);
        return drafts.Select(x => new SchedulingWorkingDraftContract(x.PlanId, x.SavedAtUtc,
            JsonSerializer.Deserialize<SchedulingWorkingDraftStateContract>(x.StateJson, SchedulingJson.Options)!)).ToArray();
    }
}
