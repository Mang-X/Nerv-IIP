using Nerv.IIP.Iam.Domain.AggregatesModel.OrganizationAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.MembershipAggregate;

public partial record MembershipId : IStringStronglyTypedId;
public partial record MembershipRoleId : IStringStronglyTypedId;

public class Membership : Entity<MembershipId>, IAggregateRoot
{
    private readonly List<MembershipRole> roles = [];

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
