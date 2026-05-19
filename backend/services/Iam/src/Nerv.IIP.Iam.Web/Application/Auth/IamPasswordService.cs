using Microsoft.AspNetCore.Identity;
namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamPasswordService
{
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string Hash(string password)
    {
        return _passwordHasher.HashPassword(new object(), password);
    }

    public bool Verify(Domain.AggregatesModel.UserAggregate.User user, string password)
    {
        return VerifyHash(user.PasswordHash, password);
    }

    public bool VerifyHash(string passwordHash, string password)
    {
        var result = _passwordHasher.VerifyHashedPassword(new object(), passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
