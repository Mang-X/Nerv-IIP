using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkillAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record SkillItem(
    string SkillCode,
    string SkillName,
    string GroupName,
    bool RequiresCertification,
    int? ValidityMonths,
    string? Description,
    bool Enabled,
    string SnapshotVersion);

public sealed record SkillListResponse(IReadOnlyCollection<SkillItem> Items, int Total);

public sealed record ListSkillsQuery(
    string OrganizationId,
    string EnvironmentId,
    bool? Enabled = null,
    string? Search = null,
    string? GroupName = null,
    int Skip = 0,
    int Take = 100) : IQuery<SkillListResponse>;

public sealed record GetSkillQuery(
    string OrganizationId,
    string EnvironmentId,
    string SkillCode) : IQuery<SkillItem>;

public sealed class ListSkillsQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListSkillsQuery, SkillListResponse>
{
    public async Task<SkillListResponse> Handle(ListSkillsQuery request, CancellationToken cancellationToken)
    {
        var keyword = NormalizeKeyword(request.Search);
        var query = dbContext.Skills
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => !request.Enabled.HasValue || x.Disabled != request.Enabled.Value)
            .Where(x => string.IsNullOrWhiteSpace(request.GroupName) || x.GroupName == request.GroupName)
            .Where(x => keyword == null || x.SkillCode.ToLower().Contains(keyword) || x.SkillName.ToLower().Contains(keyword) || x.GroupName.ToLower().Contains(keyword));
        var total = await query.CountAsync(cancellationToken);
        var skills = await query
            .OrderBy(x => x.SkillCode)
            .Skip(Math.Max(0, request.Skip))
            .Take(Math.Clamp(request.Take, 1, 500))
            .ToListAsync(cancellationToken);

        return new SkillListResponse(skills.Select(ToItem).ToArray(), total);
    }

    internal static SkillItem ToItem(Skill skill)
    {
        return new SkillItem(
            skill.SkillCode,
            skill.SkillName,
            skill.GroupName,
            skill.RequiresCertification,
            skill.ValidityMonths,
            skill.Description,
            !skill.Disabled,
            skill.UpdatedAtUtc.ToString("O"));
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim().ToLowerInvariant();
    }
}

public sealed class GetSkillQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetSkillQuery, SkillItem>
{
    public async Task<SkillItem> Handle(GetSkillQuery request, CancellationToken cancellationToken)
    {
        var skill = await dbContext.Skills.AsNoTracking().SingleOrDefaultAsync(x =>
            x.OrganizationId == request.OrganizationId &&
            x.EnvironmentId == request.EnvironmentId &&
            x.SkillCode == request.SkillCode,
            cancellationToken)
            ?? throw new KnownException($"Skill '{request.SkillCode}' was not found.");
        return ListSkillsQueryHandler.ToItem(skill);
    }
}
