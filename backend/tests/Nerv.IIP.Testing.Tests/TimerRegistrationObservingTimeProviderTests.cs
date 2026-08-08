namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// The two constructors of <see cref="TimerRegistrationObservingTimeProvider"/>.
/// </summary>
/// <remarks>
/// Why the anchor exists at all, and why it is a constructor parameter rather than a later
/// <c>SetUtcNow</c> call, is documented once on that constructor — see
/// <see cref="TimerRegistrationObservingTimeProvider"/>. These tests pin the facts that documentation
/// claims: the start-anchored clock really starts where it is told, registrations are still published from
/// it, and <em>both</em> constructors reject a non-positive budget by the caller-facing
/// <c>registrationBudget</c> parameter name.
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

    /// <summary>
    /// Both constructors reject a non-positive registration budget, and they reject it by the caller-facing
    /// parameter name: a budget of zero or less would turn every <c>WaitFor…</c> barrier into an immediate
    /// timeout, i.e. silently disable the very thing this provider exists for.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void BothConstructors_RejectANonPositiveRegistrationBudget(long budgetTicks)
    {
        var budget = TimeSpan.FromTicks(budgetTicks);

        var anchored = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimerRegistrationObservingTimeProvider(DateTimeOffset.UtcNow, budget));
        var unanchored = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TimerRegistrationObservingTimeProvider(budget));

        Assert.Equal("registrationBudget", anchored.ParamName);
        Assert.Equal("registrationBudget", unanchored.ParamName);
    }
}
