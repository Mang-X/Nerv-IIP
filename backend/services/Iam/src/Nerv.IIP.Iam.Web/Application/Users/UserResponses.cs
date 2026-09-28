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

/// <summary>组织/环境成员账号：只含业务侧关联账号所需的最小字段（不含邮箱等联系方式）。</summary>
public sealed record MemberAccountResponse(
    string UserId,
    string LoginName,
    string? DisplayName,
    bool Enabled);

/// <summary>
/// 成员账号查询条件。<see cref="UserIds"/> 非空时按账号 ID 批量收窄（最多 <see cref="MaxUserIds"/> 个）；
/// <see cref="IncludeDisabled"/> 为假时只列启用账号。
/// </summary>
public sealed record MemberAccountListOptions(
    string OrganizationId,
    string EnvironmentId,
    string? Keyword,
    IReadOnlyList<string>? UserIds,
    bool IncludeDisabled,
    int PageIndex,
    int PageSize)
{
    public const int MaxPageSize = 100;
    public const int MaxUserIds = 100;
}
