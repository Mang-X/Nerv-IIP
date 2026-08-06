namespace Nerv.IIP.Testing.Tests;

public sealed class ConsistentlyTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StaysAsync_ReturnsTheLastObservationWhenTheWindowElapsesWithoutAViolation()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;
        var wait = Consistently.StaysAsync(
            "the deduplicated scope dispatches exactly one command",
            _ => ValueTask.FromResult(++attempts),
            _ => true,
            observation => $"commands={observation}",
            Options(),
            timeProvider: clock).AsTask();

        var last = await DriveAsync(clock, wait);

        // Window 22s / poll 5s: observations land at 0s, 5s, 10s, 15s, 20s and the window then closes.
        Assert.Equal(5, last);
        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task StaysAsync_FailsOnTheFirstViolatingObservationInsteadOfAtTheEndOfTheWindow()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var observations = new Queue<int>([1, 1, 2, 1, 1, 1]);
        var wait = Consistently.StaysAsync(
            "commands stay at 1",
            _ => ValueTask.FromResult(observations.Dequeue()),
            observation => observation == 1,
            observation => $"commands={observation}",
            Options(),
            timeProvider: clock).AsTask();

        var exception = await Assert.ThrowsAsync<ConsistentlyViolatedException>(
            async () => await DriveAsync(clock, wait));

        Assert.Equal("commands stay at 1", exception.Condition);
        Assert.Equal(3, exception.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(10), exception.Elapsed);
        Assert.Equal("commands=2", exception.ViolatingObservation);

        // The window was 22s wide; the failure landed on the third observation, not at its end.
        Assert.Equal(3, observations.Count);
    }

    [Fact]
    public async Task StaysAsync_SanitizesTheConditionAndTheViolatingObservation()
    {
        const string secret = "super-sensitive-value";
        var clock = new TimerRegistrationObservingTimeProvider();

        var wait = Consistently.StaysAsync(
            $"connection={secret}; state=commands stay at 1",
            _ => ValueTask.FromResult(2),
            observation => observation == 1,
            observation => $"commands={observation}; token={secret}",
            new EventuallyOptions(Window, PollInterval, [secret]),
            timeProvider: clock).AsTask();

        var exception = await Assert.ThrowsAsync<ConsistentlyViolatedException>(() => wait);
        Assert.Equal("connection=[REDACTED]; state=commands stay at 1", exception.Condition);
        Assert.Equal("commands=2; token=[REDACTED]", exception.ViolatingObservation);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaysAsync_PropagatesCallerCancellation()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        using var callerSource = new CancellationTokenSource();
        var wait = Consistently.StaysAsync(
            "commands stay at 1",
            async token =>
            {
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"commands={observation}",
            Options(),
            callerSource.Token,
            clock).AsTask();

        await callerSource.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(callerSource.Token, exception.CancellationToken);
    }

    /// <summary>
    /// A window that closes while the very first observation is still in flight learned nothing about the
    /// invariant, so it must not be reported as a violation. The canonical real case is a negative
    /// assertion whose observation is a query against a cold Docker PostgreSQL on a loaded runner: calling
    /// that "the invariant was violated" turns slow infrastructure into a business defect, and the
    /// diagnostic would have to name an observation that never existed.
    /// </summary>
    [Fact]
    public async Task StaysAsync_ReportsATimeoutRatherThanAViolationWhenNoObservationEverCompleted()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Consistently.StaysAsync(
            "no second maintenance work order is generated",
            async token =>
            {
                observationStarted.TrySetResult();
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            Options(),
            timeProvider: clock).AsTask();

        // The window's own CancellationTokenSource is timer #1; advancing past the whole window while the
        // first observation is still parked is exactly the race being pinned down.
        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(() => wait);
        Assert.Equal("no second maintenance work order is generated", exception.Condition);
        Assert.Equal(0, exception.Attempts);
        Assert.Equal(Window, exception.Elapsed);
        Assert.Contains("never observed", exception.Message, StringComparison.Ordinal);
        Assert.Contains("not a violation", exception.Message, StringComparison.Ordinal);
        Assert.IsNotType<ConsistentlyViolatedException>(exception);
    }

    [Fact]
    public async Task StaysAsync_SanitizesTheConditionOnTheNoObservationTimeout()
    {
        const string secret = "Host=db;Password=super-sensitive-value";
        var clock = new TimerRegistrationObservingTimeProvider();
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wait = Consistently.StaysAsync(
            $"connection={secret}; state=no second work order",
            async token =>
            {
                observationStarted.TrySetResult();
                await PendingOperation.UntilCanceledAsync(token).ConfigureAwait(false);
                return 1;
            },
            observation => observation == 1,
            observation => $"workOrders={observation}",
            new EventuallyOptions(Window, PollInterval, [secret]),
            timeProvider: clock).AsTask();

        await observationStarted.Task;
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(Window);

        var exception = await Assert.ThrowsAsync<ConsistentlyObservationTimeoutException>(() => wait);
        Assert.Equal("connection=[REDACTED]; state=no second work order", exception.Condition);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Advances the fake clock one poll interval at a time, always waiting for the *next* timer
    /// registration first. The window's own <see cref="CancellationTokenSource"/> registers timer #1 and
    /// each poll delay registers the next one, so timer #(round + 1) is the edge that makes advancing
    /// round <c>round</c> safe.
    /// </summary>
    private static async Task<int> DriveAsync(TimerRegistrationObservingTimeProvider clock, Task<int> pending)
    {
        const int maxRounds = 10;
        for (var round = 1; !pending.IsCompleted; round++)
        {
            Assert.True(round <= maxRounds, $"The stability window did not close within {maxRounds} poll rounds.");
            var barrier = clock.WaitForTimerCountAsync(round + 1);
            if (await Task.WhenAny(pending, barrier).ConfigureAwait(false) == pending)
            {
                break;
            }

            await barrier.ConfigureAwait(false);
            clock.Advance(PollInterval);
        }

        return await pending.ConfigureAwait(false);
    }

    private static EventuallyOptions Options() => new(Window, PollInterval, []);
}
