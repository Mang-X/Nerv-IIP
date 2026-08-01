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
            ["downtime-reason"] = Define("downtime-reason", "maintenance", BusinessGatewayPermissions.MaintenanceWorkOrdersRead),
            ["maintenance-reason"] = Define("maintenance-reason", "maintenance", BusinessGatewayPermissions.MaintenanceWorkOrdersRead),
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
        return grant.OrganizationWide
            ? scopeKind == "organization" && string.Equals(scopeId, organizationId, StringComparison.Ordinal)
            : definition.SupportedScopeKinds.Contains(scopeKind);
    }

    private static BusinessConsoleSearchableDirectoryDefinition Define(
        string directoryType,
        string owner,
        string permissionCode,
        params string[] scopeKinds) =>
        new(directoryType, owner, permissionCode, new HashSet<string>(scopeKinds, StringComparer.Ordinal));
}
