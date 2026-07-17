using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceCommandLockTests
{
    [Fact]
    public async Task Device_state_plan_creation_plan_update_and_pm_generation_share_org_environment_lock_key()
    {
        var generateSettings = await new GenerateDueMaintenanceWorkOrdersCommandLock().GetLockKeysAsync(
            new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"),
            CancellationToken.None);
        var stateSettings = await new ApplyMaintenanceDeviceStateCommandLock().GetLockKeysAsync(
            new ApplyMaintenanceDeviceStateCommand("org-001", "env-dev", "DEV-CNC-01", true, DateTimeOffset.UtcNow, "evt-device-001"),
            CancellationToken.None);
        var createSettings = await new CreateMaintenancePlanCommandLock().GetLockKeysAsync(
            new CreateMaintenancePlanCommand("org-001", "env-dev", "DEV-CNC-01", "PM-001", "P7D", new DateOnly(2026, 6, 1), "maintenance", null, null),
            CancellationToken.None);
        var updateSettings = await new UpdateMaintenancePlanCommandLock().GetLockKeysAsync(
            new UpdateMaintenancePlanCommand("org-001", "env-dev", new MaintenancePlanId(Guid.CreateVersion7()), "P30D", 500m),
            CancellationToken.None);

        Assert.Equal("business-maintenance:pm-generation:org-001:env-dev", generateSettings.LockKey);
        Assert.Equal(generateSettings.LockKey, stateSettings.LockKey);
        Assert.Equal(generateSettings.LockKey, createSettings.LockKey);
        Assert.Equal(generateSettings.LockKey, updateSettings.LockKey);
        Assert.Equal(TimeSpan.FromSeconds(30), generateSettings.AcquireTimeout);
        Assert.Equal(generateSettings.AcquireTimeout, stateSettings.AcquireTimeout);
        Assert.Equal(generateSettings.AcquireTimeout, createSettings.AcquireTimeout);
        Assert.Equal(generateSettings.AcquireTimeout, updateSettings.AcquireTimeout);
    }

    [Fact]
    public async Task Redis_distributed_lock_releases_after_normal_dispose_and_allows_retry()
    {
        var store = new InMemoryRedisCommandLockStore();
        var distributedLock = new RedisMaintenanceDistributedLock(store, TimeProvider.System);
        await using var first = await distributedLock.AcquireAsync("pm-lock", TimeSpan.FromSeconds(1), CancellationToken.None);

        var retryTask = distributedLock.TryAcquireAsync("pm-lock", TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        await Task.Delay(50);
        await first.DisposeAsync();
        await using var retry = await retryTask;

        Assert.NotNull(retry);
    }

    [Fact]
    public async Task Redis_distributed_lock_renews_lease_until_disposed()
    {
        var store = new InMemoryRedisCommandLockStore();
        var distributedLock = new RedisMaintenanceDistributedLock(
            store,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(20));
        await using var held = await distributedLock.AcquireAsync("pm-renewing-lock", TimeSpan.FromSeconds(1), CancellationToken.None);

        await Task.Delay(250);
        await using var blocked = await distributedLock.TryAcquireAsync("pm-renewing-lock", TimeSpan.Zero, CancellationToken.None);

        Assert.Null(blocked);
        Assert.False(held.HandleLostToken.IsCancellationRequested);
        await held.DisposeAsync();
        await using var retry = await distributedLock.TryAcquireAsync("pm-renewing-lock", TimeSpan.Zero, CancellationToken.None);
        Assert.NotNull(retry);
    }

    [Fact]
    public async Task Redis_distributed_lock_signals_handle_loss_when_renewal_fails()
    {
        var distributedLock = new RedisMaintenanceDistributedLock(
            new FailingRenewalStore(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(20));
        await using var held = await distributedLock.AcquireAsync("pm-lost-lock", TimeSpan.FromSeconds(1), CancellationToken.None);
        var lostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = held.HandleLostToken.Register(lostSignal.SetResult);

        await lostSignal.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(held.HandleLostToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Redis_distributed_lock_logs_lock_key_when_renewal_is_rejected()
    {
        var logger = new TestLogger<RedisMaintenanceDistributedLock>();
        var distributedLock = new RedisMaintenanceDistributedLock(
            new FailingRenewalStore(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(20),
            logger);
        await using var held = await distributedLock.AcquireAsync("pm-rejected-renewal", TimeSpan.FromSeconds(1), CancellationToken.None);
        var lostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = held.HandleLostToken.Register(lostSignal.SetResult);

        await lostSignal.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var warning = Assert.Single(logger.Messages, message => message.LogLevel == LogLevel.Warning);
        Assert.Contains("pm-rejected-renewal", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token", warning.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Redis_distributed_lock_logs_exception_when_renewal_throws()
    {
        var logger = new TestLogger<RedisMaintenanceDistributedLock>();
        var distributedLock = new RedisMaintenanceDistributedLock(
            new ThrowingRenewalStore(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(20),
            logger);
        await using var held = await distributedLock.AcquireAsync("pm-failed-renewal", TimeSpan.FromSeconds(1), CancellationToken.None);
        var lostSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = held.HandleLostToken.Register(lostSignal.SetResult);

        await lostSignal.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var warning = Assert.Single(logger.Messages, message => message.LogLevel == LogLevel.Warning);
        Assert.Contains("pm-failed-renewal", warning.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(warning.Exception);
    }

    [Fact]
    public async Task Command_lock_behavior_releases_after_handler_exception_and_allows_retry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRedisCommandLockStore, InMemoryRedisCommandLockStore>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDistributedLock, RedisMaintenanceDistributedLock>();
        services.AddScoped<ICommandLock<ThrowingLockedCommand>, ThrowingLockedCommandLock>();
        services.AddScoped<IRequestHandler<ThrowingLockedCommand>, ThrowingLockedCommandHandler>();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssemblyContaining<MaintenanceCommandLockTests>()
            .AddCommandLockBehavior());
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Send(new ThrowingLockedCommand("pm-lock"), CancellationToken.None));
        await using var retry = await provider.GetRequiredService<IDistributedLock>().TryAcquireAsync("pm-lock", TimeSpan.Zero, CancellationToken.None);

        Assert.NotNull(retry);
    }

    [Fact]
    public async Task Maintenance_command_lock_behavior_cancels_handler_when_lease_is_lost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDistributedLock>(new RedisMaintenanceDistributedLock(
            new FailingRenewalStore(),
            TimeProvider.System,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(20)));
        services.AddScoped<ICommandLock<CancellableLockedCommand>, CancellableLockedCommandLock>();
        services.AddScoped<IRequestHandler<CancellableLockedCommand>, CancellableLockedCommandHandler>();
        services.AddMediatR(configuration => configuration
            .RegisterServicesFromAssemblyContaining<MaintenanceCommandLockTests>()
            .AddOpenBehavior(typeof(MaintenanceCommandLockBehavior<,>)));
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<ISender>().Send(new CancellableLockedCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Generate_due_pm_handler_remains_idempotent_for_repeated_tick()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-WEEKLY", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(dbContext);

        var first = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var second = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"), CancellationToken.None);

        Assert.Equal(2, first.GeneratedCount);
        Assert.Equal(0, second.GeneratedCount);
        Assert.Equal(2, dbContext.MaintenanceWorkOrders.Count());
    }

    public sealed record ThrowingLockedCommand(string LockKey) : ICommand;

    public sealed record CancellableLockedCommand : ICommand;

    public sealed class CancellableLockedCommandLock : ICommandLock<CancellableLockedCommand>
    {
        public Task<CommandLockSettings> GetLockKeysAsync(CancellableLockedCommand command, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CommandLockSettings("pm-cancellable-lock", 1));
        }
    }

    public sealed class CancellableLockedCommandHandler : IRequestHandler<CancellableLockedCommand>
    {
        public async Task Handle(CancellableLockedCommand request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    public sealed class ThrowingLockedCommandLock : ICommandLock<ThrowingLockedCommand>
    {
        public Task<CommandLockSettings> GetLockKeysAsync(ThrowingLockedCommand command, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(new CommandLockSettings(command.LockKey, 1));
        }
    }

    public sealed class ThrowingLockedCommandHandler : IRequestHandler<ThrowingLockedCommand>
    {
        public Task Handle(ThrowingLockedCommand request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new InvalidOperationException("handler failed after command lock acquisition.");
        }
    }

    private sealed class FailingRenewalStore : IRedisCommandLockStore
    {
        public Task<bool> TryAcquireAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> RenewAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task ReleaseAsync(string key, string token, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRenewalStore : IRedisCommandLockStore
    {
        public Task<bool> TryAcquireAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> RenewAsync(string key, string token, TimeSpan leaseTime, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("redis renewal unavailable");
        }

        public Task ReleaseAsync(string key, string token, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogMessage> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(new LogMessage(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed record LogMessage(LogLevel LogLevel, string Message, Exception? Exception);
}
