namespace Nerv.IIP.Iam.Domain;

public static class IamLoginFailureCodes
{
    public const string InvalidCredentials = "iam-invalid-credentials";
    public const string AccountLocked = "iam-account-locked";

    /// <summary>
    /// 密码正确，但账号不在任何组织环境里（没有成员关系，例如被撤掉了全部角色）。
    /// 只在密码校验通过之后发出，不会泄露账号是否存在。
    /// </summary>
    public const string NoMembership = "iam-no-membership";
}

public sealed class IamLoginRejectedException(
    string code,
    DateTimeOffset? lockoutUntilUtc = null,
    int? remainingAttempts = null) : UnauthorizedAccessException(code)
{
    public string Code { get; } = code;
    public DateTimeOffset? LockoutUntilUtc { get; } = lockoutUntilUtc;
    public int? RemainingAttempts { get; } = remainingAttempts;
}
