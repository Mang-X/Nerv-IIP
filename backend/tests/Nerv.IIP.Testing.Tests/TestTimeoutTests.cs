using Microsoft.Extensions.Time.Testing;

namespace Nerv.IIP.Testing.Tests;

public sealed class TestTimeoutTests
{
    [Fact]
    public async Task RunAsync_ReturnsGenericResult()
    {
        var result = await TestTimeout.RunAsync(
            "read inventory",
            _ => ValueTask.FromResult(42),
            TimeSpan.FromSeconds(10));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_CompletesNonGenericAction()
    {
        var invoked = false;

        await TestTimeout.RunAsync(
            "read inventory",
            _ =>
            {
                invoked = true;
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromSeconds(10));

        Assert.True(invoked);
    }

    [Fact]
    public async Task RunAsync_CancelsAHangingOperationAndReportsSanitizedFakeTimeTimeout()
    {
        const string secret = "postgres-password";
        var timeProvider = new TimerRegistrationObservingTimeProvider();
        var wait = TestTimeout.RunAsync(
            $"connect with credential={secret}",
            async cancellationToken =>
                await PendingOperation.UntilCanceledAsync(cancellationToken),
            TimeSpan.FromSeconds(10),
            timeProvider: timeProvider,
            sensitiveValues: [secret]).AsTask();

        // The budget's own timer is the only one on this clock. Advancing before it is registered
        // would re-base it on the advanced now and lose the tick, so wait for the registration edge
        // instead of yielding once and hoping.
        await timeProvider.WaitForFirstTimerAsync();
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        var exception = await Assert.ThrowsAsync<TestTimeoutException>(() => wait);
        Assert.Equal("connect with credential=[REDACTED]", exception.Operation);
        Assert.Equal(TimeSpan.FromSeconds(10), exception.Elapsed);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_PropagatesCallerCancellation()
    {
        var timeProvider = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var wait = TestTimeout.RunAsync(
            "read inventory",
            async actionCancellationToken =>
                await PendingOperation.UntilCanceledAsync(actionCancellationToken),
            TimeSpan.FromSeconds(10),
            cancellation.Token,
            timeProvider).AsTask();

        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }
}
