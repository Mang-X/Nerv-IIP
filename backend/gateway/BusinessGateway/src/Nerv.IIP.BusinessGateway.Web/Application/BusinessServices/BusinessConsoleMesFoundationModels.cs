namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleMesContextRequest(
    string OrganizationId,
    string EnvironmentId);

public sealed record BusinessConsoleMesFoundationReadinessRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode,
    string? LineCode,
    string? WorkCenterCode,
    string? SkuId,
    string? ProductionVersionId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record BusinessConsoleMesFoundationReadinessResponse(
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessArea> Areas,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> BlockingIssues,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> WarningIssues);

public sealed record BusinessConsoleMesReadinessArea(
    string AreaCode,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> Issues);

public sealed record BusinessConsoleMesReadinessIssue(
    string Code,
    string Severity,
    string Message,
    string? SourceSystem,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceDisplayName,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string? Version,
    string? FixHint);

public sealed record BusinessConsoleMesOverviewResponse(
    IReadOnlyCollection<BusinessConsoleMesCockpitCount> Counts,
    IReadOnlyCollection<BusinessConsoleMesBlockerSummary> Blockers,
    IReadOnlyCollection<BusinessConsoleMesPendingWorkItem> PendingWork);

public sealed record BusinessConsoleMesCockpitCount(string Key, int Count, string Status);

public sealed record BusinessConsoleMesBlockerSummary(string AreaCode, string Code, string Message, int Count);

public sealed record BusinessConsoleMesPendingWorkItem(string RoleCode, string WorkType, int Count, string? RouteHint);
