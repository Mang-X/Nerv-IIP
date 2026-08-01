using System.Collections.Concurrent;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.DistributedLocking;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.DistributedLocks;
using NetCorePal.Extensions.Primitives;
using RedisMaintenanceDistributedLock = Nerv.IIP.DistributedLocking.RedisCommandDistributedLock;
using StackExchange.Redis;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceCommandLockTests
{
    [Fact]
    public void Production_configuration_fails_fast_when_redis_is_missing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddNervIipCommandLocking(
                configuration,
                new TestHostEnvironment(Environments.Production),
                isTesting: false,
                serviceName: "business-maintenance"));

        Assert.Contains("require a Redis connection string", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Testing_configuration_uses_an_in_memory_lock_without_redis()
    {
        var services = new ServiceCollection();
        services.AddNervIipCommandLocking(
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment("Testing"),
            isTesting: true,
            serviceName: "business-maintenance");
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDistributedLock>());
        Assert.Null(provider.GetService<IConnectionMultiplexer>());
    }

    [Fact]
    public void Existing_redis_connection_registration_is_reused()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            throw new InvalidOperationException("The registration should not be resolved by this test."));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "redis.invalid:6379",
            })
            .Build();

        services.AddNervIipCommandLocking(
            configuration,
            new TestHostEnvironment(Environments.Production),
            isTesting: false,
            serviceName: "business-maintenance");

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void Redis_key_prefix_isolated_by_service_name()
    {
        var maintenance = new StackExchangeRedisCommandLockStore(null!, "business-maintenance");
        var quality = new StackExchangeRedisCommandLockStore(null!, "business-quality");

        Assert.Equal(
            "nerv-iip:business-maintenance:locks:tenant-action",
            maintenance.ToRedisKeyForTesting("tenant-action"));
        Assert.Equal(
            "nerv-iip:business-quality:locks:tenant-action",
            quality.ToRedisKeyForTesting("tenant-action"));
    }

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
    public async Task Compatibility_complete_assignment_and_transition_share_one_work_order_lock_key()
    {
        var workOrderId = new MaintenanceWorkOrderId(Guid.CreateVersion7());
        var complete = await new CompleteMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new CompleteMaintenanceWorkOrderCommand(workOrderId, "fixed", "failure", 10, []),
            CancellationToken.None);
        var assign = await new AssignMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new AssignMaintenanceWorkOrderCommand(
                "org-001", "env-dev", workOrderId, "dispatcher-001", "tech-001", null,
                "on-duty", "assign-001", 0),
            CancellationToken.None);
        var transition = await new TransitionMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new TransitionMaintenanceWorkOrderCommand(
                "org-001", "env-dev", workOrderId, MaintenanceWorkOrderAction.Accept, "tech-001",
                "accepted", "accept-001", 0),
            CancellationToken.None);

        Assert.Contains(complete.LockKey!, assign.LockKeys!);
        Assert.Contains(complete.LockKey!, transition.LockKeys!);
    }

    [Fact]
    public async Task Assignment_and_transition_lock_the_aggregate_and_the_normalized_scope_idempotency_key()
    {
        var workOrderId = new MaintenanceWorkOrderId(Guid.CreateVersion7());
        var assign = await new AssignMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new AssignMaintenanceWorkOrderCommand(
                "org-001", "env-dev", workOrderId, "dispatcher-001", "tech-001", null,
                "on-duty", " shared-key ", 0),
            CancellationToken.None);
        var transition = await new TransitionMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new TransitionMaintenanceWorkOrderCommand(
                "org-001", "env-dev", workOrderId, MaintenanceWorkOrderAction.Accept, "tech-001",
                "accepted", "shared-key", 0),
            CancellationToken.None);
        var expectedKeys = new[]
        {
            $"business-maintenance:lifecycle-idempotency:org-001:env-dev:shared-key",
            $"business-maintenance:work-order:{workOrderId}",
        };

        Assert.Equal(expectedKeys, assign.LockKeys);
        Assert.Equal(expectedKeys, transition.LockKeys);
        Assert.Equal(TimeSpan.FromSeconds(30), assign.AcquireTimeout);
        Assert.Equal(assign.AcquireTimeout, transition.AcquireTimeout);
    }

    [Fact]
    public async Task Different_scope_or_idempotency_key_does_not_share_lifecycle_locks_for_different_work_orders()
    {
        var baseline = await LifecycleLockKeysAsync(
            "org-001", "env-dev", new MaintenanceWorkOrderId(Guid.CreateVersion7()), "shared-key");
        var differentScope = await LifecycleLockKeysAsync(
            "org-002", "env-dev", new MaintenanceWorkOrderId(Guid.CreateVersion7()), "shared-key");
        var differentKey = await LifecycleLockKeysAsync(
            "org-001", "env-dev", new MaintenanceWorkOrderId(Guid.CreateVersion7()), "other-key");

        Assert.Empty(baseline.Intersect(differentScope, StringComparer.Ordinal));
        Assert.Empty(baseline.Intersect(differentKey, StringComparer.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> LifecycleLockKeysAsync(
        string organizationId,
        string environmentId,
        MaintenanceWorkOrderId workOrderId,
        string idempotencyKey)
    {
        var settings = await new TransitionMaintenanceWorkOrderCommandLock().GetLockKeysAsync(
            new TransitionMaintenanceWorkOrderCommand(
                organizationId,
                environmentId,
                workOrderId,
                MaintenanceWorkOrderAction.Accept,
                "tech-001",
                "accepted",
                idempotencyKey,
                0),
            CancellationToken.None);
        return settings.LockKeys!;
    }

    [Fact]
    public async Task Production_service_provider_registers_assignment_and_transition_command_locks()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", "Testing");
                builder.UseSetting("IndustrialTelemetry:BaseUrl", "http://industrial-telemetry.local");
                builder.UseSetting("InternalService:BearerToken", "test-internal-token");
            });
        using var scope = factory.Services.CreateScope();

        Assert.IsType<AssignMaintenanceWorkOrderCommandLock>(
            scope.ServiceProvider.GetRequiredService<ICommandLock<AssignMaintenanceWorkOrderCommand>>());
        Assert.IsType<TransitionMaintenanceWorkOrderCommandLock>(
            scope.ServiceProvider.GetRequiredService<ICommandLock<TransitionMaintenanceWorkOrderCommand>>());
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
        // 时间边界要留给 CI 负载：租约 100ms + 续租 20ms 时，续租线程被饿一次超过 100ms
        // 租约就真过期，`blocked` 会拿到锁而断言失败（CI 上反复红，见 #1201）。
        // 契约是「续租能让持有超过一个租约周期」，用 1s 租约 / 100ms 续租 / 1.5s 观察，
        // 结论不变但对调度抖动有 10 倍余量。
        var distributedLock = new RedisMaintenanceDistributedLock(
            store,
            TimeProvider.System,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100));
        await using var held = await distributedLock.AcquireAsync("pm-renewing-lock", TimeSpan.FromSeconds(5), CancellationToken.None);

        await Task.Delay(1500);
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

        await lostSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 契约是「续租被拒时写出带锁键、且不泄漏 token 的告警」——**写几条不是契约**。
        // 续租每 20ms 触发一次，CI 负载下失败续租常在断言前跑到两次，
        // 原来的 Assert.Single 因此必然抖动（见 #1201）。改为：至少一条命中锁键，
        // 且所有告警都不含 token——这才是真正要守的两条。
        var warnings = logger.Messages.Where(message => message.LogLevel == LogLevel.Warning).ToArray();
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, message => message.Message.Contains("pm-rejected-renewal", StringComparison.Ordinal));
        Assert.All(warnings, message =>
            Assert.DoesNotContain("token", message.Message, StringComparison.OrdinalIgnoreCase));
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
    public async Task Command_lock_behavior_acquires_distinct_keys_in_ordinal_order_and_releases_in_reverse()
    {
        var distributedLock = new RecordingDistributedLock();
        var behavior = new NervIipCommandLockBehavior<MultiKeyLockedCommand, Unit>(
            [
                new MultiKeyLockedCommandLock(["z-key"]),
                new MultiKeyLockedCommandLock(["a-key"]),
                new MultiKeyLockedCommandLock(["z-key"]),
            ],
            distributedLock);

        await behavior.Handle(
            new MultiKeyLockedCommand(),
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                distributedLock.Events.Add("handler");
                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        Assert.Equal(
            ["acquire:a-key", "acquire:z-key", "handler", "release:z-key", "release:a-key"],
            distributedLock.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_lock_behavior_releases_partially_acquired_keys_when_later_acquisition_fails(
        bool cancellation)
    {
        var distributedLock = new RecordingDistributedLock("z-key", cancellation);
        var behavior = new NervIipCommandLockBehavior<MultiKeyLockedCommand, Unit>(
            [new MultiKeyLockedCommandLock(["z-key"]), new MultiKeyLockedCommandLock(["a-key"])],
            distributedLock);

        var exception = await Record.ExceptionAsync(() => behavior.Handle(
            new MultiKeyLockedCommand(),
            _ => Task.FromResult(Unit.Value),
            CancellationToken.None));

        if (cancellation)
        {
            Assert.IsAssignableFrom<OperationCanceledException>(exception);
        }
        else
        {
            Assert.IsType<InvalidOperationException>(exception);
        }
        Assert.Equal(["acquire:a-key", "acquire:z-key", "release:a-key"], distributedLock.Events);
    }

    [Fact]
    public async Task Command_lock_behavior_attempts_every_reverse_release_and_stops_all_renewals_when_releases_fail()
    {
        var store = new ReleaseFailingStore(["a-key", "z-key"]);
        var distributedLock = new RedisMaintenanceDistributedLock(
            store,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(20));
        var behavior = new NervIipCommandLockBehavior<MultiKeyLockedCommand, Unit>(
            [new MultiKeyLockedCommandLock(["z-key", "a-key"])],
            distributedLock);

        try
        {
            var exception = await Record.ExceptionAsync(() => behavior.Handle(
                new MultiKeyLockedCommand(),
                async _ =>
                {
                    await store.AllKeysRenewed.WaitAsync(TimeSpan.FromSeconds(2));
                    return Unit.Value;
                },
                CancellationToken.None));
            var aggregate = Assert.IsType<AggregateException>(exception);
            Assert.Equal(
                ["release failed: z-key", "release failed: a-key"],
                aggregate.InnerExceptions.Select(error => error.Message));
            Assert.Equal(["release:z-key", "release:a-key"], store.ReleaseEvents);

            var renewalCountsAfterRelease = store.RenewalCounts;
            await Task.Delay(100);
            Assert.Equal(renewalCountsAfterRelease, store.RenewalCounts);
        }
        finally
        {
            store.RejectRenewals();
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Command_lock_behavior_aggregates_handler_then_release_failures_without_masking_either()
    {
        var store = new ReleaseFailingStore(["z-key"]);
        var distributedLock = new RedisMaintenanceDistributedLock(
            store,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(20));
        var behavior = new NervIipCommandLockBehavior<MultiKeyLockedCommand, Unit>(
            [new MultiKeyLockedCommandLock(["z-key", "a-key"])],
            distributedLock);

        try
        {
            var exception = await Record.ExceptionAsync(() => behavior.Handle(
                new MultiKeyLockedCommand(),
                async _ =>
                {
                    await store.AllKeysRenewed.WaitAsync(TimeSpan.FromSeconds(2));
                    throw new ApplicationException("handler failed");
                },
                CancellationToken.None));
            var aggregate = Assert.IsType<AggregateException>(exception);
            Assert.Collection(
                aggregate.InnerExceptions,
                error => Assert.IsType<ApplicationException>(error),
                error => Assert.Equal("release failed: z-key", Assert.IsType<InvalidOperationException>(error).Message));
            Assert.Equal(["release:z-key", "release:a-key"], store.ReleaseEvents);
        }
        finally
        {
            store.RejectRenewals();
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Command_lock_behavior_keeps_the_single_key_command_contract()
    {
        var distributedLock = new RecordingDistributedLock();
        var behavior = new NervIipCommandLockBehavior<MultiKeyLockedCommand, Unit>(
            [new MultiKeyLockedCommandLock(["only-key"])],
            distributedLock);

        await behavior.Handle(
            new MultiKeyLockedCommand(),
            _ =>
            {
                distributedLock.Events.Add("handler");
                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        Assert.Equal(["acquire:only-key", "handler", "release:only-key"], distributedLock.Events);
    }

    [Fact]
    public async Task Command_lock_behavior_preserves_a_single_release_exception_type()
    {
        var distributedLock = new RecordingDistributedLock(failReleaseOnKey: "only-key");
        var behavior = new NervIipCommandLockBehavior<MultiKeyLockedCommand, Unit>(
            [new MultiKeyLockedCommandLock(["only-key"])],
            distributedLock);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new MultiKeyLockedCommand(),
            _ => Task.FromResult(Unit.Value),
            CancellationToken.None));

        Assert.Equal("synthetic lock release failure", exception.Message);
        Assert.Equal(["acquire:only-key", "release:only-key"], distributedLock.Events);
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
            .AddOpenBehavior(typeof(NervIipCommandLockBehavior<,>)));
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

    public sealed record MultiKeyLockedCommand : ICommand;

    public sealed class MultiKeyLockedCommandLock(IReadOnlyCollection<string> keys)
        : ICommandLock<MultiKeyLockedCommand>
    {
        public Task<CommandLockSettings> GetLockKeysAsync(
            MultiKeyLockedCommand command,
            CancellationToken cancellationToken)
        {
            _ = command;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CommandLockSettings(keys, 30));
        }
    }

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

    private sealed class ReleaseFailingStore(IReadOnlyCollection<string> failingKeys) : IRedisCommandLockStore
    {
        private readonly ConcurrentDictionary<string, int> renewalCounts = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> releaseEvents = new();
        private readonly HashSet<string> failingKeys = new(failingKeys, StringComparer.Ordinal);
        private readonly TaskCompletionSource allKeysRenewed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool rejectRenewals;

        public Task AllKeysRenewed => allKeysRenewed.Task;

        public IReadOnlyCollection<string> ReleaseEvents => releaseEvents.ToArray();

        public IReadOnlyDictionary<string, int> RenewalCounts =>
            renewalCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        public Task<bool> TryAcquireAsync(
            string key,
            string token,
            TimeSpan leaseTime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<bool> RenewAsync(
            string key,
            string token,
            TimeSpan leaseTime,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            renewalCounts.AddOrUpdate(key, 1, static (_, count) => count + 1);
            if (renewalCounts.ContainsKey("a-key") && renewalCounts.ContainsKey("z-key"))
            {
                allKeysRenewed.TrySetResult();
            }
            return Task.FromResult(!rejectRenewals);
        }

        public Task ReleaseAsync(string key, string token, CancellationToken cancellationToken)
        {
            releaseEvents.Enqueue($"release:{key}");
            if (failingKeys.Contains(key))
            {
                throw new InvalidOperationException($"release failed: {key}");
            }
            return Task.CompletedTask;
        }

        public void RejectRenewals() => rejectRenewals = true;
    }

    private sealed class RecordingDistributedLock(
        string? failOnKey = null,
        bool cancelOnFailure = false,
        string? failReleaseOnKey = null) : IDistributedLock
    {
        public List<string> Events { get; } = [];

        public ILockSynchronizationHandler? TryAcquire(
            string key,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            TryAcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

        public ILockSynchronizationHandler Acquire(
            string key,
            TimeSpan? timeout,
            CancellationToken cancellationToken) =>
            AcquireAsync(key, timeout, cancellationToken).AsTask().GetAwaiter().GetResult();

        public ValueTask<ILockSynchronizationHandler?> TryAcquireAsync(
            string key,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"acquire:{key}");
            if (string.Equals(key, failOnKey, StringComparison.Ordinal))
            {
                if (cancelOnFailure)
                {
                    throw new OperationCanceledException("synthetic lock cancellation", cancellationToken);
                }
                throw new InvalidOperationException("synthetic lock acquisition failure");
            }
            return ValueTask.FromResult<ILockSynchronizationHandler?>(
                new RecordingHandle(key, Events, failReleaseOnKey));
        }

        public async ValueTask<ILockSynchronizationHandler> AcquireAsync(
            string key,
            TimeSpan? timeout,
            CancellationToken cancellationToken) =>
            await TryAcquireAsync(key, timeout ?? TimeSpan.FromSeconds(30), cancellationToken)
                ?? throw new TimeoutException($"Could not acquire {key}.");

        private sealed class RecordingHandle(
            string key,
            List<string> events,
            string? failReleaseOnKey) : ILockSynchronizationHandler
        {
            public CancellationToken HandleLostToken => CancellationToken.None;

            public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

            public ValueTask DisposeAsync()
            {
                events.Add($"release:{key}");
                if (string.Equals(key, failReleaseOnKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("synthetic lock release failure");
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Nerv.IIP.DistributedLocking.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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
