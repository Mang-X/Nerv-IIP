using System.Diagnostics.CodeAnalysis;
using Nerv.IIP.BusinessGateway.Web.Application.Auth;
using Nerv.IIP.Contracts.Iam;

namespace Nerv.IIP.BusinessGateway.Web.Application.BusinessServices;

public sealed record BusinessConsoleSearchableDirectoryDefinition(
    string DirectoryType,
    string Owner,
    string PermissionCode,
    IReadOnlySet<string> SupportedScopeKinds)
{
    /// <summary>只按工厂切分的目录（库位 / 批次 / 序列号 / 工作中心 / 车间）：可见范围取授权工厂的并集。</summary>
    public bool SplitBySite => SupportedScopeKinds.Count == 1 && SupportedScopeKinds.Contains("site");
}

public sealed record BusinessConsoleSearchableDirectoryScope(string? Kind, string? Id);

/// <summary>
/// 按工厂切分的目录（库位 / 批次 / 序列号 / 工作中心 / 车间）可见范围，只有两种合法状态：
/// 组织级授权不收窄（<see cref="OrganizationWide"/>），或收窄到至少一个工厂（<see cref="Sites"/>）。
/// </summary>
public sealed class BusinessConsoleAuthorizedSites
{
    private BusinessConsoleAuthorizedSites(IReadOnlyList<string>? siteFilter)
    {
        SiteFilter = siteFilter;
    }

    /// <summary>组织级授权：不按工厂收窄。</summary>
    public static BusinessConsoleAuthorizedSites OrganizationWide { get; } = new(null);

    /// <summary>收窄到给定工厂（至少一个）。</summary>
    public static BusinessConsoleAuthorizedSites Sites(IReadOnlyList<string> siteCodes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(siteCodes.Count);
        return new(siteCodes);
    }

    /// <summary>下传给权威源（库存 / 主数据）的工厂过滤；null 表示不收窄。</summary>
    public IReadOnlyList<string>? SiteFilter { get; }
}

