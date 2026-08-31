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
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.ProductionReportAggregate;
using Nerv.IIP.Business.Mes.Domain.AggregatesModel.WorkOrderAggregate;
using Nerv.IIP.Business.Mes.Domain.DomainEvents;
using Nerv.IIP.Business.Mes.Infrastructure;
using Nerv.IIP.Business.Mes.Web.Application.Commands.Production;
using Nerv.IIP.Business.Mes.Web.Application.Quality;
using Nerv.IIP.Contracts.Mes;
using Npgsql;

namespace Nerv.IIP.Business.Mes.Web.Tests;

[Collection(MesPostgresLaneDatabase.CollectionName)]
public sealed class OperationActualTimeSettlementPostgresTests
{
    private const string SettledV1Topic = "nerv-iip.development.business-mes.mes.operation-actual-time-settled.v1";
    private const string SettledV2Topic = "nerv-iip.development.business-mes.mes.operation-actual-time-settled.v2";
    private const string VoidedV1Topic = "nerv-iip.development.business-mes.mes.operation-actual-time-settlement-voided.v1";
    private const string VoidedV2Topic = "nerv-iip.development.business-mes.mes.operation-actual-time-settlement-voided.v2";

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
        var settlementOutboxes = (await ReadCapOutboxContentAsync())
            .Where(content => content.Contains("mes.OperationActualTimeSettled", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, settlementOutboxes.Length);
        var settlementOutbox = Assert.Single(settlementOutboxes,
            content => content.StartsWith(SettledV2Topic, StringComparison.Ordinal));
        Assert.Contains(settlementOutboxes,
            content => content.StartsWith(SettledV1Topic, StringComparison.Ordinal)
                && content.Contains("\"EventVersion\":1", StringComparison.Ordinal));
        Assert.Contains(settlementOutboxes,
            content => content.StartsWith(nameof(MesOperationActualTimeSettledIntegrationEvent), StringComparison.Ordinal)
                && content.Contains("\"EventVersion\":1", StringComparison.Ordinal));

        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Equal("DEVICE-001", settlement.DeviceAssetId);
        Assert.Equal(MachineTimeFactStatus.Available, settlement.MachineTimeStatus);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, settlement.BillableMachineTicks);
        Assert.Equal(MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1, settlement.MachineTimeBasisCode);
        Assert.Equal(
            new[] { staged.ReportNo, completing.ReportNo }.Order(StringComparer.Ordinal),
            settlement.CoveredReports.Select(x => x.ReportNo).Order(StringComparer.Ordinal));
        Assert.Null(settlement.VoidedAtUtc);
        Assert.Contains("\"SettlementRevision\":1", settlementOutbox, StringComparison.Ordinal);
        Assert.Contains("\"EventVersion\":2", settlementOutbox, StringComparison.Ordinal);
        Assert.Contains("\"DeviceAssetId\":\"DEVICE-001\"", settlementOutbox, StringComparison.Ordinal);
        Assert.Contains("\"MachineTimeStatus\":\"available\"", settlementOutbox, StringComparison.Ordinal);
        Assert.Contains("\"BillableMachineTicks\":36000000000", settlementOutbox, StringComparison.Ordinal);
        Assert.Contains(staged.ReportNo, settlementOutbox, StringComparison.Ordinal);
        Assert.Contains(completing.ReportNo, settlementOutbox, StringComparison.Ordinal);
        await AssertMachineFactCheckRejectsIllegalRowsAsync(settlement.Id.Id);
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
        var voidOutboxes = (await ReadCapOutboxContentAsync())
            .Where(content => content.Contains("mes.OperationActualTimeSettlementVoided", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, voidOutboxes.Length);
        var voidOutbox = Assert.Single(voidOutboxes,
            content => content.StartsWith(VoidedV2Topic, StringComparison.Ordinal));
        Assert.Contains(voidOutboxes,
            content => content.StartsWith(VoidedV1Topic, StringComparison.Ordinal)
                && content.Contains("\"EventVersion\":1", StringComparison.Ordinal));
        Assert.Contains(voidOutboxes,
            content => content.StartsWith(nameof(MesOperationActualTimeSettlementVoidedIntegrationEvent), StringComparison.Ordinal)
                && content.Contains("\"EventVersion\":1", StringComparison.Ordinal));

        Assert.Equal(OperationTaskLifecycleStatus.InProgress, task.Status);
        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Equal(0, task.LaborTimeTicks);
        Assert.Equal(0, task.MachineTimeTicks);
        Assert.Equal(At(70), settlement.VoidedAtUtc);
        Assert.Equal("DEVICE-001", settlement.DeviceAssetId);
        Assert.Equal(MachineTimeFactStatus.Available, settlement.MachineTimeStatus);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, settlement.BillableMachineTicks);
        Assert.Equal([completing.ReportNo], settlement.CoveredReports.Select(x => x.ReportNo));
        Assert.Contains("\"SettlementRevision\":1", voidOutbox, StringComparison.Ordinal);
        Assert.Contains("\"ActualLaborTicks\":36000000000", voidOutbox, StringComparison.Ordinal);
        Assert.Contains("\"DeviceAssetId\":\"DEVICE-001\"", voidOutbox, StringComparison.Ordinal);
        Assert.Contains("\"BillableMachineTicks\":36000000000", voidOutbox, StringComparison.Ordinal);
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
            Assert.Contains("injected second-version settlement outbox failure after V1", exception.ToString(), StringComparison.Ordinal);
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
    public async Task Void_outbox_failure_rolls_back_task_settlement_and_reversal_report_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        await using var factory = CreateFactory();
        await MigrateAndInitializeCapAsync(factory);
        await SeedRunningTaskAsync(factory);

