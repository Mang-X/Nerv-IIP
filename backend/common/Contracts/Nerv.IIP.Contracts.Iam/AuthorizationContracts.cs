namespace Nerv.IIP.Contracts.Iam;

public sealed record AuthorizationCheckRequest(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId,
    bool IncludePrincipalContext = false);

public sealed record AuthorizationCheckResponse(
    bool Allowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason,
    AuthorizationDataScope? DataScope = null,
    IReadOnlyCollection<AuthorizationScopeGrant>? ScopeGrants = null,
    IReadOnlyCollection<AuthorizationRole>? Roles = null);

public sealed record AuthorizationRole(string Id, string DisplayName);

public sealed record AuthorizationScopeGrant(
    string SourceKind,
    string SourceId,
    string ScopeKind,
    string ScopeId,
    IReadOnlyCollection<string> ApplicablePermissionCodes,
    bool OrganizationWide = false);

public sealed record AuthorizationDataScope(
    IReadOnlyCollection<string> SiteCodes,
    IReadOnlyCollection<string> WorkshopCodes,
    IReadOnlyCollection<string> ProductionLineCodes,
    bool DenyAll = false,
    IReadOnlyCollection<string>? SelfIds = null,
    IReadOnlyCollection<string>? TeamCodes = null,
    IReadOnlyCollection<string>? WorkCenterCodes = null,
    IReadOnlyCollection<string>? OrganizationIds = null)
{
    public bool HasRestrictions =>
        DenyAll
        || SiteCodes.Count > 0
        || WorkshopCodes.Count > 0
        || ProductionLineCodes.Count > 0
        || SelfIds?.Count > 0
        || TeamCodes?.Count > 0
        || WorkCenterCodes?.Count > 0
        || OrganizationIds?.Count > 0;
}
