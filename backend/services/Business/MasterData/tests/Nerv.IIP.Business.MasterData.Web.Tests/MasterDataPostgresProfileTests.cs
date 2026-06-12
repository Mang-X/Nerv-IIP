using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nerv.IIP.Business.MasterData.Domain;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;
using Nerv.IIP.Business.MasterData.Infrastructure;
using Nerv.IIP.Business.MasterData.Infrastructure.Repositories;
using Nerv.IIP.Business.MasterData.Web.Application.Commands.MasterData;
using Nerv.IIP.Business.MasterData.Web.Application.Queries;

namespace Nerv.IIP.Business.MasterData.Web.Tests;

public sealed class MasterDataPostgresProfileTests
{
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
            Assert.Equal(1, await db.Set<WorkCalendarWorkingTime>().CountAsync());
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
