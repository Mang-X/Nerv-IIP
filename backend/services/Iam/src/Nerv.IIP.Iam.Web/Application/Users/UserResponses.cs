namespace Nerv.IIP.Iam.Web.Application.Users;

public sealed record UserResponse(
    string UserId,
    string LoginName,
    string Email,
    bool Enabled,
    DateTimeOffset? AccountExpiresAtUtc,
    bool PasswordChangeRequired,
    DateTimeOffset? PasswordExpiresAtUtc,
    DateTimeOffset? LockoutUntilUtc,
    string? DisplayName = null,
    string? EmployeeNo = null,
    string? DepartmentName = null);

public sealed record UserMembershipResponse(
    string UserId,
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyList<string> RoleIds);
