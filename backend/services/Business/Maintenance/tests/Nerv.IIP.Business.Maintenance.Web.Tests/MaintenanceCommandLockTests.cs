using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceCommandLockTests
{
    [Fact]
    public async Task Device_state_plan_creation_and_pm_generation_share_org_environment_lock_key()
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

        Assert.Equal("business-maintenance:pm-generation:org-001:env-dev", generateSettings.LockKey);
        Assert.Equal(generateSettings.LockKey, stateSettings.LockKey);
        Assert.Equal(generateSettings.LockKey, createSettings.LockKey);
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
}
