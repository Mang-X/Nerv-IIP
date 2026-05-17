using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;

public partial record RoleId : IStringStronglyTypedId;
public partial record RolePermissionId : IStringStronglyTypedId;

public class Role : Entity<RoleId>, IAggregateRoot
{
    private readonly List<RolePermission> permissions = [];

    private Role()
    {
        Id = new RoleId(string.Empty);
    }

    public Role(RoleId id, string roleName, IEnumerable<string> permissionCodes)
    {
        Id = id;
        RoleName = roleName;
        ReplacePermissions(permissionCodes);
    }

    public string RoleName { get; private set; } = string.Empty;
    public IReadOnlyCollection<RolePermission> Permissions => permissions;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);

    public void ReplacePermissions(IEnumerable<string> permissionCodes)
    {
        permissions.Clear();
        foreach (var code in permissionCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            permissions.Add(new RolePermission(new RolePermissionId($"{Id.Id}:{code}"), Id, code));
        }
    }
}

public class RolePermission : Entity<RolePermissionId>
{
    private RolePermission()
    {
        Id = new RolePermissionId(string.Empty);
        RoleId = new RoleId(string.Empty);
    }

    internal RolePermission(RolePermissionId id, RoleId roleId, string permissionCode)
    {
        Id = id;
        RoleId = roleId;
        PermissionCode = permissionCode;
    }

    public RoleId RoleId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
}
