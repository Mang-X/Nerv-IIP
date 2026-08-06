using DotNetCore.CAP;
using Microsoft.Extensions.Hosting;

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
        await using var bootstrapper = new FakeBootstrapper(PendingOperation.UntilCanceledAsync);
        await bootstrapper.StartAsync(CancellationToken.None);
        await bootstrapper.StopAsync(CancellationToken.None);

        await CapTestHost.WaitForCapBootstrapAsync(new StubServiceProvider(bootstrapper));
    }

    [Fact]
    public async Task WaitForCapBootstrapAsync_PropagatesCallerCancellation()
    {
        await using var bootstrapper = new FakeBootstrapper(PendingOperation.UntilCanceledAsync);
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
        var timeProvider = new TimerRegistrationObservingTimeProvider();
        await using var bootstrapper = new FakeBootstrapper(PendingOperation.UntilCanceledAsync);
        await bootstrapper.StartAsync(CancellationToken.None);
        var services = new StubServiceProvider(bootstrapper, credential);
        var wait = CapTestHost.WaitForCapBootstrapAsync(
            services,
            timeProvider: timeProvider).AsTask();

        // The only timer on this clock is the wait's own 30 s deadline; the bootstrap stays pending
        // without any timer at all. Advancing before that deadline is registered would re-base it on
        // the advanced now and lose the tick, so the registration itself is the barrier.
        await timeProvider.WaitForFirstTimerAsync();
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
        var timeProvider = new TimerRegistrationObservingTimeProvider();
        await using var bootstrapper = new FakeBootstrapper(
            cancellationToken => FakeClockElapsedAsync(
                TimeSpan.FromSeconds(20),
                timeProvider,
                cancellationToken));
        await bootstrapper.StartAsync(CancellationToken.None);
        var wait = CapTestHost.WaitForCapBootstrapAsync(
            new StubServiceProvider(bootstrapper),
            timeProvider: timeProvider).AsTask();

        await BoundedSignal.ObserveAsync(
            bootstrapper.BootstrapTimersRegistered,
            "the bootstrap to register its 20 s timer on the fake clock",
            () => $"fake now={timeProvider.GetUtcNow():O}, timers registered={timeProvider.TimersCreated}");
        timeProvider.Advance(TimeSpan.FromSeconds(20));

        await BoundedSignal.ObserveAsync(
            wait,
            "the CAP bootstrap wait to observe the advanced fake clock",
            () => $"fake now={timeProvider.GetUtcNow():O}, bootstrap started={bootstrapper.IsStarted}");
    }

    /// <summary>
    /// Completes once <paramref name="timeProvider"/>'s clock has advanced past
    /// <paramref name="dueTime"/>, or throws when <paramref name="cancellationToken"/> is canceled.
    /// The timer is registered before this method returns, which is what makes
    /// <see cref="FakeBootstrapper.BootstrapTimersRegistered"/> a real barrier: the caller can await
    /// the signal knowing the timer exists rather than inferring it from a pending task.
    /// </summary>
    private static Task FakeClockElapsedAsync(
        TimeSpan dueTime,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var elapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = timeProvider.CreateTimer(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            elapsed,
            dueTime,
            Timeout.InfiniteTimeSpan);
        var registration = cancellationToken.Register(
            static state =>
            {
                var (source, token) = ((TaskCompletionSource Source, CancellationToken Token))state!;
                source.TrySetCanceled(token);
            },
            (Source: elapsed, Token: cancellationToken));

        return AwaitAsync(elapsed.Task, timer, registration);

        static async Task AwaitAsync(Task pending, ITimer timer, CancellationTokenRegistration registration)
        {
            using (timer)
            using (registration)
            {
                await pending.ConfigureAwait(false);
            }
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
