using Microsoft.AspNetCore.Identity;
using Nerv.IIP.Iam.Domain.AggregatesModel.UserAggregate;

namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamPasswordService
{
    private readonly PasswordHasher<User> _passwordHasher = new();

    public string Hash(User user, string password)
    {
        return _passwordHasher.HashPassword(user, password);
    }

    public bool Verify(User user, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
