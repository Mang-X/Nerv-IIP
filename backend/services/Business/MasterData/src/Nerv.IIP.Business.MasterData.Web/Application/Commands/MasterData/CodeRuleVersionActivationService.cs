using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed class CodeRuleVersionActivationService(ApplicationDbContext dbContext)
{
    public Task<int> PromoteDueVersionsAsync(CancellationToken cancellationToken = default) =>
        PromoteDueVersionsAsync(DateTimeOffset.UtcNow, cancellationToken);

    public async Task<int> PromoteDueVersionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var dueVersions = await dbContext.CodeRuleVersions
            .Where(x => x.Status == CodeRuleVersionStatus.Scheduled && x.EffectiveFromUtc <= now)
            .OrderBy(x => x.OrganizationId)
            .ThenBy(x => x.EnvironmentId)
            .ThenBy(x => x.RuleKey)
            .ThenBy(x => x.Version)
            .ToArrayAsync(cancellationToken);
        if (dueVersions.Length == 0)
        {
            return 0;
        }

        var promotedCurrentDefinitions = 0;
        foreach (var group in dueVersions.GroupBy(x => new { x.OrganizationId, x.EnvironmentId, x.RuleKey }))
        {
            foreach (var version in group)
            {
                version.MarkActive();
            }

            var latestDue = group.OrderByDescending(x => x.Version).First();
            var current = await dbContext.CodeRules.SingleOrDefaultAsync(x =>
                x.OrganizationId == latestDue.OrganizationId &&
                x.EnvironmentId == latestDue.EnvironmentId &&
                x.RuleKey == latestDue.RuleKey,
                cancellationToken);

            if (current is not null && current.Version >= latestDue.Version)
            {
                continue;
            }

            if (current is null)
            {
                dbContext.CodeRules.Add(CodeRule.Create(
                    latestDue.OrganizationId,
                    latestDue.EnvironmentId,
                    latestDue.RuleKey,
                    latestDue.DisplayName,
                    latestDue.AppliesTo,
                    latestDue.Scope,
                    latestDue.SegmentsJson,
                    latestDue.IsActive,
                    latestDue.Version));
            }
            else
            {
                current.ReplaceDefinition(
                    latestDue.DisplayName,
                    latestDue.AppliesTo,
                    latestDue.Scope,
                    latestDue.SegmentsJson,
                    latestDue.IsActive,
                    latestDue.Version);
            }

            promotedCurrentDefinitions++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return promotedCurrentDefinitions;
    }
}
