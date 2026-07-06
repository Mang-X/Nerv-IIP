using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;
using Microsoft.Extensions.Options;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamPasswordPolicyOptions
{
    public int MinimumLength { get; init; } = 8;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public bool RequireDigit { get; init; } = true;
    public bool RequireNonAlphanumeric { get; init; } = true;
    public int PasswordExpiresDays { get; init; } = 90;
    public int PasswordHistoryCount { get; init; } = 5;
}

public sealed class IamPasswordPolicy(IOptions<IamPasswordPolicyOptions> options, IamPasswordService passwords)
{
    public IamPasswordPolicyOptions Current => options.Value;

    public DateTimeOffset? GetPasswordExpiresAtUtc(DateTimeOffset changedAtUtc)
    {
        return Current.PasswordExpiresDays > 0
            ? changedAtUtc.AddDays(Current.PasswordExpiresDays)
            : null;
    }

    public void ValidateNewPassword(User user, string password)
    {
        ValidateComplexity(password);
        if (passwords.VerifyHash(user.PasswordHash, password)
            || user.PasswordHistory.Any(history => passwords.VerifyHash(history.PasswordHash, password)))
        {
            throw new KnownException("Password was recently used.");
        }
    }

    public void ValidateComplexity(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new KnownException("Password is required.");
        }

        if (password.Length < Current.MinimumLength)
        {
            throw new KnownException($"Password must be at least {Current.MinimumLength} characters.");
        }

        if (Current.RequireUppercase && !password.Any(char.IsUpper))
        {
            throw new KnownException("Password must include an uppercase letter.");
        }

        if (Current.RequireLowercase && !password.Any(char.IsLower))
        {
            throw new KnownException("Password must include a lowercase letter.");
        }

        if (Current.RequireDigit && !password.Any(char.IsDigit))
        {
            throw new KnownException("Password must include a digit.");
        }

        if (Current.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
        {
            throw new KnownException("Password must include a non-alphanumeric character.");
        }
    }
}
