using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Quality.Web.Application.Commands.InspectionTasks;
using Nerv.IIP.Business.Quality.Web.Application.Scheduling;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Quality.Web.Tests;

public sealed class PeriodicInspectionTimeTaskSchedulerTests
{
    [Fact]
    public async Task Enabled_scheduler_dispatches_configured_scope_on_fake_time_ticks()
    {
        var sender = new CapturingSender();
        var clock = new TimerRegistrationObservingTimeProvider();
        await using var services = new ServiceCollection().AddSingleton<ISender>(sender).BuildServiceProvider();
        var configuration = Configuration(enabled: true);
        var scheduler = new PeriodicInspectionTimeTaskScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<PeriodicInspectionTimeTaskScheduler>.Instance,
            clock);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitForCountAsync(sender, 1);
        await clock.WaitForTimerCountAsync(1);
        clock.Advance(TimeSpan.FromHours(1));
        await WaitForCountAsync(sender, 2);
        await scheduler.StopAsync(CancellationToken.None);

        Assert.Equal(
            [
                new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 100),
                new GeneratePeriodicInspectionTimeTasksCommand("org-001", "env-dev", 24, 100),
            ],
            sender.Commands);
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

        Assert.Empty(sender.Commands);
    }

    private static IConfiguration Configuration(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Quality:PeriodicInspectionTime:Enabled"] = enabled.ToString(),
            ["Quality:PeriodicInspectionTime:OrganizationId"] = "org-001",
            ["Quality:PeriodicInspectionTime:EnvironmentId"] = "env-dev",
            ["Quality:PeriodicInspectionTime:Interval"] = "01:00:00",
            ["Quality:PeriodicInspectionTime:MaxWindowsPerContext"] = "24",
            ["Quality:PeriodicInspectionTime:ContextBatchSize"] = "100",
        })
        .Build();

    private static ValueTask<int> WaitForCountAsync(CapturingSender sender, int expected) =>
        Eventually.WaitAsync(
            $"periodic inspection scheduler dispatches {expected} commands",
            _ => ValueTask.FromResult(sender.Commands.Count),
            count => count >= expected,
            count => $"commands={count}; expected>={expected}",
            new EventuallyOptions(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(10), []));

    private sealed class CapturingSender : ISender
    {
        public List<GeneratePeriodicInspectionTimeTasksCommand> Commands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            Commands.Add(Assert.IsType<GeneratePeriodicInspectionTimeTasksCommand>(request));
            return Task.FromResult((TResponse)(object)0);
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
}
