using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.LifecycleAuditAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;
using Nerv.IIP.Business.MasterData.Web.Application.IntegrationEventConverters;
using Nerv.IIP.Messaging.CAP;
using NetCorePal.Extensions.DistributedTransactions.CAP;
using NetCorePal.Extensions.DistributedTransactions;
using NetCorePal.Extensions.DependencyInjection;
using NetCorePal.Extensions.Primitives;
using Savorboard.CAP.InMemoryMessageQueue;
using DotNetCore.CAP.Persistence;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataPostgresProfileTests
{
    [PostgresFact]
    public async Task Postgres_device_reference_batch_uses_two_fixed_relational_reads_for_one_and_two_hundred_references()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var counter = new RelationalReadCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(counter)
            .Options;
        await using var dbContext = new ApplicationDbContext(options, new NoopMediator());
        AssertUsesGovernedDatabase(dbContext);
        await DropMasterDataSchemaAsync(dbContext);
        await dbContext.Database.MigrateAsync();
        var devices = Enumerable.Range(1, 200)
            .Select(index => DeviceAsset.Register(
                "org-001",
                "env-dev",
                $"DEV-{index:000}",
                $"Device {index:000}",
                "LINE-A",
                "WC-A"))
            .ToArray();
        dbContext.DeviceAssets.AddRange(devices);
        await dbContext.SaveChangesAsync();
        var handler = new ResolveMasterDataReferencesQueryHandler(dbContext);

        counter.Reset();
        await handler.Handle(
            new ResolveMasterDataReferencesQuery(
                "org-001",
                "env-dev",
                [new MasterDataReferenceRequest("device-asset", devices[0].Id.ToString())]),
            CancellationToken.None);
        var oneReferenceReads = counter.ReadCount;

        counter.Reset();
        await handler.Handle(
            new ResolveMasterDataReferencesQuery(
                "org-001",
                "env-dev",
                devices.Select(device => new MasterDataReferenceRequest("device-asset", device.Id.ToString())).ToArray()),
            CancellationToken.None);

        Assert.Equal(2, oneReferenceReads);
        Assert.Equal(oneReferenceReads, counter.ReadCount);
    }

    [PostgresFact]
    public async Task Postgres_disable_endpoint_transaction_fact_persists_audit_and_cap_outbox_with_operation_identity()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = CreateCapServices(connectionString);
        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AssertUsesGovernedDatabase(db);
            await DropMasterDataSchemaAsync(db);
            await db.Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<IStorageInitializer>()
                .InitializeAsync(CancellationToken.None);
            db.Skus.Add(Sku.Create("org-001", "env-dev", "SKU-AUDIT", "Audited SKU", "pcs", "finished-goods"));
            await db.SaveChangesAsync();

            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new SetMasterDataResourceEnabledCommand(
                "org-001", "env-dev", "sku", "SKU-AUDIT", false,
                "user:postgres-auditor", "disable-op-001", Reason: "obsolete"), CancellationToken.None);
        }

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await observer.LifecycleAuditEntries.AsNoTracking().SingleAsync();
        Assert.Equal("disable-op-001", audit.OperationId);
        Assert.Equal("user:postgres-auditor", audit.ActorId);
        var outbox = await ReadCapPublishedRowsAsync(observer);
        Assert.Contains(outbox, content => content.Contains("masterdata:sku-disabled:org-001:env-dev:SKU-AUDIT:disable-op-001", StringComparison.Ordinal));
    }

    [PostgresFact]
    public async Task Postgres_cap_concurrent_operation_recovers_loser_and_persists_exactly_one_audit_and_outbox()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;
        var services = CreateCapServices(connectionString, new LifecycleAuditInsertBarrier("K-RACE-PG"));
        await using var provider = services.BuildServiceProvider();
        using (var seedScope = provider.CreateScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            AssertUsesGovernedDatabase(seed);
            await DropMasterDataSchemaAsync(seed);
            await seed.Database.MigrateAsync();
            await seedScope.ServiceProvider.GetRequiredService<IStorageInitializer>()
                .InitializeAsync(CancellationToken.None);
            seed.Skus.AddRange(
                Sku.Create("org", "env", "SKU-RACE-PG", "Race", "pcs", "finished-goods"),
                Sku.Create("org", "env", "SKU-COLLISION-PG", "Collision", "pcs", "finished-goods"));
            await seed.SaveChangesAsync();
        }

        var race = new SetMasterDataResourceEnabledCommand("org", "env", "sku", "SKU-RACE-PG", false, "user:pg", "K-RACE-PG", Reason: "obsolete");
        await Task.WhenAll(
            SendLifecycleCommandAsync(provider, race),
            SendLifecycleCommandAsync(provider, race));

        var collision = race with { Code = "SKU-COLLISION-PG", OperationId = "K-COLLISION-PG", Reason = "winner reason" };
        await SendLifecycleCommandAsync(provider, collision);
        await Assert.ThrowsAsync<KnownException>(() =>
            SendLifecycleCommandAsync(provider, collision with { Reason = "loser reason" }));

        using var observerScope = provider.CreateScope();
        var observer = observerScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True((await observer.Skus.AsNoTracking().SingleAsync(x => x.Code == "SKU-RACE-PG")).Disabled);
        Assert.True((await observer.Skus.AsNoTracking().SingleAsync(x => x.Code == "SKU-COLLISION-PG")).Disabled);
        var raceAudit = await observer.LifecycleAuditEntries.SingleAsync(x => x.OperationId == "K-RACE-PG");
        var collisionAudit = await observer.LifecycleAuditEntries.SingleAsync(x => x.OperationId == "K-COLLISION-PG");
        Assert.Equal("obsolete", raceAudit.Reason);
        Assert.Equal("winner reason", collisionAudit.Reason);
        Assert.NotEqual("loser reason", collisionAudit.Reason);
        var outbox = await ReadCapPublishedRowsAsync(observer);
        Assert.Single(outbox, content => content.Contains("masterdata:sku-disabled:org:env:SKU-RACE-PG:K-RACE-PG", StringComparison.Ordinal));
        Assert.Single(outbox, content => content.Contains("masterdata:sku-disabled:org:env:SKU-COLLISION-PG:K-COLLISION-PG", StringComparison.Ordinal));
        Assert.DoesNotContain(outbox, content => content.Contains("loser reason", StringComparison.Ordinal));
    }

    private static ServiceCollection CreateCapServices(
        string connectionString,
        SaveChangesInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddHttpContextAccessor();
        services.AddScoped<IMasterDataIntegrationEventContextAccessor, HttpMasterDataIntegrationEventContextAccessor>();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
            configuration.AddUnitOfWorkBehaviors();
        });
        services.AddMasterDataPostgreSqlPersistence(connectionString);
        if (interceptor is not null)
        {
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor));
        }
        services.AddScoped<ICapTransactionFactory, NetCorePalCapTransactionFactory>();
        services.AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(builder => builder.RegisterServicesFromAssemblies(typeof(Program)));
        services.AddCap(options =>
        {
            options.UseEntityFramework<ApplicationDbContext>();
            options.UseInMemoryMessageQueue();
        });
        return services;
    }

    private static async Task SendLifecycleCommandAsync(
        ServiceProvider provider,
        SetMasterDataResourceEnabledCommand command)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command);
    }

    private sealed class LifecycleAuditInsertBarrier(string operationId) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var hasTargetAudit = eventData.Context!.ChangeTracker.Entries<MasterDataLifecycleAuditEntry>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.OperationId == operationId);
            if (!hasTargetAudit)
            {
                return result;
            }

            if (Interlocked.Increment(ref arrivals) == 2)
            {
                release.TrySetResult();
            }

            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    [PostgresFact]
    public async Task Postgres_store_persists_master_data_aggregates()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddMasterDataPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropMasterDataSchemaAsync(db);
            await db.Database.MigrateAsync();
            await AssertMigrationsHistoryTableInSchemaAsync(db, MasterDataFacts.Schema);

            var sku = Sku.Create("org-001", "env-dev", "SKU-001", "Steel Coil", "kg", "raw-material");
            var partner = BusinessPartner.Create("org-001", "env-dev", "SUP-001", "supplier", "North Supplier");
            var department = Department.Create("org-001", "env-dev", "DPT-001", "Production", null);
            var team = Team.Create("org-001", "env-dev", "TEAM-001", "Line Team", "DPT-001", "SHIFT-A");
            var skill = PersonnelSkill.Assign("org-001", "env-dev", "user-001", "WELD", "senior", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
            var workCenter = WorkCenter.Create("org-001", "env-dev", "WC-001", "Welding Cell", 480);
            var calendar = WorkCalendar.Create("org-001", "env-dev", "CAL-001", "Default Calendar");
            calendar.AddWorkingDay(DayOfWeek.Monday);
            var device = DeviceAsset.Register("org-001", "env-dev", "DEV-001", "Robot X", "LINE-001", "WC-001");

            db.AddRange(sku, partner, department, team, skill, workCenter, calendar, device);
            await db.SaveChangesAsync();

            Assert.NotNull(sku.Id);
            Assert.NotEqual(Guid.Empty, sku.Id.Id);
            Assert.NotNull(calendar.Id);
            Assert.NotEqual(Guid.Empty, calendar.Id.Id);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.Set<Sku>().CountAsync());
            Assert.Equal(1, await db.Set<WorkCalendar>().CountAsync());
            Assert.Equal(1, await CountRowsAsync(db, "work_calendar_working_times"));
        }
    }

    [PostgresFact]
    public async Task Postgres_work_calendar_update_replaces_owned_details_after_reload()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(Program).Assembly);
        });
        services.AddMasterDataPostgreSqlPersistence(connectionString);

        await using var provider = services.BuildServiceProvider();

        using (var seedScope = provider.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DropMasterDataSchemaAsync(db);
            await db.Database.MigrateAsync();

            var calendar = WorkCalendar.Create("org-001", "env-dev", "CAL-001", "Default Calendar");
            calendar.AddWorkingDay(DayOfWeek.Monday);
            db.WorkCalendars.Add(calendar);
            await db.SaveChangesAsync();
        }

        using (var firstUpdateScope = provider.CreateScope())
        {
            var db = firstUpdateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handler = new UpdateMasterDataResourceCommandHandler(db, new ReferenceDataCodeRepository(db));
            await handler.Handle(
                new UpdateMasterDataResourceCommand(
                    "org-001",
                    "env-dev",
                    "work-calendar",
                    "CAL-001",
                    WorkingTimes:
                    [
                        new WorkCalendarWorkingTimeDetail(DayOfWeek.Monday),
                        new WorkCalendarWorkingTimeDetail(DayOfWeek.Tuesday)
                    ],
                    Holidays: [new WorkCalendarHolidayDetail(new DateOnly(2026, 5, 1), "Labor Day")],
                    Exceptions: [new WorkCalendarExceptionDetail(new DateOnly(2026, 5, 2), true, new TimeOnly(9, 0), new TimeOnly(15, 0), "Make-up shift")]),
                CancellationToken.None);
            await db.SaveChangesAsync();
        }

        using (var secondUpdateScope = provider.CreateScope())
        {
            var db = secondUpdateScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var handler = new UpdateMasterDataResourceCommandHandler(db, new ReferenceDataCodeRepository(db));
            await handler.Handle(
                new UpdateMasterDataResourceCommand(
                    "org-001",
                    "env-dev",
                    "work-calendar",
                    "CAL-001",
                    WorkingTimes: [new WorkCalendarWorkingTimeDetail(DayOfWeek.Wednesday)],
                    Holidays: [new WorkCalendarHolidayDetail(new DateOnly(2026, 10, 1), "National Day")],
                    Exceptions: [new WorkCalendarExceptionDetail(new DateOnly(2026, 10, 2), false, null, null, "Shutdown")]),
                CancellationToken.None);
            await db.SaveChangesAsync();
        }

        using (var verifyScope = provider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await CountRowsAsync(db, "work_calendar_working_times"));
            Assert.Equal(1, await CountRowsAsync(db, "work_calendar_holidays"));
            Assert.Equal(1, await CountRowsAsync(db, "work_calendar_exceptions"));

            var calendar = await db.WorkCalendars.SingleAsync(x => x.OrganizationId == "org-001" && x.EnvironmentId == "env-dev" && x.Code == "CAL-001");
            Assert.Equal(DayOfWeek.Wednesday, Assert.Single(calendar.WorkingTimes).DayOfWeek);
            Assert.Equal(new DateOnly(2026, 10, 1), Assert.Single(calendar.Holidays).Date);
            Assert.Equal(new DateOnly(2026, 10, 2), Assert.Single(calendar.Exceptions).Date);
        }
    }

    private static async Task<long> CountRowsAsync(ApplicationDbContext db, string table)
    {
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(MasterDataFacts.Schema);
        var quotedTable = new NpgsqlCommandBuilder().QuoteIdentifier(table);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {quotedSchema}.{quotedTable}";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<string[]> ReadCapPublishedRowsAsync(ApplicationDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT row_to_json(p)::text FROM cap.published AS p";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return [.. rows];
    }

    private static async Task DropMasterDataSchemaAsync(ApplicationDbContext db)
    {
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(MasterDataFacts.Schema);
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertMigrationsHistoryTableInSchemaAsync(ApplicationDbContext db, string schema)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name = '__EFMigrationsHistory'
            )
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "schema";
        parameter.Value = schema;
        command.Parameters.Add(parameter);

        var exists = (bool?)await command.ExecuteScalarAsync() ?? false;
        Assert.True(exists, $"Expected EF migrations history table in schema '{schema}'.");
    }

    private static void AssertUsesGovernedDatabase(ApplicationDbContext db)
    {
        var governedConnection = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")!);
        Assert.Equal(governedConnection.Database, db.Database.GetDbConnection().Database);
    }

    private sealed class RelationalReadCounter : DbCommandInterceptor
    {
        public int ReadCount { get; private set; }

        public void Reset() => ReadCount = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReadCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class NoopMediator : IMediator
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES")))
        {
            Skip = "Set NERV_IIP_TEST_POSTGRES to run PostgreSQL profile tests.";
        }
    }
}
