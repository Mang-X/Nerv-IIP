using Xunit.Sdk;

namespace Nerv.IIP.Testing.Tests;

/// <summary>
/// Pins down which failures <see cref="Eventually.AssertAsync"/> retries. The XML contract on that method
/// says "a disposed context … is rethrown immediately"; these tests are what makes that a fact rather than
/// a claim, because <see cref="ObjectDisposedException"/> and Npgsql's
/// <c>NpgsqlOperationInProgressException</c> both <em>derive from</em>
/// <see cref="InvalidOperationException"/> and would be silently retried for the whole budget by an
/// <c>is InvalidOperationException</c> test.
/// </summary>
public sealed class EventuallyAssertTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Real-clock ceiling on the "rethrown immediately" cases. Without it, a regression that puts these
    /// exception types back on the retry list would <em>hang</em> the test — the fake clock is never
    /// advanced in those cases, so the retry loop's poll delay never completes — and a hang is not a red
    /// test. With it, the same regression fails in seconds.
    /// </summary>
    private static readonly TimeSpan OuterBudget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task AssertAsync_RetriesAPlainInvalidOperationExceptionUntilTheAssertionHolds()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;
        var wait = Eventually.AssertAsync(
            "the projection row exists",
            _ =>
            {
                attempts++;
                return attempts < 3
                    // EF Core's SingleAsync on a row that has not been projected yet throws exactly this.
                    ? throw new InvalidOperationException("Sequence contains no elements")
                    : Task.CompletedTask;
            },
            Options(),
            timeProvider: clock).AsTask();

        await DriveAsync(clock, wait);

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task AssertAsync_RetriesAnXunitAssertionFailure()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;
        var wait = Eventually.AssertAsync(
            "the projected quantity settles",
            _ =>
            {
                attempts++;
                Assert.Equal(2, attempts);
                return Task.CompletedTask;
            },
            Options(),
            timeProvider: clock).AsTask();

        await DriveAsync(clock, wait);

        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// The regression this whole file exists for: an <see cref="ObjectDisposedException"/> means the scope
    /// the assertion closes over is gone, so no amount of retrying can make it pass. It must surface as
    /// itself on the first attempt, not as a timeout 30 seconds later.
    /// </summary>
    [Fact]
    public async Task AssertAsync_RethrowsADisposedContextImmediatelyInsteadOfRetryingIt()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;

        var wait = Eventually.AssertAsync(
            "the projection row exists",
            _ =>
            {
                attempts++;
                throw new ObjectDisposedException("ApplicationDbContext");
            },
            Options(),
            timeProvider: clock).AsTask();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await wait.WaitAsync(OuterBudget));

        Assert.Equal(1, attempts);
        Assert.Contains("ApplicationDbContext", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Same rule, stated structurally rather than by naming one type: Npgsql's
    /// <c>NpgsqlOperationInProgressException</c> is the other real-world subclass that used to be
    /// swallowed. This stands in for it without dragging Npgsql into Nerv.IIP.Testing.Tests.
    /// </summary>
    [Fact]
    public async Task AssertAsync_RethrowsASubclassOfInvalidOperationExceptionImmediately()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;

        var wait = Eventually.AssertAsync(
            "the projection row exists",
            _ =>
            {
                attempts++;
                throw new ConnectionBusyLikeException();
            },
            Options(),
            timeProvider: clock).AsTask();

        await Assert.ThrowsAsync<ConnectionBusyLikeException>(
            async () => await wait.WaitAsync(OuterBudget));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AssertAsync_RethrowsAnUnrelatedExceptionImmediately()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var attempts = 0;

        var wait = Eventually.AssertAsync(
            "the projection row exists",
            _ =>
            {
                attempts++;
                throw new FormatException("bad connection string");
            },
            Options(),
            timeProvider: clock).AsTask();

        await Assert.ThrowsAsync<FormatException>(
            async () => await wait.WaitAsync(OuterBudget));

        Assert.Equal(1, attempts);
    }

    /// <summary>
    /// A retried assertion that never holds must still report the last assertion failure, not a bare
    /// timeout with no diagnosis.
    /// </summary>
    [Fact]
    public async Task AssertAsync_ReportsTheLastAssertionFailureOnTimeout()
    {
        var clock = new TimerRegistrationObservingTimeProvider();
        var wait = Eventually.AssertAsync(
            "the projected quantity settles",
            _ => throw new XunitException("Assert.Equal() Failure: quantity"),
            Options(),
            timeProvider: clock).AsTask();

        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(
            async () => await DriveAsync(clock, wait));

        Assert.Contains("assertion still failing", exception.LastObservation, StringComparison.Ordinal);
        Assert.Contains("quantity", exception.LastObservation, StringComparison.Ordinal);
    }

    private sealed class ConnectionBusyLikeException()
        : InvalidOperationException("A command is already in progress on this connection.");

    private static EventuallyOptions Options() => new(Window, PollInterval, []);

    /// <summary>
    /// Advances the fake clock one poll interval at a time, waiting for the <em>next</em> timer
    /// registration first: the budget's own <see cref="CancellationTokenSource"/> is timer #1 and each poll
    /// delay registers the next one.
    /// </summary>
    private static async Task DriveAsync(TimerRegistrationObservingTimeProvider clock, Task pending)
    {
        const int maxRounds = 10;
        for (var round = 1; !pending.IsCompleted; round++)
        {
            Assert.True(round <= maxRounds, $"The budget did not elapse within {maxRounds} poll rounds.");
            var barrier = clock.WaitForTimerCountAsync(round + 1);
            if (await Task.WhenAny(pending, barrier).ConfigureAwait(false) == pending)
            {
                break;
            }

            await barrier.ConfigureAwait(false);
            clock.Advance(PollInterval);
        }

        await pending.ConfigureAwait(false);
    }
}
