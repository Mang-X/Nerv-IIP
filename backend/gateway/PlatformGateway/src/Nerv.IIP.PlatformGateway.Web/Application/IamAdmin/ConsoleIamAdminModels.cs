namespace Nerv.IIP.PlatformGateway.Web.Application.IamAdmin;

public sealed record PagedListResponse<T>(int PageIndex, int PageSize, int TotalCount, IReadOnlyList<T> Items);
public sealed record ConsoleIamListRequest(int? PageIndex, int? PageSize, string? SortBy, string? SortOrder, string? FilterSearch, bool? FilterEnabled, bool? FilterRevoked);
public sealed record ConsoleIamUserResponse(
    string UserId,
    string LoginName,
    string Email,
    bool Enabled,
    DateTimeOffset? AccountExpiresAtUtc,
    bool PasswordChangeRequired,
    DateTimeOffset? PasswordExpiresAtUtc,
    DateTimeOffset? LockoutUntilUtc);
public sealed record ConsoleCreateIamUserRequest(string LoginName, string Email, string Password, DateTimeOffset? AccountExpiresAtUtc);
public sealed record ConsoleUpdateIamUserRequest(string LoginName, string Email, bool Enabled, DateTimeOffset? AccountExpiresAtUtc);
public sealed record ConsoleResetIamUserPasswordRequest(string NewPassword);
public sealed record ConsoleIamRoleResponse(string RoleId, string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record ConsoleCreateIamRoleRequest(string RoleName, IReadOnlyList<string> PermissionCodes);
public sealed record ConsoleUpdateIamRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);
public sealed record ConsoleIamPermissionCatalogResponse(IReadOnlyList<ConsoleIamPermissionResponse> Items);
public sealed record ConsoleIamPermissionResponse(string Code, string Domain, string Description, bool Seeded);
public sealed record ConsoleIamSessionResponse(string SessionId, string UserId, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? RevokedAtUtc, int PermissionVersion);
