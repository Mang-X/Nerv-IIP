namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// The start-anchored constructor of <see cref="TimerRegistrationObservingTimeProvider"/>.
/// </summary>
/// <remarks>
/// A subject whose <em>other</em> guard still reads the process wall clock (for example a domain rule that
/// rejects a deadline in the past) cannot be driven by a clock parked in the year 2000, so those tests anchor
/// at real now. The anchor has to be part of construction: <c>SetUtcNow</c> after the fact fires every timer
/// already registered against the clock, which is only harmless while nothing has registered yet — precisely
/// the kind of ordering assumption this provider exists to remove. These tests pin that the anchored
/// constructor really starts where it is told and still publishes registrations.
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
