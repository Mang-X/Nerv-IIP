using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.ToolingAssetAggregate;

namespace Nerv.IIP.Business.MasterData.Web.Application.Queries;

public sealed record SchedulingTransitionRequest(string OperationId, string WorkCenterCode, string FromSkuCode, string? FromProductFamilyCode, string ToSkuCode);
public sealed record ResolveSchedulingToolingFactsQuery(string OrganizationId, string EnvironmentId, IReadOnlyCollection<SchedulingTransitionRequest> Transitions) : IQuery<ResolveSchedulingToolingFactsResponse>;
public sealed record SchedulingToolingFactResponse(string OperationId, int SetupMinutes, IReadOnlyCollection<string> RequiredToolingCodes);
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
        var productFamilyBySku = await dbContext.Skus.AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.EnvironmentId == request.EnvironmentId && fromSkuCodes.Contains(x.Code))
            .ToDictionaryAsync(x => x.Code, x => x.Category, StringComparer.Ordinal, cancellationToken);

        var facts = request.Transitions.Select(transition =>
        {
            var fromProductFamily = transition.FromProductFamilyCode ?? productFamilyBySku.GetValueOrDefault(transition.FromSkuCode);
            var match = entries.Where(x => x.Matches(transition.FromSkuCode, fromProductFamily, transition.ToSkuCode, transition.WorkCenterCode))
                .OrderByDescending(x => x.Specificity).ThenBy(x => x.Id.ToString(), StringComparer.Ordinal).FirstOrDefault();
            if (match is null) return new SchedulingToolingFactResponse(transition.OperationId, 0, []);
            var applicable = match.RequiredTooling.Select(x => x.ToolingCode).Where(code => tooling.Any(x =>
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) && x.IsApplicable(transition.WorkCenterCode, transition.ToSkuCode)))
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if (applicable.Length != match.RequiredTooling.Count)
                throw new KnownException($"Changeover tooling for operation '{transition.OperationId}' is unavailable or not applicable.");
            return new SchedulingToolingFactResponse(transition.OperationId, match.SetupMinutes, applicable);
        }).ToArray();
        return new ResolveSchedulingToolingFactsResponse(facts);
    }
}
