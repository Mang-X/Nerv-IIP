using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Persistence;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.OperationTaskAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class OperationActualTimeSettlementPostgresTests
{
    [MesRealPostgresFact]
    public async Task Completion_state_and_settlement_outbox_are_committed_together_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndInitializeCapAsync(factory);
        await SeedRunningTaskAsync(factory);

        using var commandScope = factory.Services.CreateScope();
        var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
        var staged = await sender.Send(new RecordProductionReportCommand(
            "org-001", "env-dev", "WO-001", "OP-001", 4m, 0m, false,
            At(20), "report-stage-postgres-001"));
        var completing = await sender.Send(new RecordProductionReportCommand(
            "org-001", "env-dev", "WO-001", "OP-001", 6m, 0m, true,
            At(60), "report-complete-postgres-001"));

        using var assertionScope = factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = await dbContext.OperationTasks.AsNoTracking().SingleAsync();
        var settlement = await dbContext.OperationActualTimeSettlements
            .AsNoTracking()
            .Include(x => x.CoveredReports)
            .SingleAsync();
        var settlementOutbox = Assert.Single(
            await ReadCapOutboxContentAsync(),
            content => content.Contains("mes.OperationActualTimeSettled", StringComparison.Ordinal));

        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Equal(
            new[] { staged.ReportNo, completing.ReportNo }.Order(StringComparer.Ordinal),
            settlement.CoveredReports.Select(x => x.ReportNo).Order(StringComparer.Ordinal));
        Assert.Null(settlement.VoidedAtUtc);
        Assert.Contains("\"SettlementRevision\":1", settlementOutbox, StringComparison.Ordinal);
        Assert.Contains(staged.ReportNo, settlementOutbox, StringComparison.Ordinal);
        Assert.Contains(completing.ReportNo, settlementOutbox, StringComparison.Ordinal);
    }

    [MesRealPostgresFact]
    public async Task Completion_reversal_commits_reopen_state_and_void_outbox_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndInitializeCapAsync(factory);
        await SeedRunningTaskAsync(factory);

        using var commandScope = factory.Services.CreateScope();
        var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
        var completing = await sender.Send(new RecordProductionReportCommand(
            "org-001", "env-dev", "WO-001", "OP-001", 10m, 0m, true,
            At(60), "report-complete-postgres-void-001"));
        await sender.Send(new ReverseProductionReportCommand(
            "org-001", "env-dev", completing.ReportNo, "更正完工报工", At(70),
            "user:operator-001", "report-reverse-postgres-void-001"));

        using var assertionScope = factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = await dbContext.OperationTasks.AsNoTracking().SingleAsync();
        var settlement = await dbContext.OperationActualTimeSettlements
            .AsNoTracking()
            .Include(x => x.CoveredReports)
            .SingleAsync();
        var voidOutbox = Assert.Single(
            await ReadCapOutboxContentAsync(),
            content => content.Contains("mes.OperationActualTimeSettlementVoided", StringComparison.Ordinal));

        Assert.Equal(OperationTaskLifecycleStatus.InProgress, task.Status);
        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Equal(0, task.LaborTimeTicks);
        Assert.Equal(0, task.MachineTimeTicks);
        Assert.Equal(At(70), settlement.VoidedAtUtc);
        Assert.Equal([completing.ReportNo], settlement.CoveredReports.Select(x => x.ReportNo));
        Assert.Contains("\"SettlementRevision\":1", voidOutbox, StringComparison.Ordinal);
        Assert.Contains("\"ActualLaborTicks\":36000000000", voidOutbox, StringComparison.Ordinal);
        Assert.Contains(completing.ReportNo, voidOutbox, StringComparison.Ordinal);
    }

    [MesRealPostgresFact]
    public async Task Settlement_outbox_failure_rolls_back_state_lineage_and_reports_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndInitializeCapAsync(factory);
        await SeedRunningTaskAsync(factory);
        await InstallSettlementOutboxFailureTriggerAsync();

        using (var commandScope = factory.Services.CreateScope())
        {
            var sender = commandScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => sender.Send(
                new RecordProductionReportCommand(
                    "org-001", "env-dev", "WO-001", "OP-001", 10m, 0m, true,
                    At(60), "report-complete-postgres-atomic-failure-001")));
            Assert.Contains("injected settlement outbox failure", exception.ToString(), StringComparison.Ordinal);
        }

        using var assertionScope = factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = await dbContext.OperationTasks.AsNoTracking().SingleAsync();
        Assert.Equal(OperationTaskLifecycleStatus.InProgress, task.Status);
        Assert.Equal(0, task.ActualTimeSettlementRevision);
        Assert.Empty(await dbContext.ProductionReports.AsNoTracking().ToArrayAsync());
        Assert.Empty(await dbContext.OperationActualTimeSettlements.AsNoTracking().ToArrayAsync());
        Assert.DoesNotContain(
            await ReadCapOutboxContentAsync(),
            content => content.Contains("mes.OperationActualTimeSettled", StringComparison.Ordinal));
    }

    [MesRealPostgresFact]
    public async Task Concurrent_completion_rejects_the_stale_settlement_revision_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        await using (var setup = CreateContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.WorkOrders.Add(CreateWorkOrder());
            setup.OperationTasks.Add(CreateRunningTask());
            await setup.SaveChangesAsync();
        }

        await using var winnerContext = CreateContext(options);
        await using var staleContext = CreateContext(options);
        var winner = await winnerContext.OperationTasks.SingleAsync();
        var stale = await staleContext.OperationTasks.SingleAsync();
        winner.Complete(At(60), ["PR-WINNER"]);
        stale.Complete(At(61), ["PR-STALE"]);

        await winnerContext.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());
        await using var assertion = CreateContext(options);
        var persisted = await assertion.OperationTasks.AsNoTracking().SingleAsync();
        Assert.Equal(1, persisted.ActualTimeSettlementRevision);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PostgreSQL"] = MesPostgresLaneDatabase.ConnectionString,
            ["Messaging:Provider"] = "InMemory",
            ["Cap:Version"] = "test-mes-settlement",
            ["InternalService:BearerToken"] = "test-internal-token",
        };
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
        });
    }

    private static async Task MigrateAndInitializeCapAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        MesPostgresLaneDatabase.AssertUsesGovernedDatabase(dbContext);
        await dbContext.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBootstrapper>().BootstrapAsync(CancellationToken.None);
    }

    private static async Task SeedRunningTaskAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.WorkOrders.Add(CreateWorkOrder());
        dbContext.OperationTasks.Add(CreateRunningTask());
        await dbContext.SaveChangesAsync();
    }

    private static WorkOrder CreateWorkOrder() =>
        WorkOrder.Create(
            "org-001", "env-dev", "WO-001", "SKU-001", "PV-001", 10m, 1,
            At(480));

    private static OperationTask CreateRunningTask() =>
        OperationTask.Create(
            "org-001", "env-dev", "WO-001", "OP-001",
            OperationTaskLifecycleStatus.InProgress, 10, "WC-001", [], At(0),
            TimeSpan.FromHours(1), At(0), null);

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static async Task<string[]> ReadCapOutboxContentAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Content\" FROM cap.published";
        await using var reader = await command.ExecuteReaderAsync();
        var content = new List<string>();
        while (await reader.ReadAsync())
        {
            content.Add(reader.GetString(0));
        }

        return content.ToArray();
    }

    private static async Task InstallSettlementOutboxFailureTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE FUNCTION cap.reject_actual_time_settlement_outbox()
            RETURNS trigger AS $$
            BEGIN
                IF NEW."Content" LIKE '%mes.OperationActualTimeSettled%' THEN
                    RAISE EXCEPTION 'injected settlement outbox failure';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_actual_time_settlement_outbox
            BEFORE INSERT ON cap.published
            FOR EACH ROW EXECUTE FUNCTION cap.reject_actual_time_settlement_outbox();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static DateTimeOffset At(int minute) =>
        DateTimeOffset.Parse("2026-08-26T01:00:00Z").AddMinutes(minute);
}