        string completedReportNo;
        using (var completionScope = factory.Services.CreateScope())
        {
            var sender = completionScope.ServiceProvider.GetRequiredService<ISender>();
            completedReportNo = (await sender.Send(new RecordProductionReportCommand(
                "org-001", "env-dev", "WO-001", "OP-001", 10m, 0m, true,
                At(60), "report-complete-postgres-void-failure-001"))).ReportNo;
        }

        await InstallVoidOutboxFailureTriggerAsync();
        using (var reversalScope = factory.Services.CreateScope())
        {
            var sender = reversalScope.ServiceProvider.GetRequiredService<ISender>();
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => sender.Send(
                new ReverseProductionReportCommand(
                    "org-001", "env-dev", completedReportNo, "故障注入", At(70),
                    "user:operator-001", "report-reverse-postgres-void-failure-001")));
            Assert.Contains("injected second-version void outbox failure after V1", exception.ToString(), StringComparison.Ordinal);
        }

        using var assertionScope = factory.Services.CreateScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = await dbContext.OperationTasks.AsNoTracking().SingleAsync();
        var settlement = await dbContext.OperationActualTimeSettlements.AsNoTracking().SingleAsync();
        var reports = await dbContext.ProductionReports.AsNoTracking().ToArrayAsync();
        Assert.Equal(OperationTaskLifecycleStatus.Completed, task.Status);
        Assert.Equal(1, task.ActualTimeSettlementRevision);
        Assert.Null(settlement.VoidedAtUtc);
        Assert.Single(reports);
        Assert.Equal(completedReportNo, reports[0].ReportNo);
        Assert.False(reports[0].IsReversal);
        Assert.DoesNotContain(
            await ReadCapOutboxContentAsync(),
            content => content.Contains("mes.OperationActualTimeSettlementVoided", StringComparison.Ordinal));
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

    [MesRealPostgresFact]
    public async Task Settlement_void_before_completion_is_rejected_by_named_check_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        OperationActualTimeSettlement settlement;
        await using (var setup = CreateContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.WorkOrders.Add(CreateWorkOrder());
            var task = CreateRunningTask();
            setup.OperationTasks.Add(task);
            task.Complete(At(60), []);
            settlement = OperationActualTimeSettlement.Capture(
                Assert.Single(task.GetDomainEvents()
                    .OfType<OperationActualTimeSettledDomainEvent>()).Settlement);
            setup.OperationActualTimeSettlements.Add(settlement);
            await setup.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mes.operation_actual_time_settlements
            SET voided_at_utc = completed_at_utc - INTERVAL '1 second'
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("id", settlement.Id.Id);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("ck_operation_actual_time_settlements_void_order", exception.ConstraintName);
    }

    [MesRealPostgresFact]
    public async Task Settlement_lineage_rejects_a_report_from_another_scope_or_task_on_postgres()
    {
        await MesPostgresLaneDatabase.ResetSchemaAsync();
        var options = MesPostgresLaneDatabase.CreateOptions();
        OperationActualTimeSettlement settlement;
        OperationActualTimeSettlement environmentSettlement;
        await using (var setup = CreateContext(options))
        {
            MesPostgresLaneDatabase.AssertUsesGovernedDatabase(setup);
            await setup.Database.MigrateAsync();
            setup.WorkOrders.Add(CreateWorkOrder());
            var task = CreateRunningTask();
            setup.OperationTasks.Add(task);
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-002", "env-dev", "WO-002", "SKU-002", "PV-002", 10m, 1, At(480)));
            setup.OperationTasks.Add(OperationTask.Create(
                "org-002", "env-dev", "WO-002", "OP-002",
                OperationTaskLifecycleStatus.InProgress, 10, "WC-002", [], At(0),
                TimeSpan.FromHours(1), At(0), null));
            setup.ProductionReports.Add(ProductionReport.Record(
                "org-002", "env-dev", "PR-OTHER", "WO-002", "OP-002",
                1m, 0m, false, At(30)));
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-dev", "WO-003", "SKU-003", "PV-003", 10m, 1, At(480)));
            setup.OperationTasks.Add(OperationTask.Create(
                "org-001", "env-dev", "WO-003", "OP-003",
                OperationTaskLifecycleStatus.InProgress, 10, "WC-003", [], At(0),
                TimeSpan.FromHours(1), At(0), null));
            setup.ProductionReports.Add(ProductionReport.Record(
                "org-001", "env-dev", "PR-OTHER-TASK", "WO-003", "OP-003",
                1m, 0m, false, At(30)));
            setup.ProductionReports.Add(ProductionReport.Record(
                "org-001", "env-dev", "PR-ENV-DEV", "WO-001", "OP-001",
                1m, 0m, false, At(30)));
            setup.WorkOrders.Add(WorkOrder.Create(
                "org-001", "env-other", "WO-001", "SKU-001", "PV-001", 10m, 1, At(480)));
            var environmentTask = OperationTask.Create(
                "org-001", "env-other", "WO-001", "OP-001",
                OperationTaskLifecycleStatus.InProgress, 10, "WC-001", [], At(0),
                TimeSpan.FromHours(1), At(0), null);
            setup.OperationTasks.Add(environmentTask);
            setup.ProductionReports.Add(ProductionReport.Record(
                "org-001", "env-other", "PR-ENV-OTHER", "WO-001", "OP-001",
                1m, 0m, false, At(30)));
            task.Complete(At(60), []);
            settlement = OperationActualTimeSettlement.Capture(
                Assert.Single(task.GetDomainEvents()
                    .OfType<OperationActualTimeSettledDomainEvent>()).Settlement);
            setup.OperationActualTimeSettlements.Add(settlement);
            environmentTask.Complete(At(60), []);
            environmentSettlement = OperationActualTimeSettlement.Capture(
                Assert.Single(environmentTask.GetDomainEvents()
                    .OfType<OperationActualTimeSettledDomainEvent>()).Settlement);
            setup.OperationActualTimeSettlements.Add(environmentSettlement);
            await setup.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await AssertLineageRejectedAsync(
            connection,
            settlement.Id.Id,
            "org-002",
            "env-dev",
            "WO-002",
            "OP-002",
            "PR-OTHER",
            "fk_operation_actual_time_settlement_reports_settlement");
        await AssertLineageRejectedAsync(
            connection,
            settlement.Id.Id,
            "org-001",
            "env-dev",
            "WO-003",
            "OP-003",
            "PR-OTHER-TASK",
            "fk_operation_actual_time_settlement_reports_settlement");
        await AssertLineageRejectedAsync(
            connection,
            settlement.Id.Id,
            "org-001",
            "env-dev",
            "WO-001",
            "OP-001",
            "PR-OTHER-TASK",
            "fk_operation_actual_time_settlement_reports_production_reports");
        await AssertLineageRejectedAsync(
            connection,
            settlement.Id.Id,
            "org-001",
            "env-other",
            "WO-001",
            "OP-001",
            "PR-ENV-OTHER",
            "fk_operation_actual_time_settlement_reports_settlement");
        await AssertLineageRejectedAsync(
            connection,
            environmentSettlement.Id.Id,
            "org-001",
            "env-other",
            "WO-001",
            "OP-001",
            "PR-ENV-DEV",
            "fk_operation_actual_time_settlement_reports_production_reports");
    }

    private static async Task AssertLineageRejectedAsync(
        NpgsqlConnection connection,
        Guid settlementId,
        string organizationId,
        string environmentId,
        string workOrderId,
        string operationTaskId,
        string reportNo,
        string expectedConstraintName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mes.operation_actual_time_settlement_reports
                (id, settlement_id, organization_id, environment_id, work_order_id, operation_task_id, report_no)
            VALUES
                (@id, @settlement_id, @organization_id, @environment_id, @work_order_id, @operation_task_id, @report_no)
            """;
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("settlement_id", settlementId);
        command.Parameters.AddWithValue("organization_id", organizationId);
        command.Parameters.AddWithValue("environment_id", environmentId);
        command.Parameters.AddWithValue("work_order_id", workOrderId);
        command.Parameters.AddWithValue("operation_task_id", operationTaskId);
        command.Parameters.AddWithValue("report_no", reportNo);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(expectedConstraintName, exception.ConstraintName);
    }

    private static async Task AssertMachineFactCheckRejectsIllegalRowsAsync(Guid settlementId)
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        var illegalFacts = new (string Status, string? Device, long? Ticks, string? Basis)[]
        {
            ("Available", null, 0, MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1),
            ("Available", "DEVICE-001", null, MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1),
            ("Available", "DEVICE-001", 0, null),
            ("Available", "DEVICE-001", -1, MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1),
            ("Available", "DEVICE-001", 0, "non-canonical-basis"),
            ("Unavailable", "DEVICE-001", null, null),
            ("Unavailable", null, 0, null),
            ("Unavailable", null, null, MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1),
            ("NotApplicable", "DEVICE-001", null, null),
            ("NotApplicable", null, 0, null),
            ("NotApplicable", null, null, MachineTimeBasisCodes.SingleDeviceActiveMinusExplicitPauseV1),
            ("Unknown", null, null, null),
        };

        foreach (var fact in illegalFacts)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE mes.operation_actual_time_settlements
                SET machine_time_status = @status,
                    device_asset_id = @device,
                    billable_machine_ticks = @ticks,
                    machine_time_basis_code = @basis
                WHERE id = @id
                """;
            command.Parameters.AddWithValue("id", settlementId);
            command.Parameters.AddWithValue("status", fact.Status);
            command.Parameters.AddWithValue("device", (object?)fact.Device ?? DBNull.Value);
            command.Parameters.AddWithValue("ticks", (object?)fact.Ticks ?? DBNull.Value);
            command.Parameters.AddWithValue("basis", (object?)fact.Basis ?? DBNull.Value);

            var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
            Assert.Equal("ck_operation_actual_time_settlements_machine_fact", exception.ConstraintName);
        }
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
            // 本用例的被测对象是工时结算与出站消息，而它要在同一工序上连报两次工——
            // 第二次会命中首件门禁（#2780）并去同步问 Quality，本 lane 里没有 Quality 在跑。
            builder.ConfigureServices(services =>
                services.AddScoped<IMesFirstArticleGate>(_ => TestMesFirstArticleGate.Allowing));
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

    private static OperationTask CreateRunningTask()
    {
        var task = OperationTask.Queue(
            "org-001", "env-dev", "WO-001", "OP-001",
            10, "WC-001", [], At(0), TimeSpan.FromHours(1));
        task.Assign("operator-001", "DEVICE-001", "SHIFT-1", At(-5));
        task.Start(At(0));
        return task;
    }

    private static ApplicationDbContext CreateContext(DbContextOptions<ApplicationDbContext> options) =>
        new(options, new NoopMediator());

    private static async Task<string[]> ReadCapOutboxContentAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Name\", \"Content\" FROM cap.published";
        await using var reader = await command.ExecuteReaderAsync();
        var content = new List<string>();
        while (await reader.ReadAsync())
        {
            content.Add($"{reader.GetString(0)}\n{reader.GetString(1)}");
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
                IF NEW."Name" = 'nerv-iip.development.business-mes.mes.operation-actual-time-settled.v2'
                   AND EXISTS (
                       SELECT 1 FROM cap.published
                       WHERE "Name" = 'nerv-iip.development.business-mes.mes.operation-actual-time-settled.v1') THEN
                    RAISE EXCEPTION 'injected second-version settlement outbox failure after V1';
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

    private static async Task InstallVoidOutboxFailureTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(MesPostgresLaneDatabase.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE FUNCTION cap.reject_actual_time_settlement_void_outbox()
            RETURNS trigger AS $$
            BEGIN
                IF NEW."Name" = 'nerv-iip.development.business-mes.mes.operation-actual-time-settlement-voided.v2'
                   AND EXISTS (
                       SELECT 1 FROM cap.published
                       WHERE "Name" = 'nerv-iip.development.business-mes.mes.operation-actual-time-settlement-voided.v1') THEN
                    RAISE EXCEPTION 'injected second-version void outbox failure after V1';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_actual_time_settlement_void_outbox
            BEFORE INSERT ON cap.published
            FOR EACH ROW EXECUTE FUNCTION cap.reject_actual_time_settlement_void_outbox();
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static DateTimeOffset At(int minute) =>
        DateTimeOffset.Parse("2026-08-26T01:00:00Z").AddMinutes(minute);
}
