using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nerv.IIP.FileStorage.Infrastructure;
using Nerv.IIP.FileStorage.Infrastructure.Records;
using Nerv.IIP.FileStorage.Web.Application.Files;
using Nerv.IIP.Testing;

namespace Nerv.IIP.FileStorage.Web.Tests;

// Regression：#2110 approved spec 2026-10-07-r2。
// 真实 HostedService / collector + EF Core InMemory，只证明调度、取消与 scope 生命周期。
[Trait("Contract", "Regression")]
[Trait("Provider", "EFCoreInMemory")]
public sealed class FileStorageGarbageCollectionHostedServiceTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("", 300)]
    [InlineData("30", 30)]
    public async Task First_tick_waits_for_interval_and_two_rounds_use_the_host_clock(
        string configuredSeconds, int intervalSeconds)
    {
        var clock = new TimerRegistrationObservingTimeProvider(Epoch);
        var probe = new CollectionProbe();
        await using var factory = CreateFactory(clock, probe, configuredSeconds);
        var worker = Worker(factory);
        Assert.Same(clock, factory.Services.GetRequiredService<TimeProvider>());
        await clock.WaitForFirstTimerAsync();
        Assert.Equal(1, clock.TimersCreated);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.DownloadGrants.Add(DownloadGrantRecord.Create(
                "gc-grant", "gc-file", "gc-org", "test", "server-proxy", Epoch,
                Epoch.AddSeconds(intervalSeconds)));
            db.DownloadGrants.Add(DownloadGrantRecord.Create(
                "gc-next-grant", "gc-file", "gc-org", "test", "server-proxy", Epoch,
                Epoch.AddSeconds(2 * intervalSeconds)));
            // 同步保存只准备 fixture，不进入回收的异步保存观测 seam。
            db.SaveChanges();
        }

        clock.Advance(TimeSpan.FromSeconds(intervalSeconds - 1));
        await AssertCollectionCountStaysAsync(probe, 0, "no collection before the first tick");
        Assert.Equal(2, GrantCount(factory));
        clock.Advance(TimeSpan.FromSeconds(1));
        var first = await probe.NextAsync();
        await first.WaitForDisposalAsync();
        Assert.Equal(1, GrantCount(factory));
        Assert.Equal(1, probe.Count);

        clock.Advance(TimeSpan.FromSeconds(intervalSeconds));
        var second = await probe.NextAsync();
        await second.WaitForDisposalAsync();
        Assert.NotEqual(first.ContextId, second.ContextId);
        Assert.Equal(0, GrantCount(factory));
        Assert.Equal(2, probe.Count);
        await StopAsync(worker);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(2, probe.Count);
    }

    [Fact]
    public async Task Slow_collection_is_serial_and_the_pending_tick_gets_a_fresh_scope()
    {
        var clock = new TimerRegistrationObservingTimeProvider(Epoch);
        var probe = new CollectionProbe { HoldCollections = true };
        await using var factory = CreateFactory(clock, probe);
        var worker = Worker(factory);
        await clock.WaitForFirstTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        var first = await probe.NextAsync();
        clock.Advance(TimeSpan.FromSeconds(60));
        // 回收挂起时没有更强的“未重入”边沿，以有界持续观测排除重入。
        await AssertCollectionCountStaysAsync(probe, 1, "slow collection does not overlap another round");
        first.Release.TrySetResult();
        var second = await probe.NextAsync();
        await first.WaitForDisposalAsync();
        Assert.NotEqual(first.ContextId, second.ContextId);
        second.Release.TrySetResult();
        await second.WaitForDisposalAsync();
        await StopAsync(worker);
        Assert.Equal(2, probe.Count);
    }

    [Fact]
    public async Task Stop_cancels_an_active_collection_and_releases_its_scope()
    {
        var clock = new TimerRegistrationObservingTimeProvider(Epoch);
        var probe = new CollectionProbe { HoldCollections = true };
        await using var factory = CreateFactory(clock, probe);
        var worker = Worker(factory);
        await clock.WaitForFirstTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        var round = await probe.NextAsync();
        await StopAsync(worker);
        await round.WaitForDisposalAsync();
        Assert.True(worker.ExecuteTask!.IsCompleted);
        Assert.DoesNotContain(probe.Logs, entry => entry.Level == LogLevel.Error);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, probe.Count);
    }

    [Fact]
    public async Task Stop_while_waiting_for_first_tick_finishes_without_collection()
    {
        var clock = new TimerRegistrationObservingTimeProvider(Epoch);
        var probe = new CollectionProbe();
        await using var factory = CreateFactory(clock, probe);
        var worker = Worker(factory);
        await clock.WaitForFirstTimerAsync();
        await StopAsync(worker);
        Assert.True(worker.ExecuteTask!.IsCompleted);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, probe.Count);
    }

    [Fact]
    public async Task Failed_collection_logs_the_error_releases_scope_and_runs_next_tick()
    {
        var clock = new TimerRegistrationObservingTimeProvider(Epoch);
        var probe = new CollectionProbe { FailFirstCollection = true };
        await using var factory = CreateFactory(clock, probe);
        var worker = Worker(factory);
        await clock.WaitForFirstTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        var first = await probe.NextAsync();
        await first.WaitForDisposalAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        var second = await probe.NextAsync();
        await second.WaitForDisposalAsync();
        Assert.NotEqual(first.ContextId, second.ContextId);
        await StopAsync(worker);
        var error = Assert.Single(probe.Logs, entry => entry.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Equal(2, probe.Count);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        TimerRegistrationObservingTimeProvider clock, CollectionProbe probe, string interval = "30") =>
        new FileStorageWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FileStorage:GarbageCollection:IntervalSeconds", interval);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                // 只有 GC 可以在本测试时钟上注册 timer；其它宿主设施保留。
                var recovery = services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(UploadCommitRecoveryHostedService));
                services.Remove(recovery);
                services.AddSingleton<ILogger<FileStorageGarbageCollectionHostedService>>(probe);
                services.AddScoped(_ => new CollectionRound(probe));
                services.AddDbContext<ApplicationDbContext>((provider, options) =>
                    options.AddInterceptors(provider.GetRequiredService<CollectionRound>()));
            });
        });

    private static FileStorageGarbageCollectionHostedService Worker(WebApplicationFactory<Program> factory) =>
        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<FileStorageGarbageCollectionHostedService>());

    private static int GrantCount(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DownloadGrants.Count();
    }

    private static async Task StopAsync(FileStorageGarbageCollectionHostedService worker) =>
        await TestTimeout.RunAsync("stop FileStorage GC", async token => await worker.StopAsync(token),
            BoundedSignal.DefaultBudget);

    private static async Task AssertCollectionCountStaysAsync(CollectionProbe probe, int expected, string condition) =>
        await Consistently.StaysAsync(condition,
            _ => ValueTask.FromResult(probe.Count), count => count == expected,
            count => $"collections={count}",
            new EventuallyOptions(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(10), []));

    private sealed class CollectionProbe : ILogger<FileStorageGarbageCollectionHostedService>
    {
        private readonly Channel<CollectionRound> rounds = Channel.CreateUnbounded<CollectionRound>();
        private int count;
        public int Count => Volatile.Read(ref count);
        public bool HoldCollections { get; init; }
        public bool FailFirstCollection { get; init; }
        public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Logs { get; } = new();

        public int Enter(CollectionRound round)
        {
            var number = Interlocked.Increment(ref count);
            rounds.Writer.TryWrite(round);
            return number;
        }

        public async Task<CollectionRound> NextAsync() =>
            await TestTimeout.RunAsync("GC enters collection", token => rounds.Reader.ReadAsync(token),
                BoundedSignal.DefaultBudget);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Logs.Enqueue((logLevel, exception));
    }

    private sealed class CollectionRound(CollectionProbe probe) : SaveChangesInterceptor, IDisposable
    {
        private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid ContextId { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ContextId = eventData.Context!.ContextId.InstanceId;
            var number = probe.Enter(this);
            if (probe.HoldCollections)
            {
                await TestTimeout.RunAsync("release GC collection", async token => await Release.Task.WaitAsync(token),
                    BoundedSignal.DefaultBudget, cancellationToken);
            }
            if (probe.FailFirstCollection && number == 1)
            {
                throw new InvalidOperationException("Injected GC collection failure.");
            }
            return result;
        }

        public void Dispose() => disposed.TrySetResult();
        public Task WaitForDisposalAsync() => BoundedSignal.ObserveAsync(disposed.Task,
            "GC collection scope disposal", () => $"collections={probe.Count}");
    }
}
