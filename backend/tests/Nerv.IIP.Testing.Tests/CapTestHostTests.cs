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

        await Task.Yield();
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

        await Task.Yield();
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        await Task.Yield();

        Assert.True(wait.IsCompletedSuccessfully);
        await wait;
    }

    private sealed class FakeBootstrapper(Func<CancellationToken, Task> bootstrap) :
        BackgroundService,
        IBootstrapper
    {
        public bool IsStarted { get; private set; }

        public async Task BootstrapAsync(CancellationToken cancellationToken)
        {
            await bootstrap(cancellationToken);
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
