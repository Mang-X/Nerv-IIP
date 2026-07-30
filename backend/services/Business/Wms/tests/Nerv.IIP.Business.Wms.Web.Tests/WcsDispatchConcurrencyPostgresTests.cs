using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nerv.IIP.Business.Wms.Domain;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WarehouseTaskAggregate;
using Nerv.IIP.Business.Wms.Domain.AggregatesModel.WcsTaskAggregate;
using Nerv.IIP.Business.Wms.Infrastructure;
using Nerv.IIP.Business.Wms.Web.Application.Errors;
using Nerv.IIP.Testing.PostgreSql;

namespace Nerv.IIP.Business.Wms.Web.Tests;

public sealed class WcsDispatchConcurrencyPostgresTests
{
    private const string PostgresConnectionStringEnvironmentVariable =
        "NERV_IIP_TEST_POSTGRES";

    [WmsWcsDispatchPostgresFact]
    public async Task Concurrent_wcs_claim_inserts_keep_one_owner_and_classify_the_loser()
    {
        var adminConnectionString =
            Environment.GetEnvironmentVariable(PostgresConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlTestDatabase.CreateAsync(
            adminConnectionString,
            "nerv_wms_wcs_claim");
        WarehouseTaskId warehouseTaskId;
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await setup.Database.MigrateAsync();
            var warehouseTask = WarehouseTask.CreatePicking(
                "org-001",
                "env-dev",
                "PICK-WCS-RACE-001",
                "OUT-001",
                "10",
                "SKU-001",
                "pcs",
                "SITE-001",
                "BIN-01",
                "PACK-01",
                5m,
                assignedPoolCode: "POOL-WAREHOUSE");
            setup.WarehouseTasks.Add(warehouseTask);
            await setup.SaveChangesAsync();
            warehouseTaskId = warehouseTask.Id;
        }

        await using var firstContext = CreateContext(database.ConnectionString);
        await using var secondContext = CreateContext(database.ConnectionString);
        firstContext.WcsTasks.Add(WcsTask.Dispatch(
            "org-001",
            "env-dev",
            warehouseTaskId,
            "wcs-a",
            "EXT-WCS-RACE-A",
            """{"task":"a"}"""));
        secondContext.WcsTasks.Add(WcsTask.Dispatch(
            "org-001",
            "env-dev",
            warehouseTaskId,
            "wcs-b",
            "EXT-WCS-RACE-B",
            """{"task":"b"}"""));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstSave = TrySaveAsync(firstContext, start.Task);
        var secondSave = TrySaveAsync(secondContext, start.Task);
        start.SetResult();
        var outcomes = await Task.WhenAll(firstSave, secondSave);

        Assert.Single(outcomes, outcome => outcome is null);
        var failure = Assert.Single(outcomes, outcome => outcome is not null);
        var dbUpdateException = Assert.IsType<DbUpdateException>(failure);
        Assert.True(WmsWcsDispatchPersistenceConflicts.IsTargetConflict(
            dbUpdateException,
            firstContext));
        var postgresException = FindPostgresException(dbUpdateException);
        Assert.NotNull(postgresException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
        Assert.Equal(
            firstContext.Model.FindEntityType(typeof(WcsTask))!
                .GetIndexes()
                .Single(index =>
                    index.IsUnique
                    && index.Properties.Select(property => property.Name).SequenceEqual(
                        [nameof(WcsTask.WarehouseTaskId)]))
                .GetDatabaseName(),
            postgresException.ConstraintName);

        await using var assertionContext = CreateContext(database.ConnectionString);
        Assert.Single(await assertionContext.WcsTasks
            .Where(task => task.WarehouseTaskId == warehouseTaskId)
            .ToListAsync());
    }

    private static async Task<Exception?> TrySaveAsync(
        ApplicationDbContext dbContext,
        Task start)
    {
        await start;
        try
        {
            await dbContext.SaveChangesAsync();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

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

public sealed class WmsWcsDispatchPostgresFactAttribute : FactAttribute
{
    public WmsWcsDispatchPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run WMS WCS dispatch concurrency tests.";
        }
    }
}
