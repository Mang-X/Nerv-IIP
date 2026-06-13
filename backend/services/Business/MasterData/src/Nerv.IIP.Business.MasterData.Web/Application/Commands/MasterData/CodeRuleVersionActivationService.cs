using Microsoft.EntityFrameworkCore;
using Hangfire;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.CodeRuleAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;

namespace Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;

public sealed class CodeRuleVersionActivationService(ApplicationDbContext dbContext)
{
    [DisableConcurrentExecution(60)]
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
            var latestDue = group.OrderByDescending(x => x.Version).First();
            var versionsToSupersede = await dbContext.CodeRuleVersions
                .Where(x =>
                    x.OrganizationId == latestDue.OrganizationId &&
                    x.EnvironmentId == latestDue.EnvironmentId &&
                    x.RuleKey == latestDue.RuleKey &&
                    x.Version < latestDue.Version &&
                    x.Status == CodeRuleVersionStatus.Active)
                .ToArrayAsync(cancellationToken);
            foreach (var version in versionsToSupersede)
            {
                version.MarkSuperseded();
            }

            foreach (var version in group)
            {
                if (version.Version == latestDue.Version)
                {
                    version.MarkActive();
                }
                else
                {
                    version.MarkSuperseded();
                }
            }

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
