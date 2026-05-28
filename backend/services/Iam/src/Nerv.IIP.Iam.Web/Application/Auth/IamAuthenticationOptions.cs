namespace Nerv.IIP.Iam.Web.Application.Auth;

public sealed class IamAuthenticationOptions
{
    public int FailedLoginLockoutThreshold { get; init; } = 5;
    public int FailedLoginLockoutMinutes { get; init; } = 15;

    public int EffectiveFailedLoginLockoutThreshold => FailedLoginLockoutThreshold > 0
        ? FailedLoginLockoutThreshold
        : 5;

    public TimeSpan EffectiveFailedLoginLockoutWindow => TimeSpan.FromMinutes(FailedLoginLockoutMinutes > 0
        ? FailedLoginLockoutMinutes
        : 15);
}
