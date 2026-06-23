using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenancePlanAggregate;
using Nerv.IIP.Business.Maintenance.Domain.AggregatesModel.MaintenanceWorkOrderAggregate;
using Nerv.IIP.Business.Maintenance.Web.Application.Commands;
using Nerv.IIP.Business.Maintenance.Web.Infrastructure;

namespace Nerv.IIP.Business.Maintenance.Web.Tests;

public sealed class MaintenanceCommandLockTests
{
    [Fact]
    public async Task Generate_due_pm_command_declares_org_env_business_date_lock_key()
    {
        var command = new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm");
        var commandLock = new GenerateDueMaintenanceWorkOrdersCommandLock();

        var settings = await commandLock.GetLockKeysAsync(command, CancellationToken.None);

        Assert.Equal("business-maintenance:pm-generation:org-001:env-dev:20260608", settings.LockKey);
    }

    [Fact]
    public async Task Redis_distributed_lock_releases_after_exception_and_allows_retry()
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
    public async Task Generate_due_pm_handler_remains_idempotent_for_repeated_tick()
    {
        await using var dbContext = MaintenanceEndpointContractTests.CreateTestDbContext();
        dbContext.MaintenancePlans.Add(MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-WEEKLY", "P7D", new DateOnly(2026, 6, 1), "maintenance"));
        await dbContext.SaveChangesAsync();
        var handler = new GenerateDueMaintenanceWorkOrdersCommandHandler(dbContext);

        var first = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        var second = await handler.Handle(new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"), CancellationToken.None);

        Assert.Equal(1, first.GeneratedCount);
        Assert.Equal(0, second.GeneratedCount);
        Assert.Single(dbContext.MaintenanceWorkOrders);
    }
}
