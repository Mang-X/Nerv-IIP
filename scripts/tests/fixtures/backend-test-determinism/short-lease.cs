using System;

public static class ShortLeaseFixture
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan SecondaryLease = TimeSpan.FromMilliseconds(5_00);
}
