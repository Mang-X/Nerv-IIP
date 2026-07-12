using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record SchedulingTransitionRequest(string OperationId, string WorkCenterCode, string FromSkuCode, string? FromProductCategoryCode, string ToSkuCode);
public sealed record ResolveSchedulingToolingFactsQuery(string OrganizationId, string EnvironmentId, IReadOnlyCollection<SchedulingTransitionRequest> Transitions) : IQuery<ResolveSchedulingToolingFactsResponse>;
public sealed record SchedulingToolingFactResponse(string OperationId, int SetupMinutes, IReadOnlyCollection<string> RequiredToolingCodes, bool ToolingAvailable);
public sealed record ResolveSchedulingToolingFactsResponse(IReadOnlyCollection<SchedulingToolingFactResponse> Facts);

public sealed class ResolveSchedulingToolingFactsQueryHandler(ApplicationDbContext dbContext) : IQueryHandler<ResolveSchedulingToolingFactsQuery, ResolveSchedulingToolingFactsResponse>
{
    public async Task<ResolveSchedulingToolingFactsResponse> Handle(ResolveSchedulingToolingFactsQuery request, CancellationToken cancellationToken)
    {
        var workCenters = request.Transitions.Select(x => x.WorkCenterCode).Distinct().ToArray();
        var entries = await dbContext.ChangeoverMatrixEntries.AsNoTracking().Include(x => x.RequiredTooling)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Active && workCenters.Contains(x.WorkCenterCode))
            .ToArrayAsync(cancellationToken);
        var tooling = await dbContext.ToolingAssets.AsNoTracking().Include(x => x.Applicability)
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && x.Status == ToolingAssetStatus.Available)
            .ToArrayAsync(cancellationToken);
        var fromSkuCodes = request.Transitions.Select(x => x.FromSkuCode).Distinct().ToArray();
        var productCategoryBySku = await dbContext.Skus.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && fromSkuCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Category, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var facts = request.Transitions.Select(transition =>
        {
            var fromProductCategory = transition.FromProductCategoryCode ?? productCategoryBySku.GetValueOrDefault(transition.FromSkuCode);
            var match = entries.Where(x => x.Matches(transition.FromSkuCode, fromProductCategory, transition.ToSkuCode, transition.WorkCenterCode))
                .OrderByDescending(x => x.Specificity).ThenBy(x => x.Id.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (match is null) return new SchedulingToolingFactResponse(transition.OperationId, 0, [], true);
            var applicable = match.RequiredTooling.Select(x => x.ToolingCode).Where(code => tooling.Any(x =>
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) && x.IsApplicable(transition.WorkCenterCode, transition.ToSkuCode)))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return new SchedulingToolingFactResponse(
                transition.OperationId,
                match.SetupMinutes,
                match.RequiredTooling.Select(x => x.ToolingCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                applicable.Length == match.RequiredTooling.Count);
        }).ToArray();
        return new ResolveSchedulingToolingFactsResponse(facts);
    }
}
