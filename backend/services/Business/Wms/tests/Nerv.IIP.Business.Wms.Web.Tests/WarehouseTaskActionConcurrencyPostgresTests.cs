using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseWorkPoolAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Auth;
using Nerv.IIP.Business.Wms.Web.Application.Commands;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WarehouseTaskActionConcurrencyPostgresTests
{
    private const string PostgresConnectionStringEnvironmentVariable =
        "NERV_IIP_TEST_POSTGRES";

    [WmsWarehouseTaskActionPostgresFact]
    public async Task Concurrent_same_payload_replays_the_winning_receipt_on_postgres()
    {
        var adminConnectionString =
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_wms_action_receipt_same");
        var warehouseTaskId = await SeedTaskAsync(database.ConnectionString, started: false);
        var command = StartCommand(warehouseTaskId);

        await using var loser = CreateContext(database.ConnectionString);
        var loserHandler = new StartWarehouseTaskCommandHandler(
            loser,
            new WarehouseWorkScopeAuthorizer(loser, TimeProvider.System));
        var staged = await loserHandler.Handle(command, CancellationToken.None);
        loser.Entry(Assert.Single(loser.WarehouseTasks.Local)).State =
            EntityState.Unchanged;

        await using (var winner = CreateContext(database.ConnectionString))
        {
            await new StartWarehouseTaskCommandHandler(
                    winner,
                    new WarehouseWorkScopeAuthorizer(winner, TimeProvider.System))
                .Handle(command, CancellationToken.None);
            await winner.SaveChangesAsync();
        }

        var behavior =
            new WarehouseTaskActionReceiptRecoveryBehavior<
                StartWarehouseTaskCommand,
                WarehouseTaskActionResult>(loser);
        var attempts = 0;
        var result = await behavior.Handle(
            command,
            async cancellationToken =>
            {
                attempts++;
                var response = attempts == 1
                    ? staged
                    : await loserHandler.Handle(command, cancellationToken);
                await loser.SaveChangesAsync(cancellationToken);
                return response;
            },
            CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(staged.WarehouseTaskId, result.WarehouseTaskId);
        Assert.Equal(staged.TaskType, result.TaskType);
        Assert.Equal(staged.Status, result.Status);
        Assert.Equal(staged.Version, result.Version);
        Assert.Equal(staged.ExecutedQuantity, result.ExecutedQuantity);
        Assert.Equal(staged.DifferenceQuantity, result.DifferenceQuantity);
        Assert.Equal(staged.AllowedActions, result.AllowedActions);
        Assert.Equal(staged.BlockReasons, result.BlockReasons);
        await using var assertionContext = CreateContext(database.ConnectionString);
        Assert.Single(await assertionContext.WarehouseTaskActionReceipts.ToListAsync());
        Assert.Equal(
            WarehouseTaskStatus.InProgress,
            (await assertionContext.WarehouseTasks.SingleAsync()).Status);
    }

    [WmsWarehouseTaskActionPostgresFact]
    public async Task Concurrent_different_payload_becomes_idempotency_conflict_on_postgres()
    {
        var adminConnectionString =
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_wms_action_receipt_different");
        var warehouseTaskId = await SeedTaskAsync(database.ConnectionString, started: true);
        var losingCommand = ProgressCommand(warehouseTaskId, 6m);
        var winningCommand = ProgressCommand(warehouseTaskId, 5m);

        await using var loser = CreateContext(database.ConnectionString);
        var loserHandler = new RecordWarehouseTaskProgressActionCommandHandler(
            loser,
            new WarehouseWorkScopeAuthorizer(loser, TimeProvider.System));
        var staged = await loserHandler.Handle(losingCommand, CancellationToken.None);
        loser.Entry(Assert.Single(loser.WarehouseTasks.Local)).State =
            EntityState.Unchanged;

        await using (var winner = CreateContext(database.ConnectionString))
        {
            await new RecordWarehouseTaskProgressActionCommandHandler(
                    winner,
                    new WarehouseWorkScopeAuthorizer(winner, TimeProvider.System))
                .Handle(winningCommand, CancellationToken.None);
            await winner.SaveChangesAsync();
        }

        var behavior =
            new WarehouseTaskActionReceiptRecoveryBehavior<
                RecordWarehouseTaskProgressActionCommand,
                WarehouseTaskActionResult>(loser);
        var attempts = 0;
        await Assert.ThrowsAsync<WmsIdempotencyConflictException>(() => behavior.Handle(
            losingCommand,
            async cancellationToken =>
            {
                attempts++;
                var response = attempts == 1
                    ? staged
                    : await loserHandler.Handle(losingCommand, cancellationToken);
                await loser.SaveChangesAsync(cancellationToken);
                return response;
            },
            CancellationToken.None));

        Assert.Equal(2, attempts);
        await using var assertionContext = CreateContext(database.ConnectionString);
        var receipt = Assert.Single(
            await assertionContext.WarehouseTaskActionReceipts.ToListAsync());
        Assert.Equal(5m, receipt.ResultExecutedQuantity);
        Assert.Equal(
            5m,
            (await assertionContext.WarehouseTasks.SingleAsync()).ExecutedQuantity);
    }

    private static async Task<WarehouseTaskId> SeedTaskAsync(
        string connectionString,
        bool started)
    {
        await using var setup = CreateContext(connectionString);
        await setup.Database.MigrateAsync();
        var task = WarehouseTask.CreatePicking(
            "org-001",
            "env-dev",
            started ? "PICK-RACE-DIFFERENT" : "PICK-RACE-SAME",
            "OUT-001",
            "LINE-001",
            "SKU-001",
            "pcs",
            "SITE-01",
            "BIN-01",
            "PACK-01",
            10m,
            assignedOperatorUserId: "user-001",
            assignedPoolCode: "POOL-A");
        if (started)
        {
            task.Start("user-001", task.Version);
        }

        setup.WarehouseWorkPools.Add(WarehouseWorkPool.Create(
            "org-001",
            "env-dev",
            "POOL-A",
            "测试作业池",
            "SITE-01"));
        setup.WarehouseWorkPoolMemberships.Add(
            WarehouseWorkPoolMembership.Create(
                "org-001",
                "env-dev",
                "POOL-A",
                "user-001",
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow.AddDays(1)));
        setup.WarehouseTasks.Add(task);
        await setup.SaveChangesAsync();
        return task.Id;
    }

    private static StartWarehouseTaskCommand StartCommand(
        WarehouseTaskId warehouseTaskId) =>
        new(
            warehouseTaskId,
            "org-001",
            "env-dev",
            "user-001",
            "start-race-same",
            1,
            WarehouseTaskType.Picking,
            ["SITE-01"],
            "self",
            "user-001");

    private static RecordWarehouseTaskProgressActionCommand ProgressCommand(
        WarehouseTaskId warehouseTaskId,
        decimal executedQuantity) =>
        new(
            warehouseTaskId,
            "org-001",
            "env-dev",
            "user-001",
            "progress-race-different",
            2,
            executedQuantity,
            WarehouseTaskType.Picking,
            ["SITE-01"],
            "self",
            "user-001");

    private static ApplicationDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    WmsFacts.Schema))
            .Options;
        return new ApplicationDbContext(options, new NoopMediator());
    }
}

public sealed class WmsWarehouseTaskActionPostgresFactAttribute : FactAttribute
{
    public WmsWarehouseTaskActionPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip =
                "Set NERV_IIP_TEST_POSTGRES to run WMS warehouse-task action concurrency tests.";
        }
    }
}
