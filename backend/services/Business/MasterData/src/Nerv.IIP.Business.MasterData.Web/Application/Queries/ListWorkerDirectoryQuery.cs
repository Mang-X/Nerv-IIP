using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkerAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record WorkerSkillItem(string SkillCode, string SkillName, string Level);

public sealed record WorkerTeamItem(string TeamCode, string TeamName, bool IsLeader, string? WorkshopCode);

public sealed record WorkerDirectoryItem(
    string UserId,
    string EmployeeNo,
    string Name,
    string? DepartmentCode,
    string? DepartmentName,
    string? JobTitle,
    string EmploymentStatus,
    string? Phone,
    bool Active,
    IReadOnlyCollection<WorkerTeamItem> Teams,
    IReadOnlyCollection<WorkerSkillItem> Skills,
    string SnapshotVersion);

public sealed record ListWorkerDirectoryResponse(
    IReadOnlyCollection<WorkerDirectoryItem> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

/// <summary>
/// Worker directory read model used by the worker maintenance page and by MES dispatch candidate
/// selection. Teams are workshop-level, so <paramref name="WorkCenterCode"/> is resolved through the
/// work center's workshop: work center -> workshop -> that workshop's teams -> their current members.
/// <paramref name="SkillCode"/> narrows to workers holding a currently valid skill.
/// </summary>
public sealed record ListWorkerDirectoryQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword = null,
    string? UserId = null,
    string? DepartmentCode = null,
    string? TeamCode = null,
    string? WorkshopCode = null,
    string? WorkCenterCode = null,
    string? SkillCode = null,
    string? EmploymentStatus = null,
    bool IncludeDisabled = false,
    int PageIndex = 1,
    int PageSize = 50) : IQuery<ListWorkerDirectoryResponse>;

public sealed class ListWorkerDirectoryQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListWorkerDirectoryQuery, ListWorkerDirectoryResponse>
{
    public async Task<ListWorkerDirectoryResponse> Handle(ListWorkerDirectoryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var workers = dbContext.Workers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.IncludeDisabled || !x.Disabled);

        var keyword = Normalize(request.Keyword)?.ToLowerInvariant();
        if (keyword is not null)
        {
            workers = workers.Where(x =>
                x.Code.ToLower().Contains(keyword) ||
                x.Name.ToLower().Contains(keyword) ||
                x.UserId.ToLower().Contains(keyword));
        }

        var userId = Normalize(request.UserId);
        if (userId is not null)
        {
            workers = workers.Where(x => x.UserId == userId);
        }

        var departmentCode = Normalize(request.DepartmentCode);
        if (departmentCode is not null)
        {
            workers = workers.Where(x => x.DepartmentCode == departmentCode);
        }

        var employmentStatus = Normalize(request.EmploymentStatus);
        if (employmentStatus is not null)
        {
            var normalizedStatus = Worker.NormalizeStatus(employmentStatus);
            workers = workers.Where(x => x.EmploymentStatus == normalizedStatus);
        }

