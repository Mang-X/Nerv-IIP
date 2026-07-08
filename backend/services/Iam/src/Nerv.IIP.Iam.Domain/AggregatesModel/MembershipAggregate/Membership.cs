using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;

public partial record MembershipId : IStringStronglyTypedId;
public partial record MembershipRoleId : IStringStronglyTypedId;
public partial record MembershipDataScopeId : IStringStronglyTypedId;

public class Membership : Entity<MembershipId>, IAggregateRoot
{
    private readonly List<MembershipRole> roles = [];
    private readonly List<MembershipDataScope> dataScopes = [];

    private Membership()
    {
        Id = new MembershipId(string.Empty);
        UserId = new UserId(string.Empty);
        OrganizationId = new OrganizationId(string.Empty);
        EnvironmentId = new IamEnvironmentId(string.Empty);
    }

    public Membership(
        MembershipId id,
        UserId userId,
        OrganizationId organizationId,
        IamEnvironmentId environmentId,
        IEnumerable<RoleId> roleIds)
    {
        Id = id;
        UserId = userId;
        OrganizationId = organizationId;
        EnvironmentId = environmentId;
        ReplaceRoles(roleIds);
    }

    public UserId UserId { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public IamEnvironmentId EnvironmentId { get; private set; }
    public IReadOnlyCollection<MembershipRole> Roles => roles;
    public IReadOnlyCollection<MembershipDataScope> DataScopes => dataScopes;

    public void ReplaceRoles(IEnumerable<RoleId> roleIds)
    {
        var desiredRoleIds = roleIds.Distinct().ToHashSet();
        roles.RemoveAll(x => !desiredRoleIds.Contains(x.RoleId));

        var existingRoleIds = roles.Select(x => x.RoleId).ToHashSet();
        foreach (var roleId in desiredRoleIds.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (!existingRoleIds.Contains(roleId))
            {
                roles.Add(new MembershipRole(new MembershipRoleId($"{Id.Id}:{roleId.Id}"), Id, roleId));
            }
        }
    }

    public void ReplaceDataScopes(IEnumerable<DataScopeBinding> scopes)
    {
        var desired = scopes
            .Select(DataScopeBinding.Normalize)
            .Distinct()
            .ToHashSet();
        dataScopes.RemoveAll(x => !desired.Contains(new DataScopeBinding(x.ScopeType, x.ScopeCode)));

        var existing = dataScopes.Select(x => new DataScopeBinding(x.ScopeType, x.ScopeCode)).ToHashSet();
        foreach (var scope in desired.OrderBy(x => x.ScopeType, StringComparer.Ordinal).ThenBy(x => x.ScopeCode, StringComparer.Ordinal))
        {
            if (!existing.Contains(scope))
            {
                dataScopes.Add(new MembershipDataScope(new MembershipDataScopeId($"{Id.Id}:{scope.ScopeType}:{scope.ScopeCode}"), Id, scope.ScopeType, scope.ScopeCode));
            }
        }
    }
}

public class MembershipRole : Entity<MembershipRoleId>
{
    private MembershipRole()
    {
        Id = new MembershipRoleId(string.Empty);
        MembershipId = new MembershipId(string.Empty);
        RoleId = new RoleId(string.Empty);
    }

    internal MembershipRole(MembershipRoleId id, MembershipId membershipId, RoleId roleId)
    {
        Id = id;
        MembershipId = membershipId;
        RoleId = roleId;
    }

    public MembershipId MembershipId { get; private set; }
    public RoleId RoleId { get; private set; }
}

public class MembershipDataScope : Entity<MembershipDataScopeId>
{
    private MembershipDataScope()
    {
        Id = new MembershipDataScopeId(string.Empty);
        MembershipId = new MembershipId(string.Empty);
    }

    internal MembershipDataScope(MembershipDataScopeId id, MembershipId membershipId, string scopeType, string scopeCode)
    {
        Id = id;
        MembershipId = membershipId;
        ScopeType = scopeType;
        ScopeCode = scopeCode;
    }

    public MembershipId MembershipId { get; private set; }
    public string ScopeType { get; private set; } = string.Empty;
    public string ScopeCode { get; private set; } = string.Empty;
}
