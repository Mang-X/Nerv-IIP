using DotNetCore.CAP;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Nerv.IIP.Testing.Tests;

public sealed class CapTestHostTests
{
    [Fact]
    public async Task WaitForCapBootstrapAsync_IsAnImmediateNoOpWithoutCapRegistration()
    {
        var services = new StubServiceProvider();

        await CapTestHost.WaitForCapBootstrapAsync(services);
    }

    [Fact]
    public async Task WaitForCapBootstrapAsync_ReturnsWhenBootstrapHasCompleted()
    {
        await using var bootstrapper = new FakeBootstrapper(_ => Task.CompletedTask);
        await bootstrapper.StartAsync(CancellationToken.None);

        await CapTestHost.WaitForCapBootstrapAsync(new StubServiceProvider(bootstrapper));
    }

    [Fact]
    public async Task WaitForCapBootstrapAsync_AcceptsBootstrapCanceledByHostShutdown()
    {
        await using var bootstrapper = new FakeBootstrapper(
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.StopAsync(CancellationToken.None);

        await CapTestHost.WaitForCapBootstrapAsync(new StubServiceProvider(bootstrapper));
    }

    [Fact]
    public async Task WaitForCapBootstrapAsync_PropagatesCallerCancellation()
    {
        await using var bootstrapper = new FakeBootstrapper(
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
        await bootstrapper.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var wait = CapTestHost.WaitForCapBootstrapAsync(
            new StubServiceProvider(bootstrapper),
            cancellationToken: cancellation.Token).AsTask();

        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        await bootstrapper.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForCapBootstrapAsync_UsesTheThirtySecondFakeTimeBudgetAndSanitizedDiagnostic()
    {
        const string credential = "credential-secret";
        var timeProvider = new FakeTimeProvider();
        await using var bootstrapper = new FakeBootstrapper(
            cancellationToken => Task.Delay(
                Timeout.InfiniteTimeSpan,
                timeProvider,
                cancellationToken));
        await bootstrapper.StartAsync(CancellationToken.None);
        var services = new StubServiceProvider(bootstrapper, credential);
        var wait = CapTestHost.WaitForCapBootstrapAsync(
            services,
            timeProvider: timeProvider).AsTask();

        await ObserveAsync(
            bootstrapper.BootstrapTimersRegistered,
            "the bootstrap delay to register its fake-clock timer",
            () => $"fake now={timeProvider.GetUtcNow():O}");
        timeProvider.Advance(TimeSpan.FromSeconds(30));

        var exception = await Assert.ThrowsAsync<TestTimeoutException>(() => wait);
        Assert.Equal("CAP bootstrap completion", exception.Operation);
        Assert.Equal(TimeSpan.FromSeconds(30), exception.Elapsed);
        Assert.DoesNotContain(credential, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(StubServiceProvider), exception.Message, StringComparison.Ordinal);
        await bootstrapper.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForCapBootstrapAsync_UsesOneFakeClockForBootstrapAndDeadline()
    {
        var timeProvider = new FakeTimeProvider();
        await using var bootstrapper = new FakeBootstrapper(
            cancellationToken => Task.Delay(
                TimeSpan.FromSeconds(20),
                timeProvider,
                cancellationToken));
        await bootstrapper.StartAsync(CancellationToken.None);
        var wait = CapTestHost.WaitForCapBootstrapAsync(
            new StubServiceProvider(bootstrapper),
            timeProvider: timeProvider).AsTask();

        await ObserveAsync(
            bootstrapper.BootstrapTimersRegistered,
            "the bootstrap delay to register its fake-clock timer",
            () => $"fake now={timeProvider.GetUtcNow():O}");
        timeProvider.Advance(TimeSpan.FromSeconds(20));

        await ObserveAsync(
            wait,
            "the CAP bootstrap wait to observe the advanced fake clock",
            () => $"fake now={timeProvider.GetUtcNow():O}, bootstrap started={bootstrapper.IsStarted}");
    }

    /// <summary>
    /// Bounded wait on an edge-triggered signal. Reports the redacted condition, elapsed time,
    /// attempt count and last observation, so a lost fake-clock tick fails with a diagnosis instead
    /// of parking the test host (the MAN-799 failure mode).
    /// </summary>
    private static async Task ObserveAsync(
        Task observation,
        string condition,
        Func<string> lastObservation)
    {
        var budget = TimeSpan.FromSeconds(5);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await observation.WaitAsync(budget);
        }
        catch (TimeoutException)
        {
            throw new global::Xunit.Sdk.XunitException(
                $"Timed out waiting for {condition} after {elapsed.Elapsed.TotalSeconds:0.###}s "
                + $"(budget {budget.TotalSeconds:0.###}s, attempts 1/1 — single bounded await on a "
                + $"completion signal); last observation: {lastObservation()}");
        }
    }

    private sealed class FakeBootstrapper(Func<CancellationToken, Task> bootstrap) :
        BackgroundService,
        IBootstrapper
    {
        private readonly TaskCompletionSource _bootstrapTimersRegistered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsStarted { get; private set; }

        /// <summary>
        /// Completes once <c>bootstrap</c> has returned its pending task — i.e. once any fake-clock
        /// timer it creates is registered. <see cref="BackgroundService.StartAsync"/> does not
        /// guarantee that the <see cref="ExecuteAsync"/> body has run by the time it returns, so a
        /// test that advances the fake clock before this signal can register the timer *after* the
        /// advance: the timer is then due one interval further out, nothing advances the clock
        /// again, and the tick is lost permanently.
        /// </summary>
        public Task BootstrapTimersRegistered => _bootstrapTimersRegistered.Task;

        public async Task BootstrapAsync(CancellationToken cancellationToken)
        {
            var pending = bootstrap(cancellationToken);
            _bootstrapTimersRegistered.TrySetResult();
            await pending;
            IsStarted = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
            BootstrapAsync(stoppingToken);
    }

    private sealed class StubServiceProvider(
        IBootstrapper? bootstrapper = null,
        string? diagnostic = null) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IBootstrapper) ? bootstrapper : null;

        public override string ToString() =>
            $"{nameof(StubServiceProvider)} credential={diagnostic}";
    }
}
