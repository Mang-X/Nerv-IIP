using NetCorePal.Extensions.Domain;

namespace Nerv.IIP.Iam.Domain.AggregatesModel.RoleAggregate;

public partial record RoleId : IStringStronglyTypedId;
public partial record RolePermissionId : IStringStronglyTypedId;
public partial record RoleDataScopeId : IStringStronglyTypedId;

public class Role : Entity<RoleId>, IAggregateRoot
{
    private readonly List<RolePermission> permissions = [];
    private readonly List<RoleDataScope> dataScopes = [];

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
    public IReadOnlyCollection<RoleDataScope> DataScopes => dataScopes;
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
                dataScopes.Add(new RoleDataScope(new RoleDataScopeId($"{Id.Id}:{scope.ScopeType}:{scope.ScopeCode}"), Id, scope.ScopeType, scope.ScopeCode));
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

public class RoleDataScope : Entity<RoleDataScopeId>
{
    private RoleDataScope()
    {
        Id = new RoleDataScopeId(string.Empty);
        RoleId = new RoleId(string.Empty);
    }

    internal RoleDataScope(RoleDataScopeId id, RoleId roleId, string scopeType, string scopeCode)
    {
        Id = id;
        RoleId = roleId;
        ScopeType = scopeType;
        ScopeCode = scopeCode;
    }

    public RoleId RoleId { get; private set; }
    public string ScopeType { get; private set; } = string.Empty;
    public string ScopeCode { get; private set; } = string.Empty;
}

public sealed record DataScopeBinding(string ScopeType, string ScopeCode)
{
    public static DataScopeBinding Normalize(DataScopeBinding binding) =>
        new(binding.ScopeType.Trim(), binding.ScopeCode.Trim());
}
