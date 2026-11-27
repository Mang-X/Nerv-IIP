using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Approval.Domain.AggregatesModel.ApprovalDelegationAggregate;

namespace Nerv.IIP.Business.Approval.Web.Application;

internal sealed class ApprovalTaskActors(
    HashSet<(string Type, string Ref)> direct,
    IReadOnlyList<ApprovalDelegation> delegations)
{
    public IEnumerable<string> CandidateRefs =>
        direct.Select(x => x.Ref).Concat(delegations.Select(x => x.DelegatorActorRef)).Distinct();

    public bool IsDirect(string type, string actorRef) => direct.Contains((type, actorRef));

    public bool Matches(string type, string actorRef, string documentType) =>
        IsDirect(type, actorRef) || delegations.Any(x =>
            x.DelegatorActorType == type && x.DelegatorActorRef == actorRef
            && (x.DocumentType is null || x.DocumentType == documentType));

    public static async Task<ApprovalTaskActors> LoadAsync(
        ApplicationDbContext dbContext, string organizationId, string environmentId,
        string actorType, string actorRef, string? roleRefs, string? departmentRef,
        CancellationToken cancellationToken)
    {
        actorType = actorType.Trim().ToLowerInvariant();
        actorRef = actorRef.Trim();
        var direct = new HashSet<(string, string)> { (actorType, actorRef) };
        foreach (var roleRef in (roleRefs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            direct.Add(("role", roleRef));
        }

        if (!string.IsNullOrWhiteSpace(departmentRef))
        {
            direct.Add(("department", departmentRef));
        }

        var now = DateTimeOffset.UtcNow;
        var delegations = await dbContext.ApprovalDelegations.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId
                && x.Status == ApprovalDelegationStatuses.Active
                && x.DelegateActorType == actorType && x.DelegateActorRef == actorRef)
            .ToListAsync(cancellationToken);

        return new ApprovalTaskActors(direct, delegations
            .Where(x => x.EffectiveFromUtc <= now && x.EffectiveToUtc >= now)
            .ToArray());
    }
}
