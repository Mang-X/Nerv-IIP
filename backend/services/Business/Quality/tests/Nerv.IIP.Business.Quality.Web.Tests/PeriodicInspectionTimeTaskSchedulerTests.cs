using MediatR;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Queries.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Nerv.IIP.Business.Quality.Domain.AggregatesModel.PeriodicInspectionOperationAggregate;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class PeriodicInspectionTimeTaskSchedulerTests
{
    [Fact]
    public async Task Enabled_scheduler_dispatches_configured_scope_on_fake_time_ticks()
    {
        var start = DateTimeOffset.Parse("2026-08-24T01:00:00Z");
        var clock = new TimerRegistrationObservingTimeProvider(start);
        var sender = new CapturingSender(
            candidateCount: 2,
            afterCommandCaptured: count =>
            {
                if (count == 1)
                {
                    clock.Advance(TimeSpan.FromMinutes(1));
                }
            });
        await using var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var configuration = Configuration(enabled: true);
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<PeriodicInspectionTimeTaskScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitForCommandCountAsync(sender, 2);
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(TimeSpan.FromMinutes(59));
        await WaitForCommandCountAsync(sender, 4);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(2, sender.Queries.Count);
        Assert.All(sender.Queries, query => Assert.Equal(100, query.ContextBatchSize));
        Assert.Equal(
            [start.UtcDateTime, start.AddHours(1).UtcDateTime],
            sender.Queries.Select(query => query.NowUtc).ToArray());
        Assert.Equal(4, sender.Commands.Count);
        Assert.All(sender.Commands, command => Assert.Equal(24, command.MaxWindows));
        Assert.Equal(
            [start.UtcDateTime, start.UtcDateTime, start.AddHours(1).UtcDateTime, start.AddHours(1).UtcDateTime],
            sender.Commands.Select(command => command.NowUtc).ToArray());
    }

    [Fact]
    public async Task Each_candidate_is_dispatched_from_a_distinct_dependency_injection_scope()
    {
        var capture = new ScopeDispatchCapture();
        var clock = new TimerRegistrationObservingTimeProvider();
        await using var services = new ServiceCollection()
            .AddSingleton(capture)
            .AddScoped<ISender, ScopeObservingSender>()
            .BuildServiceProvider();
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: true),
            NullLogger<PeriodicInspectionTimeTaskScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await capture.CommandsDispatched.WaitForAsync(2);
        await scheduler.StopAsync(CancellationToken.None);

        var queryScopeId = Assert.Single(capture.QueryDispatches).ScopeId;
        var commandScopeIds = capture.CommandDispatches.Select(dispatch => dispatch.ScopeId).ToArray();
        Assert.Equal(2, commandScopeIds.Distinct().Count());
        Assert.DoesNotContain(queryScopeId, commandScopeIds);
    }

    [Fact]
    public async Task Disabled_scheduler_dispatches_no_generation_command()
    {
        var sender = new CapturingSender();
        var clock = new FakeTimeProvider();
        await using var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: false),
            NullLogger<PeriodicInspectionTimeTaskScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Poison_candidate_does_not_prevent_the_next_candidate_from_running()
    {
        var sender = new CapturingSender(candidateCount: 2, failFirstCandidate: true);
        var logger = new RecordingLogger();
        var clock = new TimerRegistrationObservingTimeProvider();
        await using var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: true),
            logger,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitForCommandCountAsync(sender, 2);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(["OP-001", "OP-002"], sender.Commands.Select(x => x.OperationId).ToArray());
        Assert.Matches("^[0-9a-f-]{36}$", Assert.Single(logger.Scopes)["correlationId"]!.ToString());
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error
            && Equals(entry.State.GetValueOrDefault("OperationId"), "OP-001"));
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information
            && Equals(entry.State.GetValueOrDefault("GeneratedCount"), 1)
            && Equals(entry.State.GetValueOrDefault("FailedCount"), 1));
    }

    [Theory]
    [InlineData("0", "1001")]
    [InlineData("-1", "0")]
    public async Task Invalid_generation_bounds_fall_back_to_governed_defaults(string maxWindows, string batchSize)
    {
        var sender = new CapturingSender();
        var clock = new TimerRegistrationObservingTimeProvider();
        await using var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(enabled: true, maxWindows: maxWindows, batchSize: batchSize),
            NullLogger<PeriodicInspectionTimeTaskScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitForCommandCountAsync(sender, 1);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(100, Assert.Single(sender.Queries).ContextBatchSize);
        Assert.Equal(24, Assert.Single(sender.Commands).MaxWindows);
    }

    [Fact]
    public async Task Enabled_scheduler_without_a_configured_scope_dispatches_nothing()
    {
        var sender = new CapturingSender();
        await using var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Quality:PeriodicInspectionTime:Enabled"] = "true",
            }).Build();
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<PeriodicInspectionTimeTaskScheduler>.Instance,
            new FakeTimeProvider());

        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(sender.Requests);
    }

    private static IConfiguration Configuration(
        bool enabled,
        string maxWindows = "24",
        string batchSize = "100") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Quality:PeriodicInspectionTime:Enabled"] = enabled.ToString(),
            ["Quality:PeriodicInspectionTime:OrganizationId"] = "org-001",
            ["Quality:PeriodicInspectionTime:EnvironmentId"] = "env-dev",
            ["Quality:PeriodicInspectionTime:Interval"] = "01:00:00",
            ["Quality:PeriodicInspectionTime:MaxWindowsPerContext"] = maxWindows,
            ["Quality:PeriodicInspectionTime:ContextBatchSize"] = batchSize,
        })
        .Build();

    private static Task WaitForCommandCountAsync(CapturingSender sender, int expected) =>
        sender.CommandsDispatched.WaitForAsync(expected);

    /// <summary>
    /// Turns "the scheduler has dispatched its N-th command" into an awaitable edge, published by the
    /// stub sender at the moment it records the dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dispatches these tests wait for are produced by the <em>first, immediate</em>
    /// <c>TryGenerateAllScopesAsync</c> pass, which <see cref="PeriodicInspectionTimeTaskScheduler"/> runs
    /// before it ever awaits its <c>PeriodicTimer</c>. That pass is therefore not a time fact at all — it is
    /// "the background task's continuation has run" — and it is the one thing the injected
    /// <see cref="FakeTimeProvider"/> cannot model.
    /// </para>
    /// <para>
    /// Polling for it with <see cref="Eventually.WaitAsync"/> made the verdict depend on how many times the
    /// poll loop's own continuations were rescheduled inside a real-time budget: the first observation always
    /// sees zero commands, so passing required at least one further poll cycle to be serviced before the
    /// budget elapsed. On a saturated runner those continuations are exactly what starves, which is what
    /// #3323 recorded on CI (2 observations inside a 2 s / 10 ms window). Handing that window the fake clock
    /// instead does not help and is not available here: nothing may advance this clock while the wait is in
    /// flight, because the very same clock drives the subject's <c>PeriodicTimer</c> and any advance large
    /// enough to release a poll would also fire an extra scan that the assertions below forbid.
    /// </para>
    /// <para>
    /// An edge needs one continuation rather than a serviced poll cadence, so a healthy run never consults a
    /// clock at all. <see cref="BoundedSignal"/> still bounds the wait in wall clock, for the single job a
    /// budget has left here: turning a lost edge into a diagnosis instead of a parked run.
    /// </para>
    /// </remarks>
    private sealed class DispatchCountSignal(string subject)
    {
        private readonly Lock gate = new();
        private readonly List<(int ExpectedCount, TaskCompletionSource Reached)> waiters = [];
        private int dispatched;

        public int Dispatched => Volatile.Read(ref dispatched);

        public void Record()
        {
            var reached = Interlocked.Increment(ref dispatched);
            List<TaskCompletionSource>? released = null;
            lock (gate)
            {
                for (var index = waiters.Count - 1; index >= 0; index--)
                {
                    if (waiters[index].ExpectedCount > reached)
                    {
                        continue;
                    }

                    (released ??= []).Add(waiters[index].Reached);
                    waiters.RemoveAt(index);
                }
            }

            foreach (var waiter in released ?? [])
            {
                waiter.TrySetResult();
            }
        }

        /// <remarks>
        /// The already-reached check and the registration share <see cref="gate"/> with the release scan, so a
        /// dispatch that lands between them cannot be missed: it either finds the waiter registered or has
        /// already incremented the counter the check reads.
        /// </remarks>
        public Task WaitForAsync(int expectedCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(expectedCount, 1);

            TaskCompletionSource reached;
            lock (gate)
            {
                if (Volatile.Read(ref dispatched) >= expectedCount)
                {
                    return Task.CompletedTask;
                }

                reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waiters.Add((expectedCount, reached));
            }

            return BoundedSignal.ObserveAsync(
                reached.Task,
                $"the scheduler to dispatch {subject} #{expectedCount}",
                () => $"dispatched={Dispatched}; expected>={expectedCount}");
        }
    }

    private sealed class CapturingSender(
        int candidateCount = 1,
        bool failFirstCandidate = false,
        Action<int>? afterCommandCaptured = null) : ISender
    {
        private int commandCount;
        public List<object> Requests { get; } = [];
        public List<ListDuePeriodicInspectionTimeContextsQuery> Queries { get; } = [];
        public List<GeneratePeriodicInspectionTimeTaskForContextCommand> Commands { get; } = [];

        /// <summary>Edge for "the scheduler has dispatched the N-th generation command".</summary>
        public DispatchCountSignal CommandsDispatched { get; } = new("periodic inspection generation command");

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (request is ListDuePeriodicInspectionTimeContextsQuery query)
            {
                Queries.Add(query);
                IReadOnlyList<DuePeriodicInspectionTimeContext> candidates = Enumerable.Range(1, candidateCount)
                    .Select(index => new DuePeriodicInspectionTimeContext(
                        $"WO-{index:000}",
                        $"OP-{index:000}",
                        new PeriodicInspectionRuntimeContextId(Guid.CreateVersion7())))
                    .ToArray();
                return Task.FromResult((TResponse)(object)candidates);
            }

            var command = Assert.IsType<GeneratePeriodicInspectionTimeTaskForContextCommand>(request);
            Commands.Add(command);
            commandCount++;
            afterCommandCaptured?.Invoke(commandCount);
            CommandsDispatched.Record();
            if (failFirstCandidate && commandCount == 1)
            {
                throw new InvalidOperationException("poison candidate");
            }

            return Task.FromResult((TResponse)(object)1);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ScopeDispatchCapture
    {
        public ConcurrentQueue<ScopeDispatch> QueryDispatches { get; } = new();
        public ConcurrentQueue<ScopeDispatch> CommandDispatches { get; } = new();

        /// <summary>Edge for "the scheduler has dispatched the N-th scoped candidate command".</summary>
        public DispatchCountSignal CommandsDispatched { get; } = new("scoped periodic inspection candidate command");
    }

    private sealed record ScopeDispatch(Guid ScopeId, object Request);

    private sealed class ScopeObservingSender(ScopeDispatchCapture capture) : ISender
    {
        private readonly Guid scopeId = Guid.CreateVersion7();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request is ListDuePeriodicInspectionTimeContextsQuery)
            {
                capture.QueryDispatches.Enqueue(new ScopeDispatch(scopeId, request));
                IReadOnlyList<DuePeriodicInspectionTimeContext> candidates =
                [
                    new("WO-001", "OP-001", new PeriodicInspectionRuntimeContextId(Guid.CreateVersion7())),
                    new("WO-002", "OP-002", new PeriodicInspectionRuntimeContextId(Guid.CreateVersion7())),
                ];
                return Task.FromResult((TResponse)(object)candidates);
            }

            Assert.IsType<GeneratePeriodicInspectionTimeTaskForContextCommand>(request);
            capture.CommandDispatches.Enqueue(new ScopeDispatch(scopeId, request));
            capture.CommandsDispatched.Record();
            return Task.FromResult((TResponse)(object)1);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingLogger : ILogger<PeriodicInspectionTimeTaskScheduler>
    {
        public List<IReadOnlyDictionary<string, object?>> Scopes { get; } = [];
        public List<(LogLevel Level, IReadOnlyDictionary<string, object?> State)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(ToDictionary(state));
            return NoopDisposable.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = eventId;
            _ = exception;
            _ = formatter;
            Entries.Add((logLevel, ToDictionary(state)));
        }

        private static IReadOnlyDictionary<string, object?> ToDictionary<TState>(TState state)
            => state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?> { ["value"] = state };

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();
            public void Dispose()
            {
            }
        }
    }
}
