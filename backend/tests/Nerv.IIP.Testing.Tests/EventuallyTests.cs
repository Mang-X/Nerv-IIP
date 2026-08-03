using Microsoft.Extensions.Time.Testing;

namespace Nerv.IIP.Testing.Tests;

public sealed class EventuallyTests
{
    [Fact]
    public async Task WaitAsync_ReturnsTheImmediateObservationWithoutAdvancingTime()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;

        var result = await Eventually.WaitAsync(
            "inventory is posted",
            _ => ValueTask.FromResult(++attempts),
            observation => observation == 1,
            observation => $"state={observation}",
            Options(),
            timeProvider: timeProvider);

        Assert.Equal(1, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task WaitAsync_ReturnsAfterTheSecondObservation()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var wait = Eventually.WaitAsync(
            "inventory is posted",
            _ => ValueTask.FromResult(++attempts),
            observation => observation == 2,
            observation => $"state={observation}",
            Options(),
            timeProvider: timeProvider).AsTask();

        await Task.Yield();
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(2, await wait);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WaitAsync_ReportsFakeTimeTimeoutWithAttemptsElapsedAndSanitizedLastObservation()
    {
        const string secret = "super-sensitive-value";
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;
        var wait = Eventually.WaitAsync(
            $"inventory token={secret}; state=is posted",
            _ => ValueTask.FromResult(++attempts),
            _ => false,
            observation => $"attempt={observation}; password={secret}",
            new EventuallyOptions(
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(5),
                [secret]),
            timeProvider: timeProvider).AsTask();

        await Task.Yield();
        for (var index = 0; index < 4; index++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            await Task.Yield();
        }

        var exception = await Assert.ThrowsAsync<EventuallyTimeoutException>(() => wait);
        Assert.Equal("inventory token=[REDACTED]; state=is posted", exception.Condition);
        Assert.Equal(4, exception.Attempts);
        Assert.Equal(TimeSpan.FromSeconds(20), exception.Elapsed);
        Assert.Equal("attempt=4; password=[REDACTED]", exception.LastObservation);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitAsync_PropagatesCallerCancellation()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var wait = Eventually.WaitAsync(
            "inventory is posted",
            _ => ValueTask.FromResult("pending"),
            _ => false,
            observation => observation,
            Options(),
            cancellation.Token,
            timeProvider).AsTask();

        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private static EventuallyOptions Options() => new(
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(5),
        []);
}
