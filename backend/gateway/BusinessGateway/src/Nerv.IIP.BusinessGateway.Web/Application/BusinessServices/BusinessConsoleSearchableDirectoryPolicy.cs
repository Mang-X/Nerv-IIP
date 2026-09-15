using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleSearchableDirectoryDefinition(
    string DirectoryType,
    string Owner,
    string PermissionCode,
    IReadOnlySet<string> SupportedScopeKinds);

public sealed record BusinessConsoleSearchableDirectoryScope(string? Kind, string? Id);

public static class BusinessConsoleSearchableDirectoryPolicy
{
    private static readonly IReadOnlyDictionary<string, BusinessConsoleSearchableDirectoryDefinition> Definitions =
        new Dictionary<string, BusinessConsoleSearchableDirectoryDefinition>(StringComparer.Ordinal)
        {
            ["personnel"] = Define("personnel", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "team", "workshop", "work-center"),
            ["team"] = Define("team", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "workshop"),
            ["equipment"] = Define("equipment", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "work-center"),
            ["work-center"] = Define("work-center", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "site"),
            ["station"] = Define("station", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "work-center"),
            ["workshop"] = Define("workshop", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "site"),
            ["material"] = Define("material", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead),
            ["priority"] = Define("priority", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead),
            ["location"] = Define("location", "inventory", BusinessGatewayPermissions.InventoryLedgerRead, "site"),
            ["batch"] = Define("batch", "inventory", BusinessGatewayPermissions.InventoryLedgerRead, "site"),
            ["serial"] = Define("serial", "inventory", BusinessGatewayPermissions.InventoryLedgerRead, "site"),
            ["defect-code"] = Define("defect-code", "quality", BusinessGatewayPermissions.QualityInspectionRecordsRead),
            ["scrap-reason"] = Define("scrap-reason", "quality", BusinessGatewayPermissions.QualityInspectionRecordsRead),
            ["downtime-reason"] = Define("downtime-reason", "maintenance", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead),
            ["maintenance-reason"] = Define("maintenance-reason", "maintenance", BusinessGatewayPermissions.MaintenanceDowntimeReasonsRead),
        };

    public static BusinessConsoleSearchableDirectoryDefinition Require(string directoryType)
    {
        var normalized = directoryType.Trim().ToLowerInvariant();
        return Definitions.TryGetValue(normalized, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(directoryType), directoryType, "Unsupported directory type.");
    }

    public static string? ValidateScope(string directoryType, string? scopeKind, string? scopeId)
    {
        var hasKind = !string.IsNullOrWhiteSpace(scopeKind);
        var hasId = !string.IsNullOrWhiteSpace(scopeId);
        if (hasKind != hasId)
        {
            return "directory-scope-incomplete";
        }

        if (!hasKind)
        {
            return null;
        }

        var definition = Require(directoryType);
        return definition.SupportedScopeKinds.Contains(scopeKind!.Trim().ToLowerInvariant())
            ? null
            : "directory-scope-unsupported";
    }

    public static string? ValidateRankingMode(string? rankingMode)
    {
        var normalized = string.IsNullOrWhiteSpace(rankingMode)
            ? "default"
            : rankingMode.Trim().ToLowerInvariant();
        return normalized is "default" or "recent" or "suggested"
            ? null
            : "directory-ranking-mode-unsupported";
    }

