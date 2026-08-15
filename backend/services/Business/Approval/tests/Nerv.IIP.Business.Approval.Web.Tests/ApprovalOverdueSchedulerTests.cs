using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Scheduling;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalOverdueSchedulerTests
{
    [Fact]
    public async Task Scheduler_dispatches_configured_overdue_check_scope()
    {
        var sender = new CapturingSender();
        var clock = new FakeTimeProvider();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Approval:OverdueCheck:Enabled"] = "true",
                ["Approval:OverdueCheck:OrganizationId"] = "org-001",
                ["Approval:OverdueCheck:EnvironmentId"] = "env-dev",
                ["Approval:OverdueCheck:Interval"] = "01:00:00",
            })
            .Build();
        var scheduler = new ApprovalOverdueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<ApprovalOverdueScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.LastCommand is not null || scheduler.ExecuteTask?.IsCompleted == true);

        Assert.Equal(new CheckOverdueApprovalStepsCommand("org-001", "env-dev"), sender.LastCommand);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_dispatches_all_configured_overdue_check_scopes()
    {
        var sender = new CapturingSender();
        var clock = new FakeTimeProvider();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Approval:OverdueCheck:Enabled"] = "true",
                ["Approval:OverdueCheck:Scopes:0:OrganizationId"] = "org-001",
                ["Approval:OverdueCheck:Scopes:0:EnvironmentId"] = "env-dev",
                ["Approval:OverdueCheck:Scopes:1:OrganizationId"] = "org-002",
                ["Approval:OverdueCheck:Scopes:1:EnvironmentId"] = "env-prod",
                ["Approval:OverdueCheck:Interval"] = "01:00:00",
            })
            .Build();
        var scheduler = new ApprovalOverdueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<ApprovalOverdueScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.Commands.Count == 2 || scheduler.ExecuteTask?.IsCompleted == true);

        Assert.Equal(
            [
                new CheckOverdueApprovalStepsCommand("org-001", "env-dev"),
                new CheckOverdueApprovalStepsCommand("org-002", "env-prod"),
            ],
            sender.Commands);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_deduplicates_matching_scopes_from_array_and_legacy_keys()
    {
        var sender = new CapturingSender();
        var clock = new TimerRegistrationObservingTimeProvider();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Approval:OverdueCheck:Enabled"] = "true",
                ["Approval:OverdueCheck:Scopes:0:OrganizationId"] = "org-001",
                ["Approval:OverdueCheck:Scopes:0:EnvironmentId"] = "env-dev",
                ["Approval:OverdueCheck:OrganizationId"] = "org-001",
                ["Approval:OverdueCheck:EnvironmentId"] = "env-dev",
                ["Approval:OverdueCheck:Interval"] = "01:00:00",
            })
            .Build();
        var scheduler = new ApprovalOverdueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<ApprovalOverdueScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.Commands.Count > 0 || scheduler.ExecuteTask?.IsCompleted == true);
        await AssertStaysAtAsync(() => sender.Commands.Count, 1);
        Assert.Equal([new CheckOverdueApprovalStepsCommand("org-001", "env-dev")], sender.Commands);

        // Advancing a fake clock is only safe once the timer that must observe the advance actually exists.
        // "A command was dispatched" is not that fact: it only implies the timer exists because
        // ApprovalOverdueScheduler.ExecuteAsync happens to construct the PeriodicTimer before the first
        // TryCheckAllScopesAsync call today. Once that ordering no longer holds, the advance lands before
        // the registration, the tick is re-based on the advanced now and is lost for good. Measured: with
        // the dispatch-count barrier, swapping those two statements and giving the first pass 1.5 s fails
        // this test; with the barrier below, the same production code passes. The registration itself is
        // the observable edge, and it does not depend on the order those statements are written in.
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(TimeSpan.FromHours(1));
        await WaitUntilAsync(() => sender.Commands.Count > 1 || scheduler.ExecuteTask?.IsCompleted == true);
        await AssertStaysAtAsync(() => sender.Commands.Count, 2);

        Assert.Equal(
            [
                new CheckOverdueApprovalStepsCommand("org-001", "env-dev"),
                new CheckOverdueApprovalStepsCommand("org-001", "env-dev"),
            ],
            sender.Commands);

        await scheduler.StopAsync(CancellationToken.None);

        // Executable guard for the barrier's premise: this clock's only timer registrant is the scheduler's
        // single PeriodicTimer (the sender never touches it; Eventually/Consistently poll TimeProvider.System),
        // so the total is exactly 1 — a second registrant would otherwise satisfy WaitForTimerCountAsync(1)
        // vacuously and turn this test intermittently red elsewhere. This scheduler's registration order is the
        // mirror of the Inventory worker's, so the pre-shutdown race that makes the post-StopAsync position
        // provably load-bearing there does not reproduce here — the position is taken anyway, as the one that
        // holds structurally rather than by timing. The reasoning, the scope of the guard and the measurement
        // behind them are recorded once in docs/architecture/backend-test-determinism.md, §MAN-808.
        Assert.Equal(1, clock.TimersCreated);
    }

    [Fact]
    public async Task Scheduler_keeps_running_when_one_overdue_check_fails()
    {
        var sender = new ThrowingSender();
        var clock = new FakeTimeProvider();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Approval:OverdueCheck:Enabled"] = "true",
                ["Approval:OverdueCheck:OrganizationId"] = "org-001",
                ["Approval:OverdueCheck:EnvironmentId"] = "env-dev",
                ["Approval:OverdueCheck:Interval"] = "00:00:00.010",
            })
            .Build();
        var scheduler = new ApprovalOverdueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<ApprovalOverdueScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.Attempts > 0 || scheduler.ExecuteTask?.IsCompleted == true);

        Assert.True(sender.Attempts > 0);
        Assert.False(scheduler.ExecuteTask?.IsFaulted ?? false);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_falls_back_to_default_interval_when_configured_interval_is_not_positive()
    {
        var sender = new CapturingSender();
        var clock = new FakeTimeProvider();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Approval:OverdueCheck:Enabled"] = "true",
                ["Approval:OverdueCheck:OrganizationId"] = "org-001",
                ["Approval:OverdueCheck:EnvironmentId"] = "env-dev",
                ["Approval:OverdueCheck:Interval"] = "00:00:00",
            })
            .Build();
        var scheduler = new ApprovalOverdueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<ApprovalOverdueScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.LastCommand is not null || scheduler.ExecuteTask?.IsCompleted == true);

        Assert.Equal(new CheckOverdueApprovalStepsCommand("org-001", "env-dev"), sender.LastCommand);
        Assert.False(scheduler.ExecuteTask?.IsFaulted ?? false);
        await scheduler.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Bounded stability check for a negative assertion, failing on the first extra command rather than
    /// sleeping once and hoping the window was wide enough. The scheduler's periodic tick runs on the
    /// injected <see cref="FakeTimeProvider"/> that the test never advances, so a second command inside
    /// this window can only come from the deduplication itself.
    /// </summary>
    private static async Task AssertStaysAtAsync(Func<int> observe, int expected) =>
        await Consistently.StaysAsync(
            condition: $"the dispatched command count stays at {expected}",
            observe: _ => ValueTask.FromResult(observe()),
            isSatisfied: observed => observed == expected,
            describe: observed => $"commands={observed}; expected={expected}",
            options: new EventuallyOptions(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(10), []));

    private static async Task WaitUntilAsync(Func<bool> predicate) =>
        await Eventually.WaitAsync(
            condition: "the scheduler reaches the awaited observable state",
            observe: _ => ValueTask.FromResult(predicate()),
            isSatisfied: satisfied => satisfied,
            describe: satisfied => $"satisfied={satisfied}",
            options: new EventuallyOptions(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), []));

    private sealed class CapturingSender : ISender
    {
        public CheckOverdueApprovalStepsCommand? LastCommand { get; private set; }
        public List<CheckOverdueApprovalStepsCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastCommand = Assert.IsType<CheckOverdueApprovalStepsCommand>(request);
            Commands.Add(LastCommand);
            return Task.FromResult((TResponse)(object)1);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Only request/response commands are supported.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Only typed commands are supported.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported.");
        }
    }

    private sealed class ThrowingSender : ISender
    {
        public int Attempts { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            Attempts++;
            return Task.FromException<TResponse>(new TimeoutException("Transient database timeout."));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Only request/response commands are supported.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Only typed commands are supported.");
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported.");
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException("Streams are not supported.");
        }
    }
}