        // Team membership drives the team / workshop / work-center filters. Teams are workshop-level
        // (one shift crew covers every work center in its workshop), so a work center resolves to its
        // workshop first and the candidates are the members of that workshop's teams.
        var memberships = dbContext.TeamMembers
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => !x.Disabled)
            .Where(x => x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today));

        var teamCode = Normalize(request.TeamCode);
        var workshopCode = Normalize(request.WorkshopCode);
        var workCenterCode = Normalize(request.WorkCenterCode);
        if (workCenterCode is not null && workshopCode is null)
        {
            workshopCode = await dbContext.WorkCenters
                .AsNoTracking()
                .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
                .Where(x => x.Code == workCenterCode)
                .Select(x => x.WorkshopCode)
                .FirstOrDefaultAsync(cancellationToken);

            // 工作中心没挂车间时不能把过滤悄悄降级成「全厂」——按空候选处理，由前端给出显式出路。
            if (string.IsNullOrWhiteSpace(workshopCode))
            {
                return new ListWorkerDirectoryResponse([], 0, pageIndex, pageSize);
            }
        }

        if (teamCode is not null || workshopCode is not null)
        {
            var scopedTeams = dbContext.Teams
                .AsNoTracking()
                .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
                .Where(x => !x.Disabled);
            if (teamCode is not null)
            {
                scopedTeams = scopedTeams.Where(x => x.Code == teamCode);
            }

            if (workshopCode is not null)
            {
                scopedTeams = scopedTeams.Where(x => x.WorkshopCode == workshopCode);
            }

            var scopedTeamCodes = scopedTeams.Select(x => x.Code);
            var scopedUserIds = memberships
                .Where(x => scopedTeamCodes.Contains(x.TeamCode))
                .Select(x => x.UserId);
            workers = workers.Where(x => scopedUserIds.Contains(x.UserId));
        }

        var validSkills = dbContext.PersonnelSkills
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => !x.Disabled)
            .Where(x => x.EffectiveFrom <= today && x.EffectiveTo >= today);

        var skillCode = Normalize(request.SkillCode);
        if (skillCode is not null)
        {
            var skilledUserIds = validSkills.Where(x => x.SkillCode == skillCode).Select(x => x.UserId);
            workers = workers.Where(x => skilledUserIds.Contains(x.UserId));
        }

        var totalCount = await workers.CountAsync(cancellationToken);
        var page = await workers
            .OrderBy(x => x.Code)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.UserId,
                x.Code,
                x.Name,
                x.DepartmentCode,
                x.JobTitle,
                x.EmploymentStatus,
                x.Phone,
                x.Disabled,
                x.UpdatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return new ListWorkerDirectoryResponse([], totalCount, pageIndex, pageSize);
        }

        var userIds = page.Select(x => x.UserId).ToArray();
        var teamRows = await memberships
            .Where(x => userIds.Contains(x.UserId))
            .Join(
                dbContext.Teams.AsNoTracking().Where(t =>
                    t.OrganizationId == request.OrganizationId &&
                    t.EnvironmentId == request.EnvironmentId &&
                    !t.Disabled),
                member => member.TeamCode,
                team => team.Code,
                (member, team) => new
                {
                    member.UserId,
                    team.Code,
                    team.Name,
                    member.IsLeader,
                    team.WorkshopCode,
                })
            .ToListAsync(cancellationToken);

        var skillRows = await validSkills
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.SkillCode, x.Level })
            .ToListAsync(cancellationToken);
        var skillCodes = skillRows.Select(x => x.SkillCode).Distinct(StringComparer.Ordinal).ToArray();
        var skillNames = await dbContext.Skills
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => skillCodes.Contains(x.SkillCode))
            .Select(x => new { x.SkillCode, x.SkillName })
            .ToListAsync(cancellationToken);
        var skillNameByCode = skillNames.ToDictionary(x => x.SkillCode, x => x.SkillName, StringComparer.Ordinal);

        var departmentCodes = page
            .Where(x => x.DepartmentCode != null)
            .Select(x => x.DepartmentCode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var departmentNames = await dbContext.Departments
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => departmentCodes.Contains(x.Code))
            .Select(x => new { x.Code, x.Name })
            .ToListAsync(cancellationToken);
        var departmentNameByCode = departmentNames.ToDictionary(x => x.Code, x => x.Name, StringComparer.Ordinal);

        var items = page
            .Select(worker => new WorkerDirectoryItem(
                worker.UserId,
                worker.Code,
                worker.Name,
                worker.DepartmentCode,
                worker.DepartmentCode is not null && departmentNameByCode.TryGetValue(worker.DepartmentCode, out var departmentName)
                    ? departmentName
                    : null,
                worker.JobTitle,
                worker.EmploymentStatus,
                worker.Phone,
                !worker.Disabled,
                teamRows
                    .Where(team => string.Equals(team.UserId, worker.UserId, StringComparison.Ordinal))
                    .OrderByDescending(team => team.IsLeader)
                    .ThenBy(team => team.Code, StringComparer.Ordinal)
                    .Select(team => new WorkerTeamItem(team.Code, team.Name, team.IsLeader, team.WorkshopCode))
                    .ToArray(),
                skillRows
                    .Where(skill => string.Equals(skill.UserId, worker.UserId, StringComparison.Ordinal))
                    .OrderBy(skill => skill.SkillCode, StringComparer.Ordinal)
                    .Select(skill => new WorkerSkillItem(
                        skill.SkillCode,
                        skillNameByCode.TryGetValue(skill.SkillCode, out var name) ? name : skill.SkillCode,
                        skill.Level))
                    .ToArray(),
                worker.UpdatedAtUtc.ToString("O")))
            .ToArray();

        return new ListWorkerDirectoryResponse(items, totalCount, pageIndex, pageSize);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
