using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Quality.Infrastructure;

namespace Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;

public sealed record InspectionTaskResponse(
    Domain.AggregatesModel.InspectionTaskAggregate.InspectionTaskId InspectionTaskId,
    Domain.AggregatesModel.InspectionPlanAggregate.InspectionPlanId InspectionPlanId,
    string SourceType,
    string SourceService,
    string SourceDocumentId,
    string? SourceDocumentLineId,
    string SkuCode,
    decimal Quantity,
    string UomCode,
    string? BatchNo,
    string? SerialNo,
    string Status,
    string? AssignedInspectorUserId,
    string? AssignedTeamId,
    long Version,
    bool IsOverdue,
    IReadOnlyCollection<string> AllowedActions,
    IReadOnlyCollection<string> BlockReasons,
    DateTimeOffset DueAtUtc,
    DateTimeOffset CreatedAtUtc,
    Domain.AggregatesModel.InspectionRecordAggregate.InspectionRecordId? InspectionRecordId);

public sealed record ListInspectionTasksResponse(IReadOnlyCollection<InspectionTaskResponse> Items, int Total);

public sealed record ListInspectionTasksQuery(
    string OrganizationId,
    string EnvironmentId,
    string? Status,
    string? SkuCode,
    int Skip = 0,
    int Take = 100,
    Domain.AggregatesModel.InspectionTaskAggregate.InspectionTaskId? InspectionTaskId = null,
    string? ScopeKind = null,
    string? PrincipalId = null,
    IReadOnlyCollection<string>? AuthorizedTeamIds = null,
    string? SourceType = null,
    string? SourceService = null,
    string? Keyword = null,
    bool? Overdue = null,
    DateTimeOffset? AsOfUtc = null)
    : IQuery<ListInspectionTasksResponse>;

public sealed class ListInspectionTasksQueryValidator : AbstractValidator<ListInspectionTasksQuery>
{
    public ListInspectionTasksQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EnvironmentId).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Take).InclusiveBetween(1, 200);
        RuleFor(x => x.ScopeKind)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("self", StringComparison.OrdinalIgnoreCase)
                || value.Equals("team", StringComparison.OrdinalIgnoreCase)
                || value.Equals("organization", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Scope kind must be self, team or organization.");
    }
}

public sealed class ListInspectionTasksQueryHandler(ApplicationDbContext dbContext)
    : IQueryHandler<ListInspectionTasksQuery, ListInspectionTasksResponse>
{
    public async Task<ListInspectionTasksResponse> Handle(ListInspectionTasksQuery request, CancellationToken cancellationToken)
    {
        var query = dbContext.InspectionTasks
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId)
            .Where(x => request.InspectionTaskId == null || x.Id == request.InspectionTaskId);
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.SkuCode))
        {
            var skuCode = request.SkuCode.Trim();
            query = query.Where(x => x.SkuCode == skuCode);
        }

        var scopeKind = request.ScopeKind?.Trim().ToLowerInvariant();
        var principalId = request.PrincipalId?.Trim();
        if (scopeKind == "self")
        {
            query = query.Where(x => x.AssignedUserId == principalId);
        }
        else if (scopeKind == "team")
        {
            var teamIds = (request.AuthorizedTeamIds ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            query = query.Where(x => x.AssignedTeamId != null && teamIds.Contains(x.AssignedTeamId));
        }

        if (!string.IsNullOrWhiteSpace(request.SourceType))
        {
            var sourceType = request.SourceType.Trim().ToLowerInvariant();
            query = query.Where(x => x.SourceType == sourceType);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceService))
        {
            var sourceService = request.SourceService.Trim().ToLowerInvariant();
            query = query.Where(x => x.SourceService == sourceService);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.SourceDocumentId.ToLower().Contains(keyword)
                || (x.SourceDocumentLineId != null && x.SourceDocumentLineId.ToLower().Contains(keyword))
                || x.SkuCode.ToLower().Contains(keyword)
                || (x.BatchNo != null && x.BatchNo.ToLower().Contains(keyword))
                || (x.SerialNo != null && x.SerialNo.ToLower().Contains(keyword)));
        }

        var asOfUtc = request.AsOfUtc ?? DateTimeOffset.UtcNow;
        if (request.Overdue is not null)
        {
            query = request.Overdue.Value
                ? query.Where(x => x.Status != "completed" && x.DueAtUtc < asOfUtc)
                : query.Where(x => x.Status == "completed" || x.DueAtUtc >= asOfUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(x => x.Status == "completed")
            .ThenBy(x => x.DueAtUtc)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(x => new
            {
                x.Id,
                x.InspectionPlanId,
                x.SourceType,
                x.SourceService,
                x.SourceDocumentId,
                x.SourceDocumentLineId,
                x.SkuCode,
                x.Quantity,
                x.UomCode,
                x.BatchNo,
                x.SerialNo,
                x.Status,
                x.AssignedUserId,
                x.AssignedTeamId,
                x.Version,
                x.DueAtUtc,
                x.CreatedAtUtc,
                x.InspectionRecordId,
            })
            .ToArrayAsync(cancellationToken);
        var authorizedTeams = request.AuthorizedTeamIds ?? [];
        var items = rows.Select(x =>
        {
            var overdue = x.Status != "completed" && x.DueAtUtc < asOfUtc;
            var actorOwnsTask = !string.IsNullOrWhiteSpace(principalId)
                && string.Equals(x.AssignedUserId, principalId, StringComparison.Ordinal);
            var actorOwnsTeam = x.AssignedTeamId is not null
                && authorizedTeams.Contains(x.AssignedTeamId, StringComparer.Ordinal);
            string[] allowedActions = x.Status switch
            {
                "pending" when actorOwnsTask || (x.AssignedUserId is null && actorOwnsTeam) =>
                    ["claim"],
                "in-progress" when actorOwnsTask => ["submit-inspection"],
                _ => [],
            };
            string[] blockReasons = allowedActions.Length > 0
                ? []
                : x.Status == "completed"
                    ? ["task-completed"]
                    : x.AssignedUserId is not null && !actorOwnsTask
                        ? x.Status == "in-progress"
                            ? new[] { "task-already-claimed" }
                            : new[] { "task-assigned-to-another-inspector" }
                    : x.AssignedUserId is null && x.AssignedTeamId is null
                        ? ["task-unassigned"]
                        : ["task-outside-selected-work-scope"];
            return new InspectionTaskResponse(
                x.Id,
                x.InspectionPlanId,
                x.SourceType,
                x.SourceService,
                x.SourceDocumentId,
                x.SourceDocumentLineId,
                x.SkuCode,
                x.Quantity,
                x.UomCode,
                x.BatchNo,
                x.SerialNo,
                x.Status,
                x.AssignedUserId,
                x.AssignedTeamId,
                x.Version,
                overdue,
                allowedActions,
                blockReasons,
                x.DueAtUtc,
                x.CreatedAtUtc,
                x.InspectionRecordId);
        }).ToArray();

        return new ListInspectionTasksResponse(items, total);
    }
}
