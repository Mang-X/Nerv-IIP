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
    string? DenialReason);
