namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamAuthenticationOptions
{
    public int FailedLoginLockoutThreshold { get; init; } = 5;
    public int FailedLoginLockoutMinutes { get; init; } = 15;

    public TimeSpan FailedLoginLockoutWindow => TimeSpan.FromMinutes(FailedLoginLockoutMinutes);
}
