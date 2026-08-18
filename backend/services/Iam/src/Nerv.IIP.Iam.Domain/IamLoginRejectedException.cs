namespace Nerv.IIP.Iam.Domain;

public static class IamLoginFailureCodes
{
    public const string InvalidCredentials = "iam-invalid-credentials";
    public const string AccountLocked = "iam-account-locked";
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
