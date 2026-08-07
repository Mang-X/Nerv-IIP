namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// The start-anchored constructor of <see cref="TimerRegistrationObservingTimeProvider"/>.
/// </summary>
/// <remarks>
/// Why the anchor exists at all, and why it is a constructor parameter rather than a later
/// <c>SetUtcNow</c> call, is documented once on that constructor — see
/// <see cref="TimerRegistrationObservingTimeProvider"/>. These tests pin the three facts that
/// documentation claims: the clock really starts where it is told, registrations are still published, and a
/// non-positive budget is rejected.
/// </remarks>
public sealed class TimerRegistrationObservingTimeProviderTests
{
    private static readonly TimeSpan OuterBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AnchoredConstructor_StartsAtTheGivenInstantAndStillPublishesRegistrations()
    {
        var anchor = DateTimeOffset.UtcNow;
        var clock = new TimerRegistrationObservingTimeProvider(anchor);

        Assert.Equal(anchor, clock.GetUtcNow());
        Assert.Equal(0, clock.TimersCreated);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        var tick = timer.WaitForNextTickAsync().AsTask();
        await clock.WaitForTimerCountAsync(1).WaitAsync(OuterBudget);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await tick.WaitAsync(OuterBudget));
        Assert.Equal(1, clock.TimersCreated);
        Assert.Equal(anchor.AddMinutes(1), clock.GetUtcNow());
    }

    [Fact]
    public void AnchoredConstructor_RejectsANonPositiveRegistrationBudget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimerRegistrationObservingTimeProvider(DateTimeOffset.UtcNow, TimeSpan.Zero));
    }
}
