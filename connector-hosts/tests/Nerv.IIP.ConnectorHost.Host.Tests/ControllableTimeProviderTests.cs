using Nerv.IIP.ConnectorHost.TestUtilities;

namespace Nerv.IIP.ConnectorHost.Host.Tests;

/// <summary>
/// Pins the causal chain behind the MAN-799 silent hang. The fake clock only moves when a test
/// advances it, so a timer registered *after* an <see cref="ControllableTimeProvider.Advance"/>
/// never sees that advance: it is created at the already-advanced "now" and nothing fires it
/// again. Tests that advance the clock on behalf of a background loop must therefore wait for the
/// loop's timer registration first — these tests are the executable statement of that rule.
/// </summary>
public sealed class ControllableTimeProviderTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(4);

    [Fact]
    public void Advance_before_registration_is_lost_by_the_timer_created_afterwards()
    {
        var clock = new ControllableTimeProvider();
        var fired = 0;

        clock.Advance(Tick);
        using var timer = clock.CreateTimer(_ => Interlocked.Increment(ref fired), null, Tick, Tick);

        // The advance already happened, so it cannot reach a timer that did not exist yet.
        Assert.Equal(0, Volatile.Read(ref fired));

        // Nothing re-delivers it either: only a *further* advance reaches the new due time. In the
        // Worker this is terminal, because the only thing that advances the clock is the test.
        clock.Advance(Tick - TimeSpan.FromTicks(1));
        Assert.Equal(0, Volatile.Read(ref fired));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(1, Volatile.Read(ref fired));
    }

    [Fact]
    public async Task Waiting_for_the_registration_keeps_the_advance_on_the_timer()
    {
        var clock = new ControllableTimeProvider();
        var fired = 0;
        var registration = clock.WaitForTimerEverCreatedAsync(Tick, Tick);
        Assert.False(registration.IsCompleted);

        using var timer = clock.CreateTimer(_ => Interlocked.Increment(ref fired), null, Tick, Tick);
        await registration.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(Tick);
        Assert.Equal(1, Volatile.Read(ref fired));
    }

    [Fact]
    public void Registration_barrier_completes_synchronously_once_the_timer_already_exists()
    {
        var clock = new ControllableTimeProvider();
        using var timer = clock.CreateTimer(static _ => { }, null, Tick, Tick);

        Assert.True(clock.WaitForTimerEverCreatedAsync(Tick, Tick).IsCompletedSuccessfully);
        Assert.False(clock.WaitForTimerEverCreatedAsync(TimeSpan.FromSeconds(2), Tick).IsCompleted);
    }
}
