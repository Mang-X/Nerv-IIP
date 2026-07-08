namespace Nerv.IIP.Contracts.Iam;

public sealed record AuthorizationCheckRequest(
    string PermissionCode,
    string OrganizationId,
    string EnvironmentId,
    string? ResourceType,
    string? ResourceId);

public sealed record AuthorizationCheckResponse(
    bool Allowed,
    string? PrincipalId,
    string? PrincipalType,
    string? LoginName,
    string? DenialReason,
    AuthorizationDataScope? DataScope = null);

public sealed record AuthorizationDataScope(
    IReadOnlyCollection<string> SiteCodes,
    IReadOnlyCollection<string> WorkshopCodes,
    IReadOnlyCollection<string> ProductionLineCodes,
    bool DenyAll = false)
{
    public bool HasRestrictions =>
        DenyAll || SiteCodes.Count > 0 || WorkshopCodes.Count > 0 || ProductionLineCodes.Count > 0;
}