    public static BusinessConsoleSearchableDirectoryScope? ResolveAuthorizedScope(
        BusinessConsoleSearchableDirectoryDefinition definition,
        BusinessGatewayAuthorizationResult? authorization,
        string organizationId,
        string? requestedScopeKind,
        string? requestedScopeId)
    {
        if (authorization is null
            || !authorization.IsAllowed
            || authorization.DataScope?.DenyAll == true)
        {
            return null;
        }

        var grants = (authorization.ScopeGrants ?? []).ToArray();
        if (grants.Length == 0
            || grants.Any(grant => !IsRepresentableGrant(definition, grant, organizationId)))
        {
            return null;
        }

        // 无范围维度的目录（SupportedScopeKinds 为空集）：权威源查询里没有任何范围参数，
        // 目录内容是组织级参考数据，不存在按范围切分、可被越权读到的行。此时把 grant 收窄成
        // 过滤条件是空操作，「所有 grant 必须可表示」退化为「除组织级授权外一律拒」，
        // 会把只持 self/site 等受限范围的主体整体挡在词表之外（#3125）。
        // 仍然只在没有显式请求范围时放行；带显式范围一律 fail closed，绝不静默忽略它。
        if (definition.SupportedScopeKinds.Count == 0)
        {
            return string.IsNullOrWhiteSpace(requestedScopeKind)
                ? new BusinessConsoleSearchableDirectoryScope(null, null)
                : null;
        }

        var organizationWide = grants.Any(grant =>
            grant.OrganizationWide
            && string.Equals(grant.ScopeKind.Trim(), "organization", StringComparison.OrdinalIgnoreCase)
            && string.Equals(grant.ScopeId.Trim(), organizationId, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(requestedScopeKind))
        {
            var kind = requestedScopeKind.Trim().ToLowerInvariant();
            var id = requestedScopeId!.Trim();
            var exact = grants.Any(grant =>
                string.Equals(grant.ScopeKind.Trim(), kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(grant.ScopeId.Trim(), id, StringComparison.Ordinal));
            return organizationWide || exact
                ? new BusinessConsoleSearchableDirectoryScope(kind, id)
                : null;
        }

        if (organizationWide)
        {
            return new BusinessConsoleSearchableDirectoryScope(null, null);
        }

        var compatible = grants
            .Select(grant => new BusinessConsoleSearchableDirectoryScope(
                grant.ScopeKind.Trim().ToLowerInvariant(),
                grant.ScopeId.Trim()))
            .Where(scope => definition.SupportedScopeKinds.Contains(scope.Kind!))
            .Distinct()
            .ToArray();
        return compatible.Length == 1 ? compatible[0] : null;
    }

    private static bool IsRepresentableGrant(
        BusinessConsoleSearchableDirectoryDefinition definition,
        AuthorizationScopeGrant? grant,
        string organizationId)
    {
        if (grant is null
            || string.IsNullOrWhiteSpace(grant.SourceKind)
            || grant.SourceKind.Trim().ToLowerInvariant() is not ("role" or "membership")
            || string.IsNullOrWhiteSpace(grant.SourceId)
            || string.IsNullOrWhiteSpace(grant.ScopeKind)
            || string.IsNullOrWhiteSpace(grant.ScopeId)
            || grant.ApplicablePermissionCodes?.Contains(definition.PermissionCode, StringComparer.Ordinal) != true)
        {
            return false;
        }

        var scopeKind = grant.ScopeKind.Trim().ToLowerInvariant();
        var scopeId = grant.ScopeId.Trim();
        if (grant.OrganizationWide)
        {
            return scopeKind == "organization" && string.Equals(scopeId, organizationId, StringComparison.Ordinal);
        }

        // 无范围维度的目录上，受限范围本身不可能表达成过滤条件，也没有需要表达的东西；
        // 唯一仍然承重的范围约束是租户边界——落在别的组织上的 grant 依旧不可表示。
        if (definition.SupportedScopeKinds.Count == 0)
        {
            return scopeKind != "organization"
                || string.Equals(scopeId, organizationId, StringComparison.Ordinal);
        }

        return definition.SupportedScopeKinds.Contains(scopeKind);
    }

    private static BusinessConsoleSearchableDirectoryDefinition Define(
        string directoryType,
        string owner,
        string permissionCode,
        params string[] scopeKinds) =>
        new(directoryType, owner, permissionCode, new HashSet<string>(scopeKinds, StringComparer.Ordinal));
}