public static class BusinessConsoleSearchableDirectoryPolicy
{
    private static readonly IReadOnlyDictionary<string, BusinessConsoleSearchableDirectoryDefinition> Definitions =
        new Dictionary<string, BusinessConsoleSearchableDirectoryDefinition>(StringComparer.Ordinal)
        {
            ["personnel"] = Define("personnel", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "team", "workshop", "work-center"),
            ["team"] = Define("team", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "workshop"),
            ["equipment"] = Define("equipment", "master-data", BusinessGatewayPermissions.MasterDataResourcesRead, "site", "workshop", "production-line", "work-center"),
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
            // 员工「关联登录账号」候选（#3924）：本组织/环境里已启用的成员账号。登录名只对能维护员工档案的人
            // （masterdata.resources.manage）可见，且账号目录是组织级事实、不按车间/班组切分，
            // 因此只对持有组织级授权的主体开放（见 ResolveAuthorizedScope）。员工名册补登录名走同一道门
            // （BusinessConsoleWorkerLoginAccounts.CanSeeLoginNamesAsync），只读员工的主体拿到的 loginName 恒为空。
            ["login-account"] = Define("login-account", "iam", BusinessGatewayPermissions.MasterDataResourcesManage),
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

        // 登录账号目录只对组织级授权开放：受限范围（self / 车间 / 班组）的主体不因「无范围维度」被放行去
        // 读整个组织的账号名单；带显式范围同样拒绝。
        if (string.Equals(definition.Owner, "iam", StringComparison.Ordinal))
        {
            var organizationWideGrant = grants.Any(grant =>
                grant.OrganizationWide
                && string.Equals(grant.ScopeKind.Trim(), "organization", StringComparison.OrdinalIgnoreCase)
                && string.Equals(grant.ScopeId.Trim(), organizationId, StringComparison.Ordinal));
            return organizationWideGrant && string.IsNullOrWhiteSpace(requestedScopeKind)
                ? new BusinessConsoleSearchableDirectoryScope(null, null)
                : null;
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

    /// <summary>
    /// 按工厂切分的目录（库位 / 批次 / 序列号 / 工作中心 / 车间）：可见范围是用户授权工厂的并集。
    /// <list type="bullet">
    /// <item>持有多个工厂范围：看到这些工厂的并集。</item>
    /// <item>self、work-center 等不是工厂的范围不给出任何工厂，也不让同一用户的工厂授权失效
    /// （与 WMS 作业范围解析对 self 的处理一致，见 <c>PrincipalWorkContextAuthorizationResolver.ResolveSiteCandidates</c>）。</item>
    /// <item>不适用本权限的授权不参与；来源残缺、或落在别的组织 / 非组织级的组织范围授权仍整体拒绝。</item>
    /// <item>显式请求某个工厂：只有组织级授权或该工厂在授权里才放行。一个工厂都没有时拒绝（返回 null）。</item>
    /// </list>
    /// </summary>
    public static BusinessConsoleAuthorizedSites? ResolveAuthorizedSites(
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
        if (!grants.All(IsWellFormedGrant))
        {
            return null;
        }

        var applicable = grants
            .Where(grant => grant.ApplicablePermissionCodes?.Contains(definition.PermissionCode, StringComparer.Ordinal) == true)
            .ToArray();
        var organizationGrants = applicable
            .Where(grant => string.Equals(grant.ScopeKind.Trim(), "organization", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (organizationGrants.Any(grant => !grant.OrganizationWide
                || !string.Equals(grant.ScopeId.Trim(), organizationId, StringComparison.Ordinal)))
        {
            return null;
        }

        var organizationWide = organizationGrants.Length > 0;
        var sites = applicable
            .Where(grant => string.Equals(grant.ScopeKind.Trim(), "site", StringComparison.OrdinalIgnoreCase))
            .Select(grant => grant.ScopeId.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(requestedScopeKind))
        {
            var requestedSite = requestedScopeId!.Trim();
            return organizationWide || sites.Contains(requestedSite, StringComparer.Ordinal)
                ? BusinessConsoleAuthorizedSites.Sites([requestedSite])
                : null;
        }

        if (organizationWide)
        {
            return BusinessConsoleAuthorizedSites.OrganizationWide;
        }

        return sites.Length > 0 ? BusinessConsoleAuthorizedSites.Sites(sites) : null;
    }

    // #4257：设备没有 self/team 所有权，只消费适用资源读权限的空间授权。
    public static IReadOnlyList<BusinessConsoleSearchableDirectoryScope>? ResolveAuthorizedDeviceScopes(
        BusinessGatewayAuthorizationResult? authorization, string organizationId)
    {
        if (authorization is null || !authorization.IsAllowed || authorization.DataScope?.DenyAll == true)
        {
            return null;
        }
        var grants = (authorization.ScopeGrants ?? []).ToArray();
        if (!grants.All(IsWellFormedGrant))
        {
            return null;
        }
        var applicable = grants.Where(grant => grant.ApplicablePermissionCodes?.Contains(
            BusinessGatewayPermissions.MasterDataResourcesRead, StringComparer.Ordinal) == true).ToArray();
        var organizations = applicable.Where(grant =>
            string.Equals(grant.ScopeKind.Trim(), "organization", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (organizations.Any(grant => !grant.OrganizationWide || grant.ScopeId.Trim() != organizationId))
        {
            return null;
        }
        if (organizations.Length > 0)
        {
            return [new("organization", organizationId)];
        }
        var scopes = applicable.Select(grant => new BusinessConsoleSearchableDirectoryScope(
                grant.ScopeKind.Trim().ToLowerInvariant(), grant.ScopeId.Trim()))
            .Where(scope => Require("equipment").SupportedScopeKinds.Contains(scope.Kind!))
            .Distinct().ToArray();
        return scopes.Length > 0 ? scopes : null;
    }

    private static bool IsRepresentableGrant(
        BusinessConsoleSearchableDirectoryDefinition definition,
        AuthorizationScopeGrant? grant,
        string organizationId)
    {
        if (!IsWellFormedGrant(grant)
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

    /// <summary>来源（角色 / 成员关系）与范围齐全的授权；残缺的授权一律视为不可表示。</summary>
    private static bool IsWellFormedGrant([NotNullWhen(true)] AuthorizationScopeGrant? grant) =>
        grant is not null
        && !string.IsNullOrWhiteSpace(grant.SourceKind)
        && grant.SourceKind.Trim().ToLowerInvariant() is "role" or "membership"
        && !string.IsNullOrWhiteSpace(grant.SourceId)
        && !string.IsNullOrWhiteSpace(grant.ScopeKind)
        && !string.IsNullOrWhiteSpace(grant.ScopeId);

    private static BusinessConsoleSearchableDirectoryDefinition Define(
        string directoryType,
        string owner,
        string permissionCode,
        params string[] scopeKinds) =>
        new(directoryType, owner, permissionCode, new HashSet<string>(scopeKinds, StringComparer.Ordinal));
}
