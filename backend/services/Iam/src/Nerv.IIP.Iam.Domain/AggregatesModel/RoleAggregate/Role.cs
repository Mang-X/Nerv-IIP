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
        NormalizedRoleName = string.Empty;
    }

    public Role(RoleId id, string roleName, IEnumerable<string> permissionCodes)
    {
        Id = id;
        RoleName = roleName;
        NormalizedRoleName = NormalizeName(roleName);
        ReplacePermissions(permissionCodes);
    }

    public string RoleName { get; private set; } = string.Empty;
    public string NormalizedRoleName { get; private set; } = string.Empty;
    public IReadOnlyCollection<RolePermission> Permissions => permissions;
    public Deleted Deleted { get; private set; } = new(false);
    public RowVersion RowVersion { get; private set; } = new(0);

    public static string NormalizeName(string roleName)
    {
        return roleName.Trim().ToUpperInvariant();
    }

    public void ReplacePermissions(IEnumerable<string> permissionCodes)
    {
        var desiredCodes = permissionCodes.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        permissions.RemoveAll(x => !desiredCodes.Contains(x.PermissionCode));

        var existingCodes = permissions.Select(x => x.PermissionCode).ToHashSet(StringComparer.Ordinal);
        foreach (var code in desiredCodes.Order(StringComparer.Ordinal))
        {
            if (!existingCodes.Contains(code))
            {
                permissions.Add(new RolePermission(new RolePermissionId($"{Id.Id}:{code}"), Id, code));
            }
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
