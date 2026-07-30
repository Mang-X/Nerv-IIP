using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record WorkContextWorker(
    string Id,
    string UserId,
    string EmployeeNo,
    string Name,
    string? DepartmentId,
    string? DepartmentName,
    string? JobTitle,
    string EmploymentStatus);

public sealed record WorkContextTeam(
    string Id,
    string Name,
    bool IsLeader,
    string? WorkshopId,
    string ShiftId);

public sealed record WorkContextReference(string Id, string Name);

public sealed record WorkContextCoveredWorkCenter(
    string Id,
    string Name,
    string WorkshopId,
    string Relationship);

public sealed record WorkContextShift(
    string Id,
    string Name,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    bool CrossesMidnight);

public sealed record WorkContextScopeAncestor(string Kind, string Id);

public sealed record WorkContextCandidateScope(
    string Kind,
    string Id,
    string DisplayName,
    string Relationship,
    IReadOnlyCollection<WorkContextScopeAncestor> Ancestors);

public sealed record PrincipalWorkContextResponse(
    string ResolutionStatus,
    WorkContextWorker? Worker,
    IReadOnlyCollection<WorkContextTeam> Teams,
    IReadOnlyCollection<WorkContextCoveredWorkCenter> CoveredWorkCenters,
    IReadOnlyCollection<WorkContextReference> Workshops,
    IReadOnlyCollection<WorkContextShift> Shifts,
    IReadOnlyCollection<WorkContextReference> Sites,
    IReadOnlyCollection<WorkContextCandidateScope> CandidateScopes,
    IReadOnlyCollection<string> CandidateScopeKinds,
    IReadOnlyCollection<string> Issues);

public sealed record GetPrincipalWorkContextQuery(
    string OrganizationId,
    string EnvironmentId,
    string UserId) : IQuery<PrincipalWorkContextResponse>;

public sealed class GetPrincipalWorkContextQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<GetPrincipalWorkContextQuery, PrincipalWorkContextResponse>
{
    public async Task<PrincipalWorkContextResponse> Handle(
        GetPrincipalWorkContextQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organizationId = Required(request.OrganizationId);
        var environmentId = Required(request.EnvironmentId);
        var userId = Required(request.UserId);
        var workers = await dbContext.Workers
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.UserId == userId
                && !x.Disabled)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (workers.Count == 0)
        {
            return Empty("worker-not-mapped");
        }

        if (workers.Count > 1)
        {
            return Empty("worker-mapping-conflict");
        }

