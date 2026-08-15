using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;
using Nerv.IIP.Testing;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenancePlanDueSchedulerTests
{
    [Fact]
    public async Task Scheduler_keeps_running_when_one_generation_attempt_fails()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sender = new ThrowingSender();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maintenance:PmGeneration:Enabled"] = "true",
                ["Maintenance:PmGeneration:OrganizationId"] = "org-001",
                ["Maintenance:PmGeneration:EnvironmentId"] = "env-dev",
                ["Maintenance:PmGeneration:Interval"] = "01:00:00",
            })
            .Build();
        var scheduler = new MaintenancePlanDueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<MaintenancePlanDueScheduler>.Instance,
            fakeTime);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitForAttemptsAsync(sender, 1);
        fakeTime.Advance(TimeSpan.FromHours(1));
        await WaitForAttemptsAsync(sender, 2);

        Assert.Equal(2, sender.Attempts);
        Assert.False(scheduler.ExecuteTask?.IsFaulted ?? false);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_uses_configured_time_zone_for_pm_business_date()
    {
        var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 16, 30, 0, TimeSpan.Zero));
        var sender = new CapturingSender();
        await using var services = new ServiceCollection()
            .AddSingleton<ISender>(sender)
            .BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maintenance:PmGeneration:Enabled"] = "true",
                ["Maintenance:PmGeneration:OrganizationId"] = "org-001",
                ["Maintenance:PmGeneration:EnvironmentId"] = "env-dev",
                ["Maintenance:PmGeneration:Interval"] = "01:00:00",
                ["Maintenance:PmGeneration:TimeZoneId"] = "Asia/Shanghai",
            })
            .Build();
        var scheduler = new MaintenancePlanDueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<MaintenancePlanDueScheduler>.Instance,
            fakeTime);

        await scheduler.StartAsync(CancellationToken.None);
        await Eventually.WaitAsync(
            "scheduler captures its first PM generation command",
            _ => ValueTask.FromResult(sender.LastCommand),
            command => command is not null,
            command => command is null ? "command=none" : $"businessDate={command.BusinessDate}",
            EventuallyOptions());

        Assert.Equal(new DateOnly(2026, 6, 2), sender.LastCommand?.BusinessDate);
        await scheduler.StopAsync(CancellationToken.None);
    }

    private static ValueTask<int> WaitForAttemptsAsync(ThrowingSender sender, int expectedAttempts) =>
        Eventually.WaitAsync(
            $"scheduler reaches {expectedAttempts} PM generation attempts",
            _ => ValueTask.FromResult(sender.Attempts),
            attempts => attempts >= expectedAttempts,
            attempts => $"attempts={attempts}",
            EventuallyOptions());

    private static EventuallyOptions EventuallyOptions() => new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromMilliseconds(10),
        []);

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

    private sealed class CapturingSender : ISender
    {
        public GenerateDueMaintenanceWorkOrdersCommand? LastCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastCommand = Assert.IsType<GenerateDueMaintenanceWorkOrdersCommand>(request);
            var result = new GenerateDueMaintenanceWorkOrdersResult(0, Array.Empty<MaintenanceWorkOrderId>());
            return Task.FromResult((TResponse)(object)result);
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
