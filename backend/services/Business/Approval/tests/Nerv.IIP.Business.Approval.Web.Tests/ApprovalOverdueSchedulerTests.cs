using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nerv.IIP.Business.Approval.Web.Application.Commands.Chains;
using Nerv.IIP.Business.Approval.Web.Application.Scheduling;

namespace Nerv.IIP.Business.Approval.Web.Tests;

public sealed class ApprovalOverdueSchedulerTests
{
    [Fact]
    public async Task Scheduler_dispatches_configured_overdue_check_scope()
    {
        var sender = new CapturingSender();
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
            NullLogger<ApprovalOverdueScheduler>.Instance);

        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => sender.LastCommand is not null || scheduler.ExecuteTask?.IsCompleted == true);

        Assert.Equal(new CheckOverdueApprovalStepsCommand("org-001", "env-dev"), sender.LastCommand);
        await scheduler.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Scheduler_keeps_running_when_one_overdue_check_fails()
    {
        var sender = new ThrowingSender();
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
            NullLogger<ApprovalOverdueScheduler>.Instance);

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

    private sealed class CapturingSender : ISender
    {
        public CheckOverdueApprovalStepsCommand? LastCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            LastCommand = Assert.IsType<CheckOverdueApprovalStepsCommand>(request);
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