        var worker = workers[0];
        var departmentName = worker.DepartmentCode is null
            ? null
            : await dbContext.Departments
                .AsNoTracking()
                .Where(x =>
                    x.OrganizationId == organizationId
                    && x.EnvironmentId == environmentId
                    && x.Code == worker.DepartmentCode
                    && !x.Disabled)
                .Select(x => x.Name)
                .SingleOrDefaultAsync(cancellationToken);
        var workerItem = new WorkContextWorker(
            worker.Id.Id.ToString(),
            worker.UserId,
            worker.Code,
            worker.Name,
            worker.DepartmentCode,
            departmentName,
            worker.JobTitle,
            worker.EmploymentStatus);
        if (!string.Equals(worker.EmploymentStatus, "active", StringComparison.Ordinal))
        {
            return new PrincipalWorkContextResponse(
                "worker-inactive",
                workerItem,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                ["position-master-not-modeled", "worker-inactive"]);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var memberships = await dbContext.TeamMembers
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.UserId == userId
                && !x.Disabled
                && x.EffectiveFrom <= today
                && (x.EffectiveTo == null || x.EffectiveTo >= today))
            .ToListAsync(cancellationToken);
        var teamIds = memberships
            .Select(x => x.TeamCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var teams = await dbContext.Teams
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && teamIds.Contains(x.Code)
                && !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var leaderByTeam = memberships
            .GroupBy(x => x.TeamCode, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Any(member => member.IsLeader), StringComparer.Ordinal);

        var workshopIds = teams
            .Where(x => !string.IsNullOrWhiteSpace(x.WorkshopCode))
            .Select(x => x.WorkshopCode!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var workshops = await dbContext.Workshops
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && workshopIds.Contains(x.Code)
                && !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var resolvedWorkshopIds = workshops.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var coveredWorkCenterCandidates = await dbContext.WorkCenters
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && x.WorkshopCode != null
                && resolvedWorkshopIds.Contains(x.WorkshopCode)
                && !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var productionLineIds = coveredWorkCenterCandidates
            .Where(x => !string.IsNullOrWhiteSpace(x.LineCode))
            .Select(x => x.LineCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var productionLines = await dbContext.ProductionLines
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && productionLineIds.Contains(x.Code)
                && !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var shiftIds = teams
            .Select(x => x.ShiftCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var shifts = await dbContext.Shifts
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && shiftIds.Contains(x.Code)
                && !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var resolvedShiftIds = shifts.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);

        var siteIds = workshops
            .Select(x => x.SiteCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sites = await dbContext.Sites
            .AsNoTracking()
            .Where(x =>
                x.OrganizationId == organizationId
                && x.EnvironmentId == environmentId
                && siteIds.Contains(x.Code)
                && !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

        var resolvedTeamIds = teams.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var resolvedSiteIds = sites.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var resolvedProductionLineIds = productionLines.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        var workshopSiteById = workshops
            .Where(x => resolvedSiteIds.Contains(x.SiteCode))
            .ToDictionary(x => x.Code, x => x.SiteCode, StringComparer.Ordinal);
        var productionLineById = productionLines.ToDictionary(x => x.Code, StringComparer.Ordinal);
        var workCenterLineageIssues = new List<string>();
        var coveredWorkCenters = coveredWorkCenterCandidates
            .Where(x =>
            {
                if (!workshopSiteById.TryGetValue(x.WorkshopCode!, out var workshopSiteId))
                {
                    workCenterLineageIssues.Add($"work-center-site-not-mapped:{x.Code}:{x.WorkshopCode}");
                    return false;
                }

                if (!string.Equals(x.PlantCode, workshopSiteId, StringComparison.Ordinal))
                {
                    workCenterLineageIssues.Add($"work-center-site-lineage-conflict:{x.Code}:{x.PlantCode}:{workshopSiteId}");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(x.LineCode))
                {
                    return true;
                }

                if (!productionLineById.TryGetValue(x.LineCode, out var line))
                {
                    workCenterLineageIssues.Add($"production-line-not-mapped:{x.Code}:{x.LineCode}");
                    return false;
                }

                if (!string.Equals(line.WorkshopCode, x.WorkshopCode, StringComparison.Ordinal)
                    || !string.Equals(line.SiteCode, workshopSiteId, StringComparison.Ordinal))
                {
                    workCenterLineageIssues.Add($"production-line-lineage-conflict:{x.Code}:{x.LineCode}");
                    return false;
                }

                return true;
            })
            .ToArray();
        var workCenterWorkshopIds = coveredWorkCenters
            .Select(x => x.WorkshopCode!)
            .ToHashSet(StringComparer.Ordinal);
        var issues = new List<string> { "position-master-not-modeled" };
        if (teamIds.Length == 0)
        {
            issues.Add("team-not-assigned");
            issues.Add("shift-not-assigned");
        }
        issues.AddRange(teamIds
            .Where(x => !resolvedTeamIds.Contains(x))
            .Select(x => $"team-not-mapped:{x}"));
        issues.AddRange(teams
            .Where(x => string.IsNullOrWhiteSpace(x.WorkshopCode))
            .Select(x => $"workshop-not-mapped:{x.Code}"));
        issues.AddRange(teams
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.WorkshopCode)
                && !resolvedWorkshopIds.Contains(x.WorkshopCode))
            .Select(x => $"workshop-not-mapped:{x.Code}:{x.WorkshopCode}"));
        issues.AddRange(teams
            .Where(x => !resolvedShiftIds.Contains(x.ShiftCode))
            .Select(x => $"shift-not-mapped:{x.Code}:{x.ShiftCode}"));
        issues.AddRange(workshops
            .Where(x => !resolvedSiteIds.Contains(x.SiteCode))
            .Select(x => $"site-not-mapped:{x.Code}:{x.SiteCode}"));
        issues.AddRange(workshops
            .Where(x => !workCenterWorkshopIds.Contains(x.Code))
            .Select(x => $"work-center-not-mapped:{x.Code}"));
        issues.AddRange(workCenterLineageIssues);
        var orderedIssues = issues.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var teamItems = teams
            .Select(x => new WorkContextTeam(
                x.Code,
                x.Name,
                leaderByTeam.GetValueOrDefault(x.Code),
                x.WorkshopCode,
                x.ShiftCode))
            .ToArray();
        var workCenterItems = coveredWorkCenters
            .Select(x => new WorkContextCoveredWorkCenter(
                x.Code,
                x.Name,
                x.WorkshopCode!,
                "workshop-covered"))
            .ToArray();
        var workshopItems = workshops
            .Select(x => new WorkContextReference(x.Code, x.Name))
            .ToArray();
        var shiftItems = shifts
            .Select(x => new WorkContextShift(x.Code, x.Name, x.StartsAt, x.EndsAt, x.CrossesMidnight))
            .ToArray();
        var siteItems = sites.Select(x => new WorkContextReference(x.Code, x.Name)).ToArray();
        WorkContextScopeAncestor[] WorkshopAncestors(string? workshopId)
        {
            if (string.IsNullOrWhiteSpace(workshopId) || !resolvedWorkshopIds.Contains(workshopId))
            {
                return [];
            }

            var ancestors = new List<WorkContextScopeAncestor>
            {
                new("workshop", workshopId),
            };
            if (workshopSiteById.TryGetValue(workshopId, out var siteId))
            {
                ancestors.Add(new WorkContextScopeAncestor("site", siteId));
            }

            return ancestors.ToArray();
        }

        static WorkContextScopeAncestor[] OrderedAncestors(IEnumerable<WorkContextScopeAncestor> ancestors) =>
            ancestors
                .Distinct()
                .OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToArray();

        var candidateScopes = new List<WorkContextCandidateScope>
        {
            new(
                "self",
                worker.UserId,
                worker.Name,
                "worker-mapping",
                OrderedAncestors(teams.SelectMany(x => WorkshopAncestors(x.WorkshopCode)))),
            new("organization", organizationId, "当前组织", "principal-membership", []),
        };
        candidateScopes.AddRange(teamItems.Select(x =>
            new WorkContextCandidateScope(
                "team",
                x.Id,
                x.Name,
                "active-membership",
                OrderedAncestors(WorkshopAncestors(x.WorkshopId)))));
        candidateScopes.AddRange(coveredWorkCenters.Select(x =>
            new WorkContextCandidateScope(
                "work-center",
                x.Code,
                x.Name,
                "workshop-covered",
                OrderedAncestors(
                    WorkshopAncestors(x.WorkshopCode)
                        .Concat(
                            resolvedProductionLineIds.Contains(x.LineCode)
                                ? [new WorkContextScopeAncestor("production-line", x.LineCode)]
                                : [])))));
        candidateScopes.AddRange(workshops.Select(x =>
            new WorkContextCandidateScope(
                "workshop",
                x.Code,
                x.Name,
                "active-team-workshop",
                workshopSiteById.TryGetValue(x.Code, out var siteId)
                    ? [new WorkContextScopeAncestor("site", siteId)]
                    : [])));
        var orderedCandidates = candidateScopes
            .OrderBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        return new PrincipalWorkContextResponse(
            orderedIssues.Length == 1 ? "ready-with-gaps" : "incomplete",
            workerItem,
            teamItems,
            workCenterItems,
            workshopItems,
            shiftItems,
            siteItems,
            orderedCandidates,
            orderedCandidates.Select(x => x.Kind).Distinct(StringComparer.Ordinal).Order().ToArray(),
            orderedIssues);
    }

    private static PrincipalWorkContextResponse Empty(string issue) =>
        new(issue, null, [], [], [], [], [], [], [], [issue]);

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be blank.", nameof(value))
            : value.Trim();
}
