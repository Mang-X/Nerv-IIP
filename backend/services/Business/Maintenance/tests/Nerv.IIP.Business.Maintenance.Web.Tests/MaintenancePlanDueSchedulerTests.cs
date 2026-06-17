using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Maintenance.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenancePlanDueSchedulerTests
{
    [Fact]
    public async Task Scheduler_keeps_running_when_one_generation_attempt_fails()
    {
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
                ["Maintenance:PmGeneration:Interval"] = "00:00:00.010",
            })
            .Build();
        var scheduler = new MaintenancePlanDueScheduler(
            services.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            NullLogger<MaintenancePlanDueScheduler>.Instance);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.Attempts > 0 || scheduler.ExecuteTask?.IsCompleted == true);

        Assert.True(sender.Attempts > 0);
        Assert.False(scheduler.ExecuteTask?.IsFaulted ?? false);
        await scheduler.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!predicate())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, timeout.Token);
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
