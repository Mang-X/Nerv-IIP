using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record TeamMemberItem(
    string TeamCode,
    string UserId,
    bool IsLeader,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool Active,
    string SnapshotVersion);

public sealed record ListTeamMembersResponse(IReadOnlyCollection<TeamMemberItem> Members, int Total);

public sealed record ListTeamMembersQuery(
    string OrganizationId,
    string EnvironmentId,
    string TeamCode,
    bool IncludeDisabled = false) : IQuery<ListTeamMembersResponse>;

public sealed class ListTeamMembersQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListTeamMembersQuery, ListTeamMembersResponse>
{
    public async Task<ListTeamMembersResponse> Handle(ListTeamMembersQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.TeamMembers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.TeamCode == request.TeamCode)
            .Where(x => request.IncludeDisabled || !x.Disabled)
            .OrderByDescending(x => x.IsLeader)
            .ThenBy(x => x.UserId)
            .Select(x => new TeamMemberItem(
                x.TeamCode,
                x.UserId,
                x.IsLeader,
                x.EffectiveFrom,
                x.EffectiveTo,
                !x.Disabled,
                x.UpdatedAtUtc.ToString("O")));

        var members = await query.ToListAsync(cancellationToken);
        return new ListTeamMembersResponse(members, members.Count);
    }
}
